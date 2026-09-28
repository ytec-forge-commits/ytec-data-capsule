using System.Security.AccessControl;
using System.Security.Principal;
using Ytec.WindowsBackup.Core.Models;
using Ytec.WindowsBackup.Core.Services;

namespace Ytec.WindowsBackup.Windows;

public sealed class WifiProfileTransferService : IBackupSupplement
{
    private const int MaximumContainerBytes = 25 * 1024 * 1024;
    private readonly IWifiProfileCommandRunner _runner;
    private readonly Func<byte[]> _keyProvider;
    private readonly Func<DateTimeOffset> _clock;

    public WifiProfileTransferService(
        IWifiProfileCommandRunner? runner = null,
        Func<byte[]>? keyProvider = null,
        Func<DateTimeOffset>? clock = null)
    {
        _runner = runner ?? new NetshWifiProfileCommandRunner();
        _keyProvider = keyProvider ?? ApplicationWifiKey.GetKey;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string ItemId => "wifi-settings";

    public static string LocateBackupContainer(string backupJobDirectory)
    {
        var jobRoot = BackupPathPolicy.NormalizeExistingDirectory(
            backupJobDirectory,
            nameof(backupJobDirectory));
        if ((File.GetAttributes(jobRoot) & FileAttributes.ReparsePoint) != 0 ||
            !File.Exists(Path.Combine(jobRoot, "backup-manifest.v1.json")))
        {
            throw new InvalidDataException(
                "このアプリで作成したバックアップフォルダーの一番上を選択してください。");
        }

        var containerPath = BackupPathPolicy.EnsurePathUnderRoot(
            jobRoot,
            Path.Combine(
                jobRoot,
                "windows-settings",
                "wifi",
                "wifi-profiles.ywbwifi"),
            "Wi-Fiバックアップファイルの場所が不正です。");
        if (!File.Exists(containerPath))
        {
            throw new InvalidDataException(
                "選択したバックアップフォルダーにWi-Fi設定がありません。");
        }

        var containerInfo = new FileInfo(containerPath);
        if ((containerInfo.Attributes & FileAttributes.ReparsePoint) != 0 ||
            containerInfo.Length is <= 0 or > MaximumContainerBytes)
        {
            throw new InvalidDataException(
                "選択したバックアップフォルダー内のWi-Fi設定ファイルが不正です。");
        }

        return containerInfo.FullName;
    }

    public static void CleanupAbandonedTemporaryDirectories()
    {
        var temporaryRoot = GetTemporaryRoot();
        if (!Directory.Exists(temporaryRoot))
        {
            return;
        }

        foreach (var directory in Directory.GetDirectories(
            temporaryRoot,
            "*",
            SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(directory);
            if (!Guid.TryParseExact(name, "N", out _))
            {
                continue;
            }

            DeleteValidatedTemporaryDirectory(temporaryRoot, directory);
        }
    }

    public async Task<BackupSupplementResult> ExecuteAsync(
        string outputDirectory,
        IProgress<BackupSupplementProgress>? progress,
        CancellationToken cancellationToken)
    {
        var normalizedOutput = BackupPathPolicy.NormalizeExistingDirectory(
            outputDirectory,
            nameof(outputDirectory));
        if ((File.GetAttributes(normalizedOutput) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "Wi-Fi設定の出力先に再解析ポイントは使用できません。");
        }

        var wifiDirectory = BackupPathPolicy.EnsurePathUnderRoot(
            normalizedOutput,
            Path.Combine(normalizedOutput, "windows-settings", "wifi"),
            "Wi-Fi設定の出力先がジョブフォルダーの外側を指しています。");
        Directory.CreateDirectory(wifiDirectory);
        var temporaryDirectory = CreatePrivateTemporaryDirectory();
        var profiles = new List<byte[]>();
        try
        {
            progress?.Report(new BackupSupplementProgress(
                0,
                0,
                "WindowsからWi-Fi設定を一時取得しています"));
            await _runner.ExportAllProfilesAsync(
                temporaryDirectory,
                cancellationToken).ConfigureAwait(false);

            var exportedFiles = Directory
                .GetFiles(temporaryDirectory, "*.xml", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (exportedFiles.Length == 0)
            {
                throw new InvalidOperationException(
                    "バックアップできるWi-Fi設定が見つかりませんでした。");
            }

            for (var index = 0; index < exportedFiles.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = exportedFiles[index];
                var fileInfo = new FileInfo(file);
                if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    fileInfo.Length is <= 0 or > WifiProfileXmlValidator.MaximumProfileBytes)
                {
                    throw new InvalidDataException("取得したWi-Fi設定ファイルが不正です。");
                }

                cancellationToken.ThrowIfCancellationRequested();
                var profile = File.ReadAllBytes(file);
                WifiProfileXmlValidator.Validate(profile);
                profiles.Add(profile);
                progress?.Report(new BackupSupplementProgress(
                    index + 1,
                    exportedFiles.Length,
                    $"Wi-Fi設定を取得しています ({index + 1:N0}/{exportedFiles.Length:N0})"));
            }

            progress?.Report(new BackupSupplementProgress(
                0,
                profiles.Count,
                "Wi-Fi設定をAES-256と改ざん検知付きで暗号化しています"));
            var masterKey = _keyProvider();
            byte[] encrypted;
            try
            {
                encrypted = WifiEncryptedContainer.Encrypt(
                    masterKey,
                    profiles,
                    _clock());
            }
            finally
            {
                ZeroMemory(masterKey);
            }

            var containerPath = Path.Combine(wifiDirectory, "wifi-profiles.ywbwifi");
            var partialPath = containerPath + ".partial";
            try
            {
                using (var stream = new FileStream(
                    partialPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(
                            encrypted,
                            0,
                            encrypted.Length,
                            cancellationToken)
                        .ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(partialPath, containerPath);
            }
            finally
            {
                ZeroMemory(encrypted);
                if (File.Exists(partialPath))
                {
                    File.Delete(partialPath);
                }
            }

            progress?.Report(new BackupSupplementProgress(
                profiles.Count,
                profiles.Count,
                "Wi-Fi設定を暗号化してジョブへ追加しました"));
            return new BackupSupplementResult(
                ItemId,
                true,
                true,
                profiles.Count,
                []);
        }
        finally
        {
            foreach (var profile in profiles)
            {
                ZeroMemory(profile);
            }

            DeletePrivateTemporaryDirectory(temporaryDirectory);
        }
    }

    public async Task<WifiRestoreOperationResult> RestoreAsync(
        string containerPath,
        IProgress<WifiOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(containerPath))
        {
            throw new ArgumentException(
                "Wi-Fiバックアップファイルを指定してください。",
                nameof(containerPath));
        }
        var fullPath = Path.GetFullPath(containerPath);
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists ||
            (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0 ||
            fileInfo.Length is <= 0 or > MaximumContainerBytes)
        {
            throw new InvalidDataException("Wi-Fiバックアップファイルが見つからないか不正です。");
        }

        progress?.Report(new WifiOperationProgress(
            WifiOperationStage.Decrypting,
            0,
            0,
            "Wi-Fiバックアップを復号・検証しています"));
        cancellationToken.ThrowIfCancellationRequested();
        var encrypted = File.ReadAllBytes(fullPath);
        var masterKey = _keyProvider();
        WifiContainerContents contents;
        try
        {
            contents = WifiEncryptedContainer.Decrypt(masterKey, encrypted);
        }
        finally
        {
            ZeroMemory(masterKey);
            ZeroMemory(encrypted);
        }

        var temporaryDirectory = CreatePrivateTemporaryDirectory();
        var imported = 0;
        var failed = 0;
        var errors = new List<string>();
        try
        {
            for (var index = 0; index < contents.Profiles.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var profile = contents.Profiles[index];
                var temporaryFile = Path.Combine(
                    temporaryDirectory,
                    $"profile-{index + 1:D4}.xml");
                try
                {
                    WifiProfileXmlValidator.Validate(profile);
                    using (var stream = new FileStream(
                        temporaryFile,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        64 * 1024,
                        FileOptions.Asynchronous | FileOptions.WriteThrough))
                    {
                        await stream.WriteAsync(
                                profile,
                                0,
                                profile.Length,
                                cancellationToken)
                            .ConfigureAwait(false);
                        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                        stream.Flush(flushToDisk: true);
                    }
                    var succeeded = await _runner.ImportProfileAsync(
                        temporaryFile,
                        cancellationToken).ConfigureAwait(false);
                    if (succeeded)
                    {
                        imported++;
                    }
                    else
                    {
                        failed++;
                        errors.Add($"Wi-Fiプロファイル {index + 1:N0} を登録できませんでした。");
                    }
                }
                finally
                {
                    ZeroMemory(profile);
                    if (File.Exists(temporaryFile))
                    {
                        File.Delete(temporaryFile);
                    }
                }

                progress?.Report(new WifiOperationProgress(
                    WifiOperationStage.Restoring,
                    index + 1,
                    contents.Profiles.Count,
                    $"Wi-Fi設定を復元しています ({index + 1:N0}/{contents.Profiles.Count:N0})"));
            }
        }
        finally
        {
            foreach (var profile in contents.Profiles)
            {
                ZeroMemory(profile);
            }

            DeletePrivateTemporaryDirectory(temporaryDirectory);
        }

        progress?.Report(new WifiOperationProgress(
            WifiOperationStage.Finished,
            imported + failed,
            contents.Profiles.Count,
            failed == 0
                ? "Wi-Fi設定の復元が完了しました"
                : "一部のWi-Fi設定を復元できませんでした"));
        return new WifiRestoreOperationResult(
            contents.Profiles.Count,
            imported,
            failed,
            errors);
    }

    private static string CreatePrivateTemporaryDirectory()
    {
        CleanupAbandonedTemporaryDirectories();
        var temporaryRoot = GetTemporaryRoot();
        Directory.CreateDirectory(temporaryRoot);
        var temporaryDirectory = BackupPathPolicy.EnsurePathUnderRoot(
            temporaryRoot,
            Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N")),
            "Wi-Fi一時領域がアプリ領域の外側を指しています。");
        Directory.CreateDirectory(temporaryDirectory);
        if ((File.GetAttributes(temporaryDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("Wi-Fi一時領域に再解析ポイントは使用できません。");
        }

        try
        {
            ApplyPrivateTemporaryAcl(temporaryDirectory);
        }
        catch
        {
            Directory.Delete(temporaryDirectory, recursive: false);
            throw;
        }

        return temporaryDirectory;
    }

    private static void DeletePrivateTemporaryDirectory(string temporaryDirectory)
    {
        if (!Directory.Exists(temporaryDirectory))
        {
            return;
        }

        var temporaryRoot = GetTemporaryRoot();
        DeleteValidatedTemporaryDirectory(temporaryRoot, temporaryDirectory);
    }

    private static void DeleteValidatedTemporaryDirectory(
        string temporaryRoot,
        string temporaryDirectory)
    {
        var safePath = BackupPathPolicy.EnsurePathUnderRoot(
            temporaryRoot,
            temporaryDirectory,
            "Wi-Fi一時領域外の削除を拒否しました。");
        var parent = Directory.GetParent(safePath)?.FullName;
        if (!string.Equals(
            Path.GetFullPath(parent ?? string.Empty),
            Path.GetFullPath(temporaryRoot),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Wi-Fi一時領域の削除範囲が不正です。");
        }

        if ((File.GetAttributes(safePath) & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(safePath, recursive: false);
            return;
        }

        Directory.Delete(safePath, recursive: true);
    }

    private static string GetTemporaryRoot()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException("ユーザー専用一時領域を解決できません。");
        }

        return Path.Combine(
            localApplicationData,
            "Y-TEC",
            "WindowsBackup",
            "wifi-temp");
    }

    private static void ApplyPrivateTemporaryAcl(string temporaryDirectory)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var currentUser = identity.User
            ?? throw new InvalidOperationException("現在のWindowsユーザーを確認できません。");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        AddFullControlRule(security, currentUser);
        AddFullControlRule(
            security,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        AddFullControlRule(
            security,
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        new DirectoryInfo(temporaryDirectory).SetAccessControl(security);
    }

    private static void AddFullControlRule(
        DirectorySecurity security,
        SecurityIdentifier identity)
    {
        security.AddAccessRule(new FileSystemAccessRule(
            identity,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    }

    private static void ZeroMemory(byte[] bytes)
    {
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = 0;
        }
    }
}
