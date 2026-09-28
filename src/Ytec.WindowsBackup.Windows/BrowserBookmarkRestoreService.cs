using System.Diagnostics;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ytec.WindowsBackup.Core.Services;

namespace Ytec.WindowsBackup.Windows;

public sealed class BrowserBookmarkRestoreService
{
    private const int MaximumBookmarksBytes = 128 * 1024 * 1024;
    private static readonly byte[] MozLz4Header =
        Encoding.ASCII.GetBytes("mozLz40\0");
    private readonly BrowserRestoreEnvironment _environment;
    private readonly Func<string, bool> _isProcessRunning;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _rollbackRoot;

    public BrowserBookmarkRestoreService(
        BrowserRestoreEnvironment? environment = null,
        Func<string, bool>? isProcessRunning = null,
        Func<DateTimeOffset>? clock = null,
        string? rollbackRoot = null)
    {
        _environment = environment ?? BrowserRestoreEnvironment.CurrentUser();
        _isProcessRunning = isProcessRunning ?? IsProcessRunning;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _rollbackRoot = rollbackRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Y-TEC",
            "WindowsBackup",
            "bookmark-restore-rollbacks");
    }

    public IReadOnlyList<BrowserBookmarkBackupEntry> DiscoverBackups(
        string backupJobDirectory)
    {
        var jobRoot = BackupPathPolicy.NormalizeExistingDirectory(
            backupJobDirectory,
            nameof(backupJobDirectory));
        if ((File.GetAttributes(jobRoot) & FileAttributes.ReparsePoint) != 0 ||
            !File.Exists(Path.Combine(jobRoot, "backup-manifest.v1.json")))
        {
            throw new InvalidDataException(
                "このアプリが作成したデータバックアップフォルダーではありません。");
        }

        var dataRoot = BackupPathPolicy.EnsurePathUnderRoot(
            jobRoot,
            Path.Combine(jobRoot, "data"),
            "バックアップデータの場所が不正です。");
        if (!Directory.Exists(dataRoot) ||
            (File.GetAttributes(dataRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("バックアップ内にdataフォルダーがありません。");
        }

        var entries = new List<BrowserBookmarkBackupEntry>();
        foreach (var profilesRootName in new[] { "Users", "Documents and Settings" })
        {
            var profilesRoot = Path.Combine(dataRoot, profilesRootName);
            foreach (var userDirectory in EnumerateSafeDirectories(profilesRoot))
            {
                var user = Path.GetFileName(userDirectory);
                AddChromiumEntries(
                    entries,
                    dataRoot,
                    userDirectory,
                    user,
                    BrowserBookmarkKind.Chrome,
                    Path.Combine("AppData", "Local", "Google", "Chrome", "User Data"),
                    Path.Combine(
                        "Local Settings",
                        "Application Data",
                        "Google",
                        "Chrome",
                        "User Data"));
                AddChromiumEntries(
                    entries,
                    dataRoot,
                    userDirectory,
                    user,
                    BrowserBookmarkKind.Edge,
                    Path.Combine("AppData", "Local", "Microsoft", "Edge", "User Data"),
                    Path.Combine(
                        "Local Settings",
                        "Application Data",
                        "Microsoft",
                        "Edge",
                        "User Data"));
                AddFirefoxEntries(entries, dataRoot, userDirectory, user);
            }
        }

        return entries
            .OrderBy(entry => entry.Browser)
            .ThenBy(entry => entry.BackupUser, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.BackupProfile, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<BrowserBookmarkTarget> DiscoverCurrentTargets(
        BrowserBookmarkKind browser)
    {
        if (browser == BrowserBookmarkKind.Firefox)
        {
            return [];
        }

        var root = browser == BrowserBookmarkKind.Chrome
            ? _environment.ChromeUserDataRoot
            : _environment.EdgeUserDataRoot;
        return EnumerateSafeDirectories(root)
            .Where(profile =>
                File.Exists(Path.Combine(profile, "Bookmarks")) ||
                File.Exists(Path.Combine(profile, "Preferences")) ||
                Path.GetFileName(profile).Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(profile).StartsWith(
                    "Profile ",
                    StringComparison.OrdinalIgnoreCase))
            .Select(profile =>
            {
                var profileName = Path.GetFileName(profile);
                return new BrowserBookmarkTarget(
                    $"{browser}:{profileName}",
                    browser,
                    $"{BrowserDisplayName(browser)} / {profileName}",
                    profile);
            })
            .OrderBy(target => target.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<BrowserBookmarkRestoreResult> RestoreChromiumAsync(
        BrowserBookmarkBackupEntry source,
        BrowserBookmarkTarget target,
        CancellationToken cancellationToken = default)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }
        if (source.Browser is not (BrowserBookmarkKind.Chrome or BrowserBookmarkKind.Edge) ||
            source.Browser != target.Browser)
        {
            throw new InvalidOperationException(
                "同じChrome/Edgeブラウザーのバックアップと復元先を選択してください。");
        }

        var processName = source.Browser == BrowserBookmarkKind.Chrome
            ? "chrome"
            : "msedge";
        if (_isProcessRunning(processName))
        {
            throw new InvalidOperationException(
                $"{BrowserDisplayName(source.Browser)}をすべて終了してから復元してください。");
        }

        var allowedRoot = source.Browser == BrowserBookmarkKind.Chrome
            ? _environment.ChromeUserDataRoot
            : _environment.EdgeUserDataRoot;
        var normalizedAllowedRoot = BackupPathPolicy.NormalizeExistingDirectory(
            allowedRoot,
            nameof(allowedRoot));
        var targetProfile = BackupPathPolicy.EnsurePathUnderRoot(
            normalizedAllowedRoot,
            BackupPathPolicy.NormalizeExistingDirectory(
                target.ProfileDirectory,
                nameof(target.ProfileDirectory)),
            "復元先プロファイルがブラウザーデータの外側を指しています。");
        var targetParent = Directory.GetParent(targetProfile)?.FullName;
        if (!string.Equals(
            Path.GetFullPath(targetParent ?? string.Empty),
            Path.GetFullPath(normalizedAllowedRoot),
            StringComparison.OrdinalIgnoreCase) ||
            (File.GetAttributes(targetProfile) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("復元先プロファイルの範囲が不正です。");
        }

        var sourceFile = Path.GetFullPath(source.SourcePath);
        var sourceInfo = new FileInfo(sourceFile);
        if (!sourceInfo.Exists ||
            sourceInfo.Length is <= 0 or > MaximumBookmarksBytes ||
            (sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0 ||
            !sourceInfo.Name.Equals("Bookmarks", StringComparison.OrdinalIgnoreCase) &&
            !sourceInfo.Name.Equals("Bookmarks.bak", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Chrome/Edgeブックマークファイルが不正です。");
        }

        await ValidateChromiumBookmarksAsync(sourceFile, cancellationToken)
            .ConfigureAwait(false);
        var rollbackDirectory = CreateRollbackDirectory(
            source.Browser,
            targetProfile);
        var targetFile = Path.Combine(targetProfile, "Bookmarks");
        var targetBackupFile = Path.Combine(targetProfile, "Bookmarks.bak");
        if (File.Exists(targetFile))
        {
            File.Copy(
                targetFile,
                Path.Combine(rollbackDirectory, "Bookmarks.before-restore"),
                overwrite: false);
        }

        if (File.Exists(targetBackupFile))
        {
            File.Copy(
                targetBackupFile,
                Path.Combine(rollbackDirectory, "Bookmarks.bak.before-restore"),
                overwrite: false);
        }

        var partialFile = Path.Combine(
            targetProfile,
            $"Bookmarks.ytec-restore-{Guid.NewGuid():N}.partial");
        try
        {
            using (var input = new FileStream(
                sourceFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                256 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var output = new FileStream(
                partialFile,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                256 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await input.CopyToAsync(output, 256 * 1024, cancellationToken)
                    .ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }

            await ValidateChromiumBookmarksAsync(partialFile, cancellationToken)
                .ConfigureAwait(false);
            if (_isProcessRunning(processName))
            {
                throw new InvalidOperationException(
                    $"{BrowserDisplayName(source.Browser)}が起動しました。復元を中止しました。");
            }

            if (File.Exists(targetFile))
            {
                File.Replace(
                    partialFile,
                    targetFile,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(partialFile, targetFile);
            }

            File.SetLastWriteTimeUtc(targetFile, sourceInfo.LastWriteTimeUtc);
            await WriteRollbackManifestAsync(
                rollbackDirectory,
                source,
                target).ConfigureAwait(false);
            return new BrowserBookmarkRestoreResult(
                source.Browser,
                target.DisplayName,
                rollbackDirectory);
        }
        finally
        {
            if (File.Exists(partialFile))
            {
                File.Delete(partialFile);
            }
        }
    }

    public static void ValidateFirefoxBackup(BrowserBookmarkBackupEntry source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }
        if (source.Browser != BrowserBookmarkKind.Firefox)
        {
            throw new InvalidOperationException("Firefoxブックマークを選択してください。");
        }

        var info = new FileInfo(source.SourcePath);
        if (!info.Exists ||
            info.Length is <= 0 or > MaximumBookmarksBytes ||
            (info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Firefoxブックマークバックアップが不正です。");
        }

        if (info.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var jsonStream = File.OpenRead(info.FullName);
            using var reader = new StreamReader(
                jsonStream,
                detectEncodingFromByteOrderMarks: true);
            using var jsonReader = new JsonTextReader(reader)
            {
                MaxDepth = 128,
            };
            JToken token;
            try
            {
                token = JToken.ReadFrom(jsonReader);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "FirefoxブックマークJSONを解析できません。",
                    exception);
            }

            if (token.Type != JTokenType.Object &&
                token.Type != JTokenType.Array)
            {
                throw new InvalidDataException(
                    "FirefoxブックマークJSONの内容が不正です。");
            }

            return;
        }

        using var lz4Stream = File.OpenRead(info.FullName);
        var header = new byte[MozLz4Header.Length];
        if (lz4Stream.Read(header, 0, header.Length) != header.Length ||
            !header.SequenceEqual(MozLz4Header))
        {
            throw new InvalidDataException(
                "Firefoxのjsonlz4ブックマークバックアップではありません。");
        }
    }

    private static void AddChromiumEntries(
        List<BrowserBookmarkBackupEntry> entries,
        string dataRoot,
        string userDirectory,
        string user,
        BrowserBookmarkKind browser,
        params string[] relativeUserDataRoots)
    {
        foreach (var relativeRoot in relativeUserDataRoots)
        {
            var userDataRoot = Path.Combine(userDirectory, relativeRoot);
            foreach (var profile in EnumerateSafeDirectories(userDataRoot))
            {
                var bookmarks = Path.Combine(profile, "Bookmarks");
                if (!File.Exists(bookmarks))
                {
                    bookmarks = Path.Combine(profile, "Bookmarks.bak");
                }

                if (!IsSafeRegularFile(bookmarks))
                {
                    continue;
                }

                var relative = BackupPathPolicy.ToManifestPath(
                    BackupPathPolicy.GetRelativePath(dataRoot, bookmarks));
                var profileName = Path.GetFileName(profile);
                entries.Add(new BrowserBookmarkBackupEntry(
                    $"{browser}:{relative}",
                    browser,
                    $"{BrowserDisplayName(browser)} / {user} / {profileName}",
                    bookmarks,
                    user,
                    profileName));
            }
        }
    }

    private static void AddFirefoxEntries(
        List<BrowserBookmarkBackupEntry> entries,
        string dataRoot,
        string userDirectory,
        string user)
    {
        var candidateRoots = new[]
        {
            Path.Combine(
                userDirectory,
                "AppData",
                "Roaming",
                "Mozilla",
                "Firefox",
                "Profiles"),
            Path.Combine(
                userDirectory,
                "Application Data",
                "Mozilla",
                "Firefox",
                "Profiles"),
        };
        foreach (var profilesRoot in candidateRoots)
        {
            foreach (var profile in EnumerateSafeDirectories(profilesRoot))
            {
                var bookmarkDirectory = Path.Combine(profile, "bookmarkbackups");
                if (!Directory.Exists(bookmarkDirectory) ||
                    (File.GetAttributes(bookmarkDirectory) & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var latest = Directory
                    .GetFiles(bookmarkDirectory, "*", SearchOption.TopDirectoryOnly)
                    .Where(file =>
                        Path.GetExtension(file).Equals(
                            ".jsonlz4",
                            StringComparison.OrdinalIgnoreCase) ||
                        Path.GetExtension(file).Equals(
                            ".json",
                            StringComparison.OrdinalIgnoreCase))
                    .Where(IsSafeRegularFile)
                    .Select(file => new FileInfo(file))
                    .OrderByDescending(info => info.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (latest is null)
                {
                    continue;
                }

                var relative = BackupPathPolicy.ToManifestPath(
                    BackupPathPolicy.GetRelativePath(dataRoot, latest.FullName));
                var profileName = Path.GetFileName(profile);
                entries.Add(new BrowserBookmarkBackupEntry(
                    $"Firefox:{relative}",
                    BrowserBookmarkKind.Firefox,
                    string.Equals(
                        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                        "ja",
                        StringComparison.OrdinalIgnoreCase)
                        ? $"Mozilla Firefox / {user} / {profileName} / 最新バックアップ"
                        : $"Mozilla Firefox / {user} / {profileName} / latest backup",
                    latest.FullName,
                    user,
                    profileName));
            }
        }
    }

    private static IReadOnlyList<string> EnumerateSafeDirectories(string root)
    {
        if (!Directory.Exists(root) ||
            (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory
                .GetDirectories(root, "*", SearchOption.TopDirectoryOnly)
                .Where(path =>
                    (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsSafeRegularFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists &&
                info.Length is > 0 and <= MaximumBookmarksBytes &&
                (info.Attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task ValidateChromiumBookmarksAsync(
        string path,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 64 * 1024,
            leaveOpen: false);
        using var jsonReader = new JsonTextReader(reader)
        {
            MaxDepth = 128,
        };
        JObject root;
        try
        {
            root = JObject.Load(jsonReader);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Chrome/EdgeブックマークJSONを解析できません。",
                exception);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (root["roots"]?.Type != JTokenType.Object)
        {
            throw new InvalidDataException(
                "Chrome/EdgeブックマークJSONの構造が不正です。");
        }
    }

    private string CreateRollbackDirectory(
        BrowserBookmarkKind browser,
        string targetProfile)
    {
        var root = Path.GetFullPath(_rollbackRoot);
        Directory.CreateDirectory(root);
        var baseName =
            $"{_clock():yyyyMMdd_HHmmss}_{browser}_{Path.GetFileName(targetProfile)}";
        for (var suffix = 0; suffix < 10_000; suffix++)
        {
            var name = suffix == 0 ? baseName : $"{baseName}_{suffix + 1}";
            var candidate = BackupPathPolicy.EnsurePathUnderRoot(
                root,
                Path.Combine(root, name),
                "ブックマークのロールバック先がアプリ領域の外側を指しています。");
            if (Directory.Exists(candidate) || File.Exists(candidate))
            {
                continue;
            }

            Directory.CreateDirectory(candidate);
            return candidate;
        }

        throw new IOException("ブックマークのロールバック先を作成できませんでした。");
    }

    private static async Task WriteRollbackManifestAsync(
        string rollbackDirectory,
        BrowserBookmarkBackupEntry source,
        BrowserBookmarkTarget target)
    {
        var manifest = new
        {
            schemaVersion = 1,
            browser = source.Browser.ToString(),
            source.BackupUser,
            source.BackupProfile,
            targetProfile = Path.GetFileName(target.ProfileDirectory),
            manifestContainsBookmarkContent = false,
        };
        var path = Path.Combine(rollbackDirectory, "restore-manifest.v1.json");
        var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.SequentialScan);
        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(json).ConfigureAwait(false);
    }

    private static string BrowserDisplayName(BrowserBookmarkKind browser) =>
        browser switch
        {
            BrowserBookmarkKind.Chrome => "Google Chrome",
            BrowserBookmarkKind.Edge => "Microsoft Edge",
            BrowserBookmarkKind.Firefox => "Mozilla Firefox",
            _ => browser.ToString(),
        };

    private static bool IsProcessRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
