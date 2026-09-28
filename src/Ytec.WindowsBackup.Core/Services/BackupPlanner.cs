using Ytec.WindowsBackup.Core.Models;

namespace Ytec.WindowsBackup.Core.Services;

public sealed class BackupPlanner
{
    private const int MaximumWarnings = 100;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string, string?> _destinationFileSystemResolver;

    public BackupPlanner(
        Func<DateTimeOffset>? clock = null,
        Func<string, string?>? destinationFileSystemResolver = null)
    {
        _clock = clock ?? (() => DateTimeOffset.Now);
        _destinationFileSystemResolver =
            destinationFileSystemResolver ?? ResolveDestinationFileSystem;
    }

    public BackupPlan CreatePlan(
        string sourcePath,
        string destinationPath,
        BackupCatalog catalog,
        IReadOnlyCollection<string> selectedItemIds,
        string? requestedJobName = null,
        CancellationToken cancellationToken = default)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (selectedItemIds is null)
        {
            throw new ArgumentNullException(nameof(selectedItemIds));
        }

        var sourceRoot = BackupPathPolicy.NormalizeExistingDirectory(sourcePath, nameof(sourcePath));
        var destinationRoot =
            BackupPathPolicy.NormalizeExistingDirectory(destinationPath, nameof(destinationPath));
        BackupPathPolicy.EnsureDestinationOutsideSource(sourceRoot, destinationRoot);
        var jobName = BackupFolderNamePolicy.Validate(
            requestedJobName ?? $"YTEC_Backup_{_clock():yyyyMMdd_HHmmss}");
        var outputDirectory =
            BackupFolderNamePolicy.GetOutputDirectory(destinationRoot, jobName);
        BackupFolderNamePolicy.EnsureDoesNotExist(outputDirectory);

        var sourceDetection = WindowsSourceDetector.Detect(sourceRoot);
        var layout = sourceDetection.Layout;
        var profilesRoot = sourceDetection.ProfilesRoot;
        var profiles = WindowsSourceDetector.GetUserProfileDirectories(layout, profilesRoot);

        var selectedIds = new HashSet<string>(
            selectedItemIds,
            StringComparer.OrdinalIgnoreCase);
        if (selectedIds.Count == 0)
        {
            throw new InvalidOperationException("バックアップ項目を1つ以上選択してください。");
        }

        var selectedItems = catalog.Items
            .Where(item => selectedIds.Contains(item.Id))
            .ToArray();
        var missingIds = selectedIds
            .Where(id => selectedItems.All(item =>
                !string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (missingIds.Length > 0)
        {
            throw new InvalidOperationException(
                $"設定にないバックアップ項目が選択されました: {string.Join(", ", missingIds)}");
        }

        var catalogExcludedNames = new HashSet<string>(
            catalog.ExcludedFileNames,
            StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var candidates =
            new Dictionary<string, CandidateAccumulator>(StringComparer.OrdinalIgnoreCase);
        var onlineOnlySkips = new OnlineOnlySkipAccumulator();

        var fileItems = selectedItems
            .Where(item => item.Kind == BackupItemKind.FileCopy)
            .ToArray();
        var supplementItemIds = selectedItems
            .Where(item => item.Kind != BackupItemKind.FileCopy)
            .Select(item => item.Id)
            .ToArray();
        foreach (var item in fileItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scopeRoots = item.Scope switch
            {
                BackupScope.PerUser => profiles,
                BackupScope.SourceRoot => [sourceRoot],
                _ => throw new InvalidDataException($"未対応のscopeです: {item.Scope}"),
            };

            var extensions = new HashSet<string>(
                item.Extensions.Select(extension => extension.ToUpperInvariant()),
                StringComparer.OrdinalIgnoreCase);
            var excludedNames = new HashSet<string>(
                catalogExcludedNames,
                StringComparer.OrdinalIgnoreCase);
            excludedNames.UnionWith(item.ExcludedFileNames);

            foreach (var scopeRoot in scopeRoots)
            {
                foreach (var configuredPath in item.Paths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var scanRoot in ExpandConfiguredPath(scopeRoot, configuredPath, warnings))
                    {
                        ScanPath(
                            sourceRoot,
                            scanRoot,
                            item,
                            extensions,
                            excludedNames,
                            candidates,
                            warnings,
                            onlineOnlySkips,
                            cancellationToken);
                    }
                }
            }
        }

        var orderedCandidates = candidates.Values
            .Select(value => value.ToCandidate())
            .OrderBy(candidate => candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        long? availableBytes = TryGetAvailableBytes(destinationRoot, warnings);
        var destinationFileSystem =
            CheckDestinationFileSystem(destinationRoot, warnings);
        var totalBytes = orderedCandidates.Sum(candidate => candidate.Length);
        if (onlineOnlySkips.Files > 0)
        {
            AddWarning(
                warnings,
                $"オンライン専用ファイル {onlineOnlySkips.Files:N0}件" +
                $"（表示サイズ {FileSizeFormatter.Format(onlineOnlySkips.LogicalBytes)}）は" +
                "対象外です。OneDrive / iCloud等でPC内にダウンロード済みの" +
                "ファイルだけをコピーします。");
        }

        if (availableBytes is not null && availableBytes < totalBytes)
        {
            AddWarning(
                warnings,
                $"空き容量が不足しています。必要 {FileSizeFormatter.Format(totalBytes)} / 空き {FileSizeFormatter.Format(availableBytes.Value)}");
        }

        if (orderedCandidates.Length == 0)
        {
            if (supplementItemIds.Length == 0)
            {
                AddWarning(warnings, "選択した項目にコピー対象ファイルがありません。");
            }
            else
            {
                AddWarning(
                    warnings,
                    "ファイルコピー対象はありません。選択したWindows設定だけを保存します。");
            }
        }

        if (supplementItemIds.Contains(
            "wifi-settings",
            StringComparer.OrdinalIgnoreCase))
        {
            AddWarning(
                warnings,
                "Wi-Fi設定は元ドライブではなく、現在このアプリを実行しているPCから取得します。");
        }

        return new BackupPlan
        {
            JobName = jobName,
            SourceRoot = sourceRoot,
            DestinationRoot = destinationRoot,
            SourceLayout = layout,
            SelectedItemIds = selectedItems.Select(item => item.Id).ToArray(),
            SupplementItemIds = supplementItemIds,
            Candidates = orderedCandidates,
            Warnings = warnings,
            SkippedOnlineOnlyFiles = onlineOnlySkips.Files,
            SkippedOnlineOnlyBytes = onlineOnlySkips.LogicalBytes,
            AvailableBytes = availableBytes,
            DestinationFileSystem = destinationFileSystem.Name,
            DestinationSupportsAccessControl =
                destinationFileSystem.SupportsAccessControl,
            DestinationAllowsBackup = destinationFileSystem.AllowsBackup,
        };
    }

    private static IEnumerable<string> ExpandConfiguredPath(
        string scopeRoot,
        string configuredPath,
        List<string> warnings)
    {
        BackupPathPolicy.EnsureSafeRelativeDefinitionPath(configuredPath);
        var segments = configuredPath
            .Replace('\\', '/')
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var current = new List<string> { scopeRoot };

        for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            var segment = segments[segmentIndex];
            var isLastSegment = segmentIndex == segments.Length - 1;
            var hasWildcard = segment.Contains('*') || segment.Contains('?');
            var next = new List<string>();
            foreach (var parent in current)
            {
                if (!Directory.Exists(parent) || IsReparsePoint(parent))
                {
                    continue;
                }

                if (!hasWildcard)
                {
                    var exactPath = BackupPathPolicy.EnsurePathUnderRoot(
                        scopeRoot,
                        Path.Combine(parent, segment),
                        "設定パスがユーザーまたは元ドライブの外側を指しています。");
                    if ((isLastSegment && (Directory.Exists(exactPath) || File.Exists(exactPath))) ||
                        (!isLastSegment && Directory.Exists(exactPath)))
                    {
                        if (IsReparsePoint(exactPath))
                        {
                            AddWarning(warnings, $"再解析ポイントを除外しました: {configuredPath}");
                        }
                        else
                        {
                            next.Add(exactPath);
                        }
                    }

                    continue;
                }

                string[] entries;
                try
                {
                    entries = isLastSegment
                        ? Directory.GetFileSystemEntries(
                            parent,
                            segment,
                            SearchOption.TopDirectoryOnly)
                        : Directory.GetDirectories(
                            parent,
                            segment,
                            SearchOption.TopDirectoryOnly);
                }
                catch (Exception exception) when (
                    exception is UnauthorizedAccessException or IOException)
                {
                    AddWarning(warnings, $"候補パスを列挙できません: {configuredPath}");
                    continue;
                }

                foreach (var entry in entries.OrderBy(
                    path => path,
                    StringComparer.OrdinalIgnoreCase))
                {
                    var safeEntry = BackupPathPolicy.EnsurePathUnderRoot(
                        scopeRoot,
                        entry,
                        "展開した設定パスがユーザーまたは元ドライブの外側を指しています。");
                    if (IsReparsePoint(safeEntry))
                    {
                        AddWarning(warnings, $"再解析ポイントを除外しました: {configuredPath}");
                        continue;
                    }

                    next.Add(safeEntry);
                }
            }

            current = next
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (current.Count == 0)
            {
                break;
            }
        }

        foreach (var entry in current)
        {
            yield return entry;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }

    private static void ScanPath(
        string sourceRoot,
        string scanRoot,
        BackupItemDefinition item,
        HashSet<string> extensions,
        HashSet<string> excludedNames,
        Dictionary<string, CandidateAccumulator> candidates,
        List<string> warnings,
        OnlineOnlySkipAccumulator onlineOnlySkips,
        CancellationToken cancellationToken)
    {
        if (File.Exists(scanRoot))
        {
            AddFileIfEligible(
                sourceRoot,
                scanRoot,
                item,
                extensions,
                excludedNames,
                candidates,
                warnings,
                onlineOnlySkips);
            return;
        }

        if (!Directory.Exists(scanRoot))
        {
            return;
        }

        if (item.SelectionMode == BackupSelectionMode.FilesOnly)
        {
            return;
        }

        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(scanRoot);
        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pendingDirectories.Pop();
            FileAttributes directoryAttributes;
            try
            {
                directoryAttributes = File.GetAttributes(directory);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException)
            {
                AddWarning(warnings, $"フォルダー属性を確認できません: {ToRelativeSafe(sourceRoot, directory)}");
                continue;
            }

            if ((directoryAttributes & FileAttributes.ReparsePoint) != 0)
            {
                AddWarning(warnings, $"再解析ポイントを除外しました: {ToRelativeSafe(sourceRoot, directory)}");
                continue;
            }

            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(directory);
                directories = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException)
            {
                AddWarning(warnings, $"フォルダーを読み取れません: {ToRelativeSafe(sourceRoot, directory)}");
                continue;
            }

            foreach (var file in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddFileIfEligible(
                    sourceRoot,
                    file,
                    item,
                    extensions,
                    excludedNames,
                    candidates,
                    warnings,
                    onlineOnlySkips);
            }

            foreach (var child in directories.OrderByDescending(
                path => path,
                StringComparer.OrdinalIgnoreCase))
            {
                pendingDirectories.Push(child);
            }
        }
    }

    private static void AddFileIfEligible(
        string sourceRoot,
        string file,
        BackupItemDefinition item,
        HashSet<string> extensions,
        HashSet<string> excludedNames,
        Dictionary<string, CandidateAccumulator> candidates,
        List<string> warnings,
        OnlineOnlySkipAccumulator onlineOnlySkips)
    {
        if (excludedNames.Contains(Path.GetFileName(file)))
        {
            return;
        }

        if (item.SelectionMode == BackupSelectionMode.ExtensionsOnly &&
            !extensions.Contains(Path.GetExtension(file)))
        {
            return;
        }

        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(file);
            if (CloudFileAvailability.IsNotFullyPresentLocally(
                fileInfo.Attributes))
            {
                onlineOnlySkips.Add(fileInfo.Length);
                return;
            }

            if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                AddWarning(warnings, $"再解析ポイントのファイルを除外しました: {ToRelativeSafe(sourceRoot, file)}");
                return;
            }
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException)
        {
            AddWarning(warnings, $"ファイル情報を読み取れません: {ToRelativeSafe(sourceRoot, file)}");
            return;
        }

        var fullPath = BackupPathPolicy.EnsurePathUnderRoot(
            sourceRoot,
            file,
            "コピー候補が元ドライブの外側を指しています。");
        var relativePath = BackupPathPolicy.GetRelativePath(sourceRoot, fullPath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("コピー候補の相対パスが不正です。");
        }

        var destinationRelativePath = GetDestinationRelativePath(
            item,
            fileInfo.Extension,
            relativePath);
        var candidateKey = fullPath + "\0" + destinationRelativePath;
        if (candidates.TryGetValue(candidateKey, out var existing))
        {
            existing.AddItem(item.Id);
            return;
        }

        candidates[candidateKey] = new CandidateAccumulator(
            item.Id,
            fullPath,
            destinationRelativePath,
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc);
    }

    private sealed class OnlineOnlySkipAccumulator
    {
        public int Files { get; private set; }

        public long LogicalBytes { get; private set; }

        public void Add(long logicalBytes)
        {
            Files++;
            LogicalBytes += logicalBytes;
        }
    }

    private static string GetDestinationRelativePath(
        BackupItemDefinition item,
        string extension,
        string sourceRelativePath)
    {
        var routeParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.DestinationFolder))
        {
            routeParts.Add(NormalizeDefinitionPath(item.DestinationFolder!));
        }

        var extensionFolder = item.ExtensionDestinationFolders
            .FirstOrDefault(route =>
                route.Key.Equals(extension, StringComparison.OrdinalIgnoreCase))
            .Value;
        if (!string.IsNullOrWhiteSpace(extensionFolder))
        {
            routeParts.Add(NormalizeDefinitionPath(extensionFolder));
        }

        routeParts.Add(sourceRelativePath);
        return Path.Combine(routeParts.ToArray());
    }

    private static string NormalizeDefinitionPath(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

    private static long? TryGetAvailableBytes(string destinationRoot, List<string> warnings)
    {
        try
        {
            var driveRoot = Path.GetPathRoot(destinationRoot);
            if (string.IsNullOrWhiteSpace(driveRoot))
            {
                AddWarning(warnings, "バックアップ先の空き容量を自動確認できません。");
                return null;
            }

            return new DriveInfo(driveRoot).AvailableFreeSpace;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            AddWarning(warnings, "バックアップ先の空き容量を自動確認できません。");
            return null;
        }
    }

    private DestinationFileSystemCheck CheckDestinationFileSystem(
        string destinationRoot,
        List<string> warnings)
    {
        try
        {
            var format = _destinationFileSystemResolver(destinationRoot);
            if (string.IsNullOrWhiteSpace(format))
            {
                AddWarning(
                    warnings,
                    "バックアップ先がEveryone所有者・ACLに対応するか自動確認できません。");
                return new DestinationFileSystemCheck(null, null, true);
            }

            var normalizedFormat = format!.Trim();
            if (normalizedFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase) ||
                normalizedFormat.Equals("ReFS", StringComparison.OrdinalIgnoreCase))
            {
                return new DestinationFileSystemCheck(normalizedFormat, true, true);
            }

            if (normalizedFormat.Equals("exFAT", StringComparison.OrdinalIgnoreCase))
            {
                AddWarning(
                    warnings,
                    "バックアップ先はexFATです。コピーは実行できますが、" +
                    "所有者とEveryoneフルアクセスACLはexFATへ保存できないため適用しません。");
                return new DestinationFileSystemCheck(normalizedFormat, false, true);
            }

            AddWarning(
                warnings,
                $"バックアップ先は{normalizedFormat}です。安全な対応を確認できないため、" +
                "NTFS、ReFS、またはexFATの保存先を使用してください。");
            return new DestinationFileSystemCheck(normalizedFormat, false, false);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            AddWarning(
                warnings,
                "バックアップ先がEveryone所有者・ACLに対応するか自動確認できません。");
            return new DestinationFileSystemCheck(null, null, true);
        }
    }

    private static string? ResolveDestinationFileSystem(string destinationRoot)
    {
        var driveRoot = Path.GetPathRoot(destinationRoot);
        return string.IsNullOrWhiteSpace(driveRoot)
            ? null
            : new DriveInfo(driveRoot).DriveFormat;
    }

    private static string ToRelativeSafe(string sourceRoot, string path)
    {
        try
        {
            return BackupPathPolicy.ToManifestPath(
                BackupPathPolicy.GetRelativePath(sourceRoot, path));
        }
        catch
        {
            return Path.GetFileName(path);
        }
    }

    private static void AddWarning(List<string> warnings, string warning)
    {
        if (warnings.Count >= MaximumWarnings ||
            warnings.Contains(warning, StringComparer.Ordinal))
        {
            return;
        }

        warnings.Add(warning);
    }

    private sealed class CandidateAccumulator
    {
        private readonly HashSet<string> _itemIds = new(StringComparer.OrdinalIgnoreCase);

        public CandidateAccumulator(
            string itemId,
            string sourcePath,
            string relativePath,
            long length,
            DateTime lastWriteTimeUtc)
        {
            _itemIds.Add(itemId);
            SourcePath = sourcePath;
            RelativePath = relativePath;
            Length = length;
            LastWriteTimeUtc = lastWriteTimeUtc;
        }

        private string SourcePath { get; }

        private string RelativePath { get; }

        private long Length { get; }

        private DateTime LastWriteTimeUtc { get; }

        public void AddItem(string itemId) => _itemIds.Add(itemId);

        public BackupCandidate ToCandidate() =>
            new(
                _itemIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
                SourcePath,
                RelativePath,
                Length,
                LastWriteTimeUtc);
    }

    private sealed record DestinationFileSystemCheck(
        string? Name,
        bool? SupportsAccessControl,
        bool AllowsBackup);
}
