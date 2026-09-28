using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Ytec.WindowsBackup.Core.Models;
using Ytec.WindowsBackup.Core.Services;
using Ytec.WindowsBackup.Windows;

namespace Ytec.WindowsBackup.Tests;

internal static class LocalAcceptanceRunner
{
    private const string SourcePrefix = "ytec-windows-backup-acceptance-source-";
    private const string DestinationPrefix = "ytec-windows-backup-acceptance-destination-";
    private const string AclPrefix = "ytec-windows-backup-acl-";
    private const string VssPrefix = "ytec-windows-backup-vss-";

    public static async Task<int> RunCopyAsync(
        string sourceParent,
        string destinationParent)
    {
        var normalizedSourceParent = NormalizeExistingDirectory(
            sourceParent,
            nameof(sourceParent));
        var normalizedDestinationParent = NormalizeExistingDirectory(
            destinationParent,
            nameof(destinationParent));
        var token = Guid.NewGuid().ToString("N");
        var sourceWorkspace = Path.Combine(
            normalizedSourceParent,
            SourcePrefix + token);
        var destinationWorkspace = Path.Combine(
            normalizedDestinationParent,
            DestinationPrefix + token);

        try
        {
            var sourceRoot = CreateSyntheticSource(sourceWorkspace);
            var destinationRoot =
                Directory.CreateDirectory(destinationWorkspace).FullName;
            var catalog = new BackupCatalog
            {
                SchemaVersion = 1,
                CatalogVersion = "local-acceptance",
                ExcludedFileNames = ["desktop.ini"],
                Items =
                [
                    new BackupItemDefinition
                    {
                        Id = "documents",
                        DisplayName = "合成ドキュメント",
                        Category = "ローカル受入",
                        Scope = BackupScope.PerUser,
                        SelectionMode = BackupSelectionMode.AllFiles,
                        Paths = ["Documents"],
                    },
                ],
            };
            var plan = new BackupPlanner().CreatePlan(
                sourceRoot,
                destinationRoot,
                catalog,
                ["documents"]);
            if (!plan.CanExecute)
            {
                throw new InvalidOperationException(
                    "合成コピープランを実行可能にできませんでした。");
            }

            var copyOptions = WindowsCopyTuning.Resolve(
                sourceRoot,
                destinationRoot);
            Console.WriteLine(
                $"コピー方式: {copyOptions.Strategy}, " +
                $"並列 {copyOptions.MaxConcurrentCopies}, " +
                $"大容量並列 {copyOptions.MaxConcurrentLargeFiles}");
            Console.WriteLine(
                $"合成データ: {plan.Candidates.Count:N0}ファイル / " +
                $"{plan.TotalBytes / 1024d / 1024d:N1} MiB");
            Console.WriteLine(
                $"保存先: {plan.DestinationFileSystem ?? "判定不能"}, " +
                $"ACL適用: {(plan.ApplyAccessControl ? "あり" : "対象外")}");

            var stopwatch = Stopwatch.StartNew();
            var result = await new BackupExecutor(
                new NoOpFinalizer(),
                copyOptions: copyOptions).ExecuteAsync(plan);
            stopwatch.Stop();

            if (result.Status != BackupRunStatus.Completed ||
                result.CopiedFiles != plan.Candidates.Count ||
                result.FailedFiles != 0)
            {
                throw new InvalidOperationException(
                    $"コピー結果が不正です: {result.Status}, " +
                    $"成功 {result.CopiedFiles:N0}, 失敗 {result.FailedFiles:N0}");
            }

            VerifyCopiedFiles(sourceRoot, result.OutputDirectory, plan);
            if (Directory.GetFiles(
                result.OutputDirectory,
                "*.ytec-partial",
                SearchOption.AllDirectories).Any())
            {
                throw new InvalidOperationException(
                    "正常完了後に部分コピーファイルが残っています。");
            }

            var mebibytesPerSecond =
                plan.TotalBytes / 1024d / 1024d / stopwatch.Elapsed.TotalSeconds;
            Console.WriteLine(
                $"PASS  合成SSDコピー: {stopwatch.Elapsed.TotalSeconds:N2}秒 / " +
                $"{mebibytesPerSecond:N1} MiB/s");
            Console.WriteLine("PASS  全ファイルのSHA-256一致");
            Console.WriteLine("PASS  部分コピーファイル残留なし");
            return 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"FAIL  合成コピー受入: {exception.Message}");
            return 1;
        }
        finally
        {
            DeleteOwnedDirectory(
                normalizedSourceParent,
                sourceWorkspace,
                SourcePrefix);
            DeleteOwnedDirectory(
                normalizedDestinationParent,
                destinationWorkspace,
                DestinationPrefix);
        }
    }

    public static async Task<int> RunAclAsync(string parentDirectory)
    {
        if (!WindowsBackupEnvironment.IsProcessElevated())
        {
            Console.WriteLine(
                "SKIP  ACL実地試験には管理者として起動したPowerShellが必要です。");
            return 2;
        }

        var normalizedParent = NormalizeExistingDirectory(
            parentDirectory,
            nameof(parentDirectory));
        var workspace = Path.Combine(
            normalizedParent,
            AclPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            var destinationRoot = Directory.CreateDirectory(workspace).FullName;
            var outputDirectory = Directory.CreateDirectory(
                Path.Combine(destinationRoot, "YTEC_Backup_ACL_Acceptance")).FullName;
            var nested = Directory.CreateDirectory(
                Path.Combine(outputDirectory, "data", "nested")).FullName;
            var rootFile = Path.Combine(outputDirectory, "backup-manifest.v1.json");
            var nestedFile = Path.Combine(nested, "synthetic.txt");
            File.WriteAllText(rootFile, "{}");
            File.WriteAllText(nestedFile, "synthetic ACL acceptance");

            var result = await new WindowsAclFinalizer().FinalizeAsync(
                destinationRoot,
                outputDirectory,
                progress: null,
                CancellationToken.None);
            if (!result.Succeeded || result.FailedEntries != 0)
            {
                throw new InvalidOperationException(
                    $"ACL処理に失敗しました: {string.Join(", ", result.Errors)}");
            }

            VerifyEveryoneAcl(outputDirectory, isDirectory: true);
            VerifyEveryoneAcl(nested, isDirectory: true);
            VerifyEveryoneAcl(rootFile, isDirectory: false);
            VerifyEveryoneAcl(nestedFile, isDirectory: false);
            Console.WriteLine(
                $"PASS  Everyone所有者・フルアクセス: " +
                $"{result.ProcessedEntries:N0}エントリ");
            return 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"FAIL  ACL受入: {exception.Message}");
            return 1;
        }
        finally
        {
            DeleteOwnedDirectory(normalizedParent, workspace, AclPrefix);
        }
    }

    public static async Task<int> RunVssAsync(string parentDirectory)
    {
        if (!WindowsBackupEnvironment.IsProcessElevated())
        {
            Console.WriteLine(
                "SKIP  VSS実地試験には管理者権限が必要です。");
            return 2;
        }

        if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
        {
            Console.WriteLine(
                "SKIP  64ビットWindowsのVSS実地試験にはx64プロセスが必要です。");
            return 2;
        }

        var normalizedParent = NormalizeExistingDirectory(
            parentDirectory,
            nameof(parentDirectory));
        var workspace = Path.Combine(
            normalizedParent,
            VssPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            var sourceRoot = Directory.CreateDirectory(
                Path.Combine(workspace, "source")).FullName;
            Directory.CreateDirectory(Path.Combine(sourceRoot, "Windows"));
            var documents = Directory.CreateDirectory(
                Path.Combine(sourceRoot, "Users", "Alice", "Documents")).FullName;
            Directory.CreateDirectory(Path.Combine(sourceRoot, "Users", "Public"));
            Directory.CreateDirectory(Path.Combine(sourceRoot, "Users", "Default"));
            var destinationRoot = Directory.CreateDirectory(
                Path.Combine(workspace, "destination")).FullName;
            var lockedPath = Path.Combine(documents, "vss-locked-synthetic.txt");
            const string expected = "Y-TEC synthetic VSS locked-file acceptance";
            File.WriteAllText(
                lockedPath,
                expected,
                new System.Text.UTF8Encoding(false));

            var catalog = new BackupCatalog
            {
                SchemaVersion = 1,
                CatalogVersion = "vss-acceptance",
                ExcludedFileNames = ["desktop.ini"],
                Items =
                [
                    new BackupItemDefinition
                    {
                        Id = "documents",
                        DisplayName = "VSS合成ドキュメント",
                        Category = "ローカル受入",
                        Scope = BackupScope.PerUser,
                        SelectionMode = BackupSelectionMode.AllFiles,
                        Paths = ["Documents"],
                    },
                ],
            };
            var plan = new BackupPlanner().CreatePlan(
                sourceRoot,
                destinationRoot,
                catalog,
                ["documents"]);
            var vssSource = new WindowsVssFileBackupSource();
            using var exclusiveLock = new FileStream(
                lockedPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            AssertDirectReadIsBlocked(lockedPath);

            var result = await new BackupExecutor(
                new NoOpFinalizer(),
                lockedFileSourceFactory: () => vssSource).ExecuteAsync(plan);
            var file = result.Files.Single();
            var copiedPath = Path.Combine(
                result.OutputDirectory,
                "data",
                file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (result.Status != BackupRunStatus.Completed ||
                result.CopiedFiles != 1 ||
                result.FailedFiles != 0 ||
                result.VolumeShadowCopyFiles != 1 ||
                file.CopySource != BackupCopySource.VolumeShadowCopy)
            {
                throw new InvalidOperationException(
                    $"VSSコピー結果が不正です: {result.Status}, " +
                    $"VSS {result.VolumeShadowCopyFiles:N0}, 失敗 {result.FailedFiles:N0}");
            }

            if (!string.Equals(
                    File.ReadAllText(copiedPath),
                    expected,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "VSSからコピーした合成ファイルの内容が一致しません。");
            }

            if (vssSource.CreatedSnapshotCount != 1 ||
                vssSource.DeletedSnapshotCount != 1 ||
                result.Warnings.Count != 0)
            {
                throw new InvalidOperationException(
                    $"VSS後始末が不正です: 作成 {vssSource.CreatedSnapshotCount:N0}, " +
                    $"削除 {vssSource.DeletedSnapshotCount:N0}, " +
                    $"警告 {result.Warnings.Count:N0}");
            }

            Console.WriteLine("PASS  直接読取は共有違反で拒否");
            Console.WriteLine("PASS  ロック中ファイルをVSSからコピー");
            Console.WriteLine("PASS  VSSコピー内容一致");
            Console.WriteLine("PASS  作成したVSSスナップショットを削除");
            return 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"FAIL  VSS受入: {exception.Message} " +
                $"(0x{exception.HResult:X8})");
            if (exception.InnerException is not null)
            {
                Console.WriteLine(
                    $"DETAIL  {exception.InnerException.Message} " +
                    $"(0x{exception.InnerException.HResult:X8})");
            }
            return 1;
        }
        finally
        {
            DeleteOwnedDirectory(normalizedParent, workspace, VssPrefix);
        }
    }

    public static async Task<int> RunGuestAsync(string guestRoot)
    {
        var root = NormalizeExistingDirectory(guestRoot, nameof(guestRoot));
        var results = Directory.CreateDirectory(
            Path.Combine(root, "results")).FullName;
        var x86 = Path.Combine(root, "x86", "Ytec.WindowsBackup.Tests.exe");
        var x64 = Path.Combine(root, "x64", "Ytec.WindowsBackup.Tests.exe");
        var ui = Path.Combine(root, "ui", "Y-TEC Data Capsule.exe");
        var requiredFiles = Environment.Is64BitOperatingSystem
            ? new[] { x86, x64, ui }
            : new[] { x86, ui };
        foreach (var required in requiredFiles)
        {
            if (!File.Exists(required))
            {
                throw new FileNotFoundException(
                    "VM受入ペイロードが不足しています。",
                    required);
            }
        }

        var operations = new List<int>
        {
            await RunChildAndCaptureAsync(
                x86,
                [],
                Path.Combine(results, "tests-x86.txt")),
            await RunChildAndCaptureAsync(
                x86,
                [
                    "--write-cross-arch",
                    Path.Combine(results, "wifi-from-x86.ywbwifi"),
                ],
                Path.Combine(results, "wifi-write-x86.txt")),
        };
        if (Environment.Is64BitOperatingSystem)
        {
            operations.Add(await RunChildAndCaptureAsync(
                x64,
                [],
                Path.Combine(results, "tests-x64.txt")));
            operations.Add(await RunChildAndCaptureAsync(
                x64,
                [
                    "--read-cross-arch",
                    Path.Combine(results, "wifi-from-x86.ywbwifi"),
                ],
                Path.Combine(results, "wifi-read-x64.txt")));
            operations.Add(await RunChildAndCaptureAsync(
                x64,
                [
                    "--write-cross-arch",
                    Path.Combine(results, "wifi-from-x64.ywbwifi"),
                ],
                Path.Combine(results, "wifi-write-x64.txt")));
            operations.Add(await RunChildAndCaptureAsync(
                x86,
                [
                    "--read-cross-arch",
                    Path.Combine(results, "wifi-from-x64.ywbwifi"),
                ],
                Path.Combine(results, "wifi-read-x86.txt")));
        }

        var aclParent = Directory.CreateDirectory(
            Path.Combine(root, "acl-parent")).FullName;
        var aclExit = await RunChildAndCaptureAsync(
            Environment.Is64BitOperatingSystem ? x64 : x86,
            ["--acl-acceptance", aclParent],
            Path.Combine(results, "acl.txt"));
        File.WriteAllText(
            Path.Combine(results, "acl.exit.txt"),
            aclExit.ToString());

        StopStaleValidationUiProcesses();
        using var uiProcess = Process.Start(new ProcessStartInfo
        {
            FileName = ui,
            WorkingDirectory = Path.GetDirectoryName(ui),
            UseShellExecute = true,
        }) ?? throw new InvalidOperationException(
            "VM内でUI検証版を起動できませんでした。");
        await Task.Delay(TimeSpan.FromSeconds(3));
        var uiLaunchSucceeded = !uiProcess.HasExited;
        File.WriteAllText(
            Path.Combine(results, "ui.txt"),
            uiLaunchSucceeded
                ? $"PASS  UIプロセス起動継続 PID={uiProcess.Id}"
                : $"FAIL  UIプロセスが早期終了 ExitCode={uiProcess.ExitCode}");

        var allCoreOperationsSucceeded =
            operations.All(exitCode => exitCode == 0) &&
            uiLaunchSucceeded;
        File.WriteAllText(
            Path.Combine(results, "done.txt"),
            allCoreOperationsSucceeded ? "PASS" : "FAIL");
        File.WriteAllText(
            Path.Combine(results, "runtime.txt"),
            $"OS={(Environment.Is64BitOperatingSystem ? "x64" : "x86")}" +
            Environment.NewLine +
            $"Process={(Environment.Is64BitProcess ? "x64" : "x86")}");
        Console.WriteLine(
            allCoreOperationsSucceeded
                ? "PASS  VM内の回帰・相互読込処理が完了しました。"
                : "FAIL  VM内の回帰・相互読込処理に失敗しました。");
        Console.WriteLine(
            aclExit == 0
                ? "PASS  VM内のEveryone ACL実地試験が成功しました。"
                : aclExit == 2
                    ? "SKIP  VM内プロセスは非管理者のためACL実地試験を省略しました。"
                    : "FAIL  VM内のACL実地試験に失敗しました。");
        return allCoreOperationsSucceeded && (aclExit == 0 || aclExit == 2)
            ? 0
            : 1;
    }

    private static void StopStaleValidationUiProcesses()
    {
        foreach (var processName in new[]
                 {
                     "Ytec.WindowsBackup.App",
                     "Y-TEC Data Capsule",
                 })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        if (process.HasExited)
                        {
                            continue;
                        }

                        if (process.CloseMainWindow())
                        {
                            process.WaitForExit(3000);
                        }
                        if (!process.HasExited)
                        {
                            process.Kill();
                            process.WaitForExit(3000);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // The process exited between enumeration and cleanup.
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // A process from another security context is not part
                        // of this validation session; leave it untouched.
                    }
                }
            }
        }
    }

    private static string CreateSyntheticSource(string sourceWorkspace)
    {
        var sourceRoot = Directory.CreateDirectory(
            Path.Combine(sourceWorkspace, "source")).FullName;
        Directory.CreateDirectory(Path.Combine(sourceRoot, "Windows"));
        var documents = Directory.CreateDirectory(
            Path.Combine(sourceRoot, "Users", "Alice", "Documents")).FullName;
        var small = Directory.CreateDirectory(Path.Combine(documents, "small")).FullName;
        var large = Directory.CreateDirectory(Path.Combine(documents, "large")).FullName;

        using var random = RandomNumberGenerator.Create();
        var buffer = new byte[1024 * 1024];
        random.GetBytes(buffer);
        for (var index = 0; index < 1024; index++)
        {
            buffer[0] = (byte)index;
            WritePatternFile(
                Path.Combine(small, $"small-{index:D4}.bin"),
                64 * 1024,
                buffer);
        }

        for (var index = 0; index < 8; index++)
        {
            buffer[0] = (byte)(index + 17);
            WritePatternFile(
                Path.Combine(large, $"large-{index:D2}.bin"),
                64L * 1024 * 1024,
                buffer);
        }

        return sourceRoot;
    }

    private static void WritePatternFile(
        string path,
        long length,
        byte[] buffer)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            buffer.Length,
            FileOptions.SequentialScan);
        var remaining = length;
        while (remaining > 0)
        {
            var count = (int)Math.Min(buffer.Length, remaining);
            stream.Write(buffer, 0, count);
            remaining -= count;
        }
    }

    private static void VerifyCopiedFiles(
        string sourceRoot,
        string outputDirectory,
        BackupPlan plan)
    {
        using var sha256 = SHA256.Create();
        foreach (var candidate in plan.Candidates)
        {
            var destinationPath = Path.Combine(
                outputDirectory,
                "data",
                candidate.RelativePath);
            if (!File.Exists(destinationPath))
            {
                throw new InvalidOperationException(
                    $"コピー先がありません: {candidate.RelativePath}");
            }

            byte[] sourceHash;
            byte[] destinationHash;
            using (var source = File.OpenRead(candidate.SourcePath))
            {
                sourceHash = sha256.ComputeHash(source);
            }

            sha256.Initialize();
            using (var destination = File.OpenRead(destinationPath))
            {
                destinationHash = sha256.ComputeHash(destination);
            }

            if (!sourceHash.SequenceEqual(destinationHash))
            {
                throw new InvalidOperationException(
                    $"SHA-256が一致しません: {candidate.RelativePath}");
            }
        }
    }

    private static void VerifyEveryoneAcl(string path, bool isDirectory)
    {
        FileSystemSecurity security = isDirectory
            ? new DirectoryInfo(path).GetAccessControl(
                AccessControlSections.Owner | AccessControlSections.Access)
            : new FileInfo(path).GetAccessControl(
                AccessControlSections.Owner | AccessControlSections.Access);
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var owner = security.GetOwner(typeof(SecurityIdentifier));
        if (!owner.Equals(everyone))
        {
            throw new InvalidOperationException(
                $"所有者がEveryoneではありません: {Path.GetFileName(path)}");
        }

        var hasFullControl = security
            .GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                targetType: typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Any(rule =>
                rule.AccessControlType == AccessControlType.Allow &&
                rule.IdentityReference.Equals(everyone) &&
                (rule.FileSystemRights & FileSystemRights.FullControl) ==
                FileSystemRights.FullControl);
        if (!hasFullControl)
        {
            throw new InvalidOperationException(
                $"Everyoneフルアクセスがありません: {Path.GetFileName(path)}");
        }
    }

    private static async Task<int> RunChildAndCaptureAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string outputPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Arguments = string.Join(" ", arguments.Select(QuoteArgument)),
        };
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"子プロセスを起動できませんでした: {Path.GetFileName(executable)}");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await Task.Run(process.WaitForExit);
        var combined = await standardOutput;
        var error = await standardError;
        File.WriteAllText(
            outputPath,
            combined + (string.IsNullOrWhiteSpace(error)
                ? string.Empty
                : Environment.NewLine + error),
            new System.Text.UTF8Encoding(false));
        File.WriteAllText(
            outputPath + ".exit.txt",
            process.ExitCode.ToString());
        return process.ExitCode;
    }

    private static string QuoteArgument(string value) =>
        "\"" + value.Replace("\"", "\\\"") + "\"";

    private static void AssertDirectReadIsBlocked(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
        }
        catch (IOException)
        {
            return;
        }

        throw new InvalidOperationException(
            "合成ファイルが共有違反になっていません。");
    }

    private static string NormalizeExistingDirectory(
        string path,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "既存フォルダーを指定してください。",
                parameterName);
        }

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath) ||
            (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new DirectoryNotFoundException(
                $"既存の通常フォルダーではありません: {fullPath}");
        }

        return fullPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }

    private static void DeleteOwnedDirectory(
        string parent,
        string path,
        string requiredPrefix)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var fullParent = Path.GetFullPath(parent).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var actualParent = Directory.GetParent(fullPath)?.FullName.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (!string.Equals(
                fullParent,
                actualParent,
                StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullPath).StartsWith(
                requiredPrefix,
                StringComparison.Ordinal) ||
            (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "合成試験領域外の削除を拒否しました。");
        }

        Directory.Delete(fullPath, recursive: true);
    }

    private sealed class NoOpFinalizer : IBackupFinalizer
    {
        public Task<BackupFinalizationResult> FinalizeAsync(
            string destinationRoot,
            string outputDirectory,
            IProgress<BackupFinalizationProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new BackupFinalizationResult(true, true, 1, 0, []));
        }
    }
}
