using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Ytec.WindowsBackup.Core.Models;

namespace Ytec.WindowsBackup.Core.Services;

public sealed class BackupExecutor
{
    private const int CopyBufferSize = 1024 * 1024;
    private const long LargeFileThreshold = 64L * 1024 * 1024;
    private readonly IBackupFinalizer _finalizer;
    private readonly Func<DateTimeOffset> _clock;
    private readonly BackupCopyOptions _copyOptions;
    private readonly IReadOnlyDictionary<string, IBackupSupplement> _supplements;
    private readonly Func<ILockedFileBackupSource>? _lockedFileSourceFactory;
    private readonly IBackupOutputDirectoryFactory _outputDirectoryFactory;

    public BackupExecutor(
        IBackupFinalizer finalizer,
        Func<DateTimeOffset>? clock = null,
        BackupCopyOptions? copyOptions = null,
        IReadOnlyCollection<IBackupSupplement>? supplements = null,
        Func<ILockedFileBackupSource>? lockedFileSourceFactory = null,
        IBackupOutputDirectoryFactory? outputDirectoryFactory = null)
    {
        _finalizer = finalizer ?? throw new ArgumentNullException(nameof(finalizer));
        _clock = clock ?? (() => DateTimeOffset.Now);
        _copyOptions = copyOptions ?? BackupCopyOptions.Balanced;
        _lockedFileSourceFactory = lockedFileSourceFactory;
        _outputDirectoryFactory =
            outputDirectoryFactory ?? new DefaultBackupOutputDirectoryFactory();
        if (_copyOptions.MaxConcurrentCopies is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(
                nameof(copyOptions),
                "同時コピー数は1～16で指定してください。");
        }

        if (_copyOptions.MaxConcurrentLargeFiles is < 1 or > 8 ||
            _copyOptions.MaxConcurrentLargeFiles > _copyOptions.MaxConcurrentCopies)
        {
            throw new ArgumentOutOfRangeException(
                nameof(copyOptions),
                "大容量ファイルの同時コピー数が不正です。");
        }

        if (string.IsNullOrWhiteSpace(_copyOptions.Strategy))
        {
            throw new ArgumentException("コピー方式名は必須です。", nameof(copyOptions));
        }

        var supplementList = supplements ?? [];
        if (supplementList.Any(supplement => string.IsNullOrWhiteSpace(supplement.ItemId)))
        {
            throw new ArgumentException("追加バックアップ処理の項目IDは必須です。", nameof(supplements));
        }

        _supplements = supplementList.ToDictionary(
            supplement => supplement.ItemId,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<BackupResult> ExecuteAsync(
        BackupPlan plan,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }
        if (!plan.CanExecute)
        {
            throw new InvalidOperationException("実行できるバックアップ計画ではありません。");
        }

        var sourceRoot =
            BackupPathPolicy.NormalizeExistingDirectory(plan.SourceRoot, nameof(plan.SourceRoot));
        var destinationRoot = BackupPathPolicy.NormalizeExistingDirectory(
            plan.DestinationRoot,
            nameof(plan.DestinationRoot));
        BackupPathPolicy.EnsureDestinationOutsideSource(sourceRoot, destinationRoot);

        using var outputLease = _outputDirectoryFactory.Create(
            destinationRoot,
            plan.JobName,
            plan.ApplyAccessControl);
        var outputDirectory = outputLease.OutputDirectory;
        var dataDirectory = Path.Combine(outputDirectory, "data");
        Directory.CreateDirectory(dataDirectory);

        var startedAt = _clock();
        var resultSlots = new BackupFileResult?[plan.Candidates.Count];
        var completedFiles = 0;
        var completedBytes = 0L;
        var cancelled = false;
        var runWarnings = new List<string>();
        if (plan.SkippedOnlineOnlyFiles > 0)
        {
            runWarnings.Add(
                $"オンライン専用ファイル {plan.SkippedOnlineOnlyFiles:N0}件" +
                "は対象外とし、ダウンロード済みファイルだけをコピーしました。");
        }

        var lockedFileSource = _lockedFileSourceFactory?.Invoke();
        using var largeFileGate = new SemaphoreSlim(
            _copyOptions.MaxConcurrentLargeFiles,
            _copyOptions.MaxConcurrentLargeFiles);
        try
        {
            var nextIndex = -1;
            var workers = Enumerable
                .Range(
                    0,
                    Math.Min(_copyOptions.MaxConcurrentCopies, plan.Candidates.Count))
                .Select(_ => CopyWorkerAsync())
                .ToArray();
            await Task.WhenAll(workers).ConfigureAwait(false);

            async Task CopyWorkerAsync()
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var index = Interlocked.Increment(ref nextIndex);
                    if (index >= plan.Candidates.Count)
                    {
                        return;
                    }

                    var candidate = plan.Candidates[index];
                    var manifestPath =
                        BackupPathPolicy.ToManifestPath(candidate.RelativePath);
                    progress?.Report(new BackupProgress(
                        BackupProgressStage.Copying,
                        Volatile.Read(ref completedFiles),
                        plan.Candidates.Count,
                        Interlocked.Read(ref completedBytes),
                        plan.TotalBytes,
                        manifestPath,
                        "ファイルをコピーしています"));

                    var holdsLargeFileGate = false;
                    try
                    {
                        if (candidate.Length >= LargeFileThreshold)
                        {
                            await largeFileGate.WaitAsync(cancellationToken)
                                .ConfigureAwait(false);
                            holdsLargeFileGate = true;
                        }

                        var copySource = await CopyCandidateAsync(
                            sourceRoot,
                            dataDirectory,
                            candidate,
                            lockedFileSource,
                            cancellationToken).ConfigureAwait(false);
                        Interlocked.Add(ref completedBytes, candidate.Length);
                        resultSlots[index] = new BackupFileResult(
                            manifestPath,
                            candidate.ItemIds,
                            candidate.Length,
                            true,
                            null,
                            null,
                            copySource);
                    }
                    catch (OperationCanceledException) when (
                        cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (OnlineOnlyFileSkippedException)
                    {
                        resultSlots[index] = new BackupFileResult(
                            manifestPath,
                            candidate.ItemIds,
                            candidate.Length,
                            false,
                            null,
                            "計画後にオンライン専用となったため対象外にしました。",
                            BackupCopySource.Direct,
                            true);
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException or
                        NotSupportedException or System.Security.SecurityException)
                    {
                        resultSlots[index] = new BackupFileResult(
                            manifestPath,
                            candidate.ItemIds,
                            candidate.Length,
                            false,
                            $"0x{exception.HResult:X8}",
                            FriendlyCopyError(exception));
                    }
                    finally
                    {
                        if (holdsLargeFileGate)
                        {
                            largeFileGate.Release();
                        }
                    }

                    var finishedFiles = Interlocked.Increment(ref completedFiles);
                    progress?.Report(new BackupProgress(
                        BackupProgressStage.Copying,
                        finishedFiles,
                        plan.Candidates.Count,
                        Interlocked.Read(ref completedBytes),
                        plan.TotalBytes,
                        manifestPath,
                        "コピー処理を進めています"));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }
        finally
        {
            if (lockedFileSource is not null)
            {
                runWarnings.AddRange(lockedFileSource.Complete());
                lockedFileSource.Dispose();
            }
        }

        var results = resultSlots
            .Where(result => result is not null)
            .Select(result => result!)
            .ToArray();
        var supplementResults = new List<BackupSupplementResult>();
        if (!cancelled)
        {
            foreach (var supplementItemId in plan.SupplementItemIds)
            {
                if (!_supplements.TryGetValue(supplementItemId, out var supplement))
                {
                    supplementResults.Add(new BackupSupplementResult(
                        supplementItemId,
                        true,
                        false,
                        0,
                        ["対応する追加バックアップ処理が登録されていません。"]));
                    continue;
                }

                var supplementProgress = new Progress<BackupSupplementProgress>(update =>
                    progress?.Report(new BackupProgress(
                        BackupProgressStage.ProcessingSupplement,
                        results.Length,
                        plan.Candidates.Count,
                        Interlocked.Read(ref completedBytes),
                        plan.TotalBytes,
                        string.Empty,
                        update.Message)));
                try
                {
                    supplementResults.Add(await supplement.ExecuteAsync(
                        outputDirectory,
                        supplementProgress,
                        cancellationToken).ConfigureAwait(false));
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidDataException or
                    InvalidOperationException or UnauthorizedAccessException or
                    System.Security.SecurityException or CryptographicException)
                {
                    supplementResults.Add(new BackupSupplementResult(
                        supplementItemId,
                        true,
                        false,
                        0,
                        [$"追加バックアップ処理に失敗しました: 0x{exception.HResult:X8}"]));
                }
            }
        }

        var provisionalStatus = cancelled
            ? BackupRunStatus.Cancelled
            : results.Any(result => !result.Succeeded) ||
                supplementResults.Any(result => !result.Succeeded) ||
                runWarnings.Count > 0
                ? BackupRunStatus.CompletedWithWarnings
                : BackupRunStatus.Completed;
        var provisionalResult = new BackupResult
        {
            OutputDirectory = outputDirectory,
            Status = provisionalStatus,
            StartedAt = startedAt,
            FinishedAt = _clock(),
            Files = results,
            AccessControl = BackupFinalizationResult.NotAttempted,
            Supplements = supplementResults,
            Warnings = runWarnings,
            SkippedOnlineOnlyFilesDuringPlanning =
                plan.SkippedOnlineOnlyFiles,
            SkippedOnlineOnlyBytesDuringPlanning =
                plan.SkippedOnlineOnlyBytes,
        };

        progress?.Report(new BackupProgress(
            BackupProgressStage.WritingManifest,
            results.Length,
            plan.Candidates.Count,
            Interlocked.Read(ref completedBytes),
            plan.TotalBytes,
            string.Empty,
            "結果マニフェストを保存しています"));
        await WriteManifestAsync(
            outputDirectory,
            plan,
            provisionalResult,
            preserveSecurityDescriptor: false,
            CancellationToken.None).ConfigureAwait(false);

        BackupFinalizationResult finalization = BackupFinalizationResult.NotAttempted;
        FileStream? lockedManifestStream = null;
        try
        {
            if (!cancelled && plan.ApplyAccessControl)
            {
                lockedManifestStream = OpenManifestForProtectedRewrite(outputDirectory);
            }

            if (plan.ApplyAccessControl &&
                (!cancelled || outputLease.RequiresFinalizationOnCancellation))
            {
                var finalizationProgress = new Progress<BackupFinalizationProgress>(update =>
                    progress?.Report(new BackupProgress(
                        BackupProgressStage.ApplyingAccessControl,
                        results.Length,
                        plan.Candidates.Count,
                        Interlocked.Read(ref completedBytes),
                        plan.TotalBytes,
                        update.CurrentRelativePath,
                        $"Everyoneフルアクセスを設定中 ({update.ProcessedEntries:N0}件)")));

                // ACL処理が途中で止まると出力物の権限が不統一になるため、
                // コピー後のキャンセル要求では中断しない。
                finalization = await _finalizer.FinalizeAsync(
                    destinationRoot,
                    outputDirectory,
                    finalizationProgress,
                    CancellationToken.None).ConfigureAwait(false);
            }
            else if (!cancelled)
            {
                finalization = BackupFinalizationResult.Skipped(
                    $"{plan.DestinationFileSystem ?? "このファイルシステム"}は" +
                    "所有者・ACLを保存しないため適用対象外です。");
            }

            var status = cancelled
                ? BackupRunStatus.Cancelled
                : results.Any(result => !result.Succeeded) ||
                    supplementResults.Any(result => !result.Succeeded) ||
                    runWarnings.Count > 0 ||
                    !finalization.Succeeded
                    ? BackupRunStatus.CompletedWithWarnings
                    : BackupRunStatus.Completed;
            var result = new BackupResult
            {
                OutputDirectory = outputDirectory,
                Status = status,
                StartedAt = startedAt,
                FinishedAt = _clock(),
                Files = results,
                AccessControl = finalization,
                Supplements = supplementResults,
                Warnings = runWarnings,
                SkippedOnlineOnlyFilesDuringPlanning =
                    plan.SkippedOnlineOnlyFiles,
                SkippedOnlineOnlyBytesDuringPlanning =
                    plan.SkippedOnlineOnlyBytes,
            };

            if (!cancelled)
            {
                progress?.Report(new BackupProgress(
                    BackupProgressStage.WritingManifest,
                    results.Length,
                    plan.Candidates.Count,
                    Interlocked.Read(ref completedBytes),
                    plan.TotalBytes,
                    string.Empty,
                    plan.ApplyAccessControl
                        ? "ACL結果をマニフェストへ反映しています"
                        : "ACL適用対象外の理由をマニフェストへ反映しています"));
                if (lockedManifestStream is not null)
                {
                    await WriteManifestAsync(
                        lockedManifestStream,
                        plan,
                        result,
                        CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    await WriteManifestAsync(
                        outputDirectory,
                        plan,
                        result,
                        preserveSecurityDescriptor: true,
                        CancellationToken.None).ConfigureAwait(false);
                }
            }

            progress?.Report(new BackupProgress(
                BackupProgressStage.Finished,
                results.Length,
                plan.Candidates.Count,
                Interlocked.Read(ref completedBytes),
                plan.TotalBytes,
                string.Empty,
                status == BackupRunStatus.Completed
                    ? "バックアップが完了しました"
                    : status == BackupRunStatus.Cancelled
                        ? "バックアップをキャンセルしました"
                        : "警告付きでバックアップが完了しました"));

            return result;
        }
        finally
        {
            lockedManifestStream?.Dispose();
        }
    }

    private static async Task<BackupCopySource> CopyCandidateAsync(
        string sourceRoot,
        string dataDirectory,
        BackupCandidate candidate,
        ILockedFileBackupSource? lockedFileSource,
        CancellationToken cancellationToken)
    {
        var sourcePath = BackupPathPolicy.EnsurePathUnderRoot(
            sourceRoot,
            candidate.SourcePath,
            "コピー元が元ドライブの外側を指しています。");
        var sourceAttributes = File.GetAttributes(sourcePath);
        if (CloudFileAvailability.IsNotFullyPresentLocally(sourceAttributes))
        {
            throw new OnlineOnlyFileSkippedException();
        }

        if ((sourceAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("再解析ポイントはコピーできません。");
        }

        var destinationPath = BackupPathPolicy.EnsurePathUnderRoot(
            dataDirectory,
            Path.Combine(dataDirectory, candidate.RelativePath),
            "コピー先がジョブフォルダーの外側を指しています。");
        var destinationParent = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("コピー先フォルダーを解決できません。");
        Directory.CreateDirectory(destinationParent);

        var partialPath = destinationPath + ".ytec-partial";
        BackupPathPolicy.EnsurePathUnderRoot(
            dataDirectory,
            partialPath,
            "一時コピー先がジョブフォルダーの外側を指しています。");

        try
        {
            var copySource = BackupCopySource.Direct;
            Stream source;
            try
            {
                source = OpenDirectSource(sourcePath);
            }
            catch (Exception exception) when (
                lockedFileSource is not null &&
                IsLockedSourceException(exception))
            {
                source = lockedFileSource.OpenRead(sourcePath);
                copySource = BackupCopySource.VolumeShadowCopy;
            }

            using (source)
            using (var destination = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination, CopyBufferSize, cancellationToken)
                    .ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.SetLastWriteTimeUtc(partialPath, candidate.LastWriteTimeUtc);
            File.Move(partialPath, destinationPath);
            return copySource;
        }
        catch
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }

            throw;
        }
    }

    private static Stream OpenDirectSource(string sourcePath) =>
        new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static bool IsLockedSourceException(Exception exception)
    {
        if (exception is UnauthorizedAccessException)
        {
            return true;
        }

        if (exception is not IOException)
        {
            return false;
        }

        var windowsError = exception.HResult & 0xFFFF;
        return windowsError is 32 or 33;
    }

    private static FileStream OpenManifestForProtectedRewrite(string outputDirectory) =>
        new(
            Path.Combine(outputDirectory, "backup-manifest.v1.json"),
            FileMode.Open,
            FileAccess.Write,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);

    private async Task WriteManifestAsync(
        FileStream stream,
        BackupPlan plan,
        BackupResult result,
        CancellationToken cancellationToken)
    {
        var json = CreateManifestJson(plan, result);
        stream.Position = 0;
        stream.SetLength(0);
        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            64 * 1024,
            leaveOpen: true);
        cancellationToken.ThrowIfCancellationRequested();
        await writer.WriteAsync(json).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private async Task WriteManifestAsync(
        string outputDirectory,
        BackupPlan plan,
        BackupResult result,
        bool preserveSecurityDescriptor,
        CancellationToken cancellationToken)
    {
        var json = CreateManifestJson(plan, result);
        var manifestPath = Path.Combine(outputDirectory, "backup-manifest.v1.json");
        if (preserveSecurityDescriptor)
        {
            using var existingStream = OpenManifestForProtectedRewrite(outputDirectory);
            await WriteManifestAsync(
                existingStream,
                plan,
                result,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var temporaryPath = manifestPath + ".tmp";
        using (var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.SequentialScan))
        using (var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteAsync(json).ConfigureAwait(false);
        }

        File.Move(temporaryPath, manifestPath);
    }

    private string CreateManifestJson(BackupPlan plan, BackupResult result)
    {
        var manifest = new
        {
            schemaVersion = 1,
            appVersion =
                typeof(BackupExecutor).Assembly.GetName().Version?.ToString(3) ??
                "unknown",
            runtime = new
            {
                targetFramework = ".NET Framework 4.6.1",
                processArchitecture = Environment.Is64BitProcess ? "x64" : "x86",
                osArchitecture = Environment.Is64BitOperatingSystem ? "x64" : "x86",
                backupFormatArchitectureIndependent = true,
            },
            jobName = plan.JobName,
            sourceLayout = plan.SourceLayout.ToString(),
            selectedItemIds = plan.SelectedItemIds,
            status = result.Status.ToString(),
            startedAt = result.StartedAt,
            finishedAt = result.FinishedAt,
            copiedFiles = result.CopiedFiles,
            failedFiles = result.FailedFiles,
            copiedBytes = result.CopiedBytes,
            plannedBytes = plan.TotalBytes,
            volumeShadowCopy = new
            {
                copiedFiles = result.VolumeShadowCopyFiles,
                result.Warnings,
            },
            copyPerformance = new
            {
                _copyOptions.Strategy,
                _copyOptions.MaxConcurrentCopies,
                _copyOptions.MaxConcurrentLargeFiles,
                largeFileThresholdBytes = LargeFileThreshold,
            },
            accessControl = new
            {
                owner = "Everyone",
                permission = "FullControl",
                required = plan.ApplyAccessControl,
                destinationFileSystem = plan.DestinationFileSystem,
                result.AccessControl.Attempted,
                result.AccessControl.Succeeded,
                result.AccessControl.ProcessedEntries,
                result.AccessControl.FailedEntries,
                result.AccessControl.SkipReason,
                result.AccessControl.Errors,
            },
            cloudFiles = new
            {
                policy = "SkipNotFullyPresentLocally",
                skippedFiles = result.SkippedOnlineOnlyFiles,
                skippedLogicalBytes = result.SkippedOnlineOnlyBytes,
            },
            supplements = result.Supplements,
            files = result.Files,
        };

        var settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new StringEnumConverter() },
        };
        return JsonConvert.SerializeObject(manifest, settings);
    }

    private static string FriendlyCopyError(Exception exception) =>
        exception switch
        {
            LockedFileSnapshotException =>
                "ロック中ファイルをVSSスナップショットからも読み取れませんでした。",
            UnauthorizedAccessException => "アクセス権がなく読み取れませんでした。",
            FileNotFoundException => "計画後に元ファイルが見つからなくなりました。",
            DirectoryNotFoundException => "計画後に元フォルダーが見つからなくなりました。",
            IOException => "ファイルの読み書きに失敗しました。",
            _ => "ファイルをコピーできませんでした。",
        };

    private sealed class OnlineOnlyFileSkippedException : IOException
    {
        public OnlineOnlyFileSkippedException()
            : base("オンライン専用ファイルはコピー対象外です。")
        {
        }
    }
}
