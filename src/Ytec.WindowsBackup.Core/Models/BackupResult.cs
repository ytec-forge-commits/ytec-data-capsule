namespace Ytec.WindowsBackup.Core.Models;

public sealed class BackupResult
{
    public required string OutputDirectory { get; init; }

    public required BackupRunStatus Status { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset FinishedAt { get; init; }

    public required IReadOnlyList<BackupFileResult> Files { get; init; }

    public required BackupFinalizationResult AccessControl { get; init; }

    public IReadOnlyList<BackupSupplementResult> Supplements { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public int CopiedFiles => Files.Count(file => file.Succeeded);

    public int FailedFiles => Files.Count(file => !file.Succeeded && !file.Skipped);

    public int SkippedOnlineOnlyFiles =>
        SkippedOnlineOnlyFilesDuringPlanning +
        Files.Count(file => file.Skipped);

    public long SkippedOnlineOnlyBytes =>
        SkippedOnlineOnlyBytesDuringPlanning +
        Files.Where(file => file.Skipped).Sum(file => file.Length);

    public int SkippedOnlineOnlyFilesDuringPlanning { get; init; }

    public long SkippedOnlineOnlyBytesDuringPlanning { get; init; }

    public long CopiedBytes => Files.Where(file => file.Succeeded).Sum(file => file.Length);

    public int VolumeShadowCopyFiles =>
        Files.Count(file =>
            file.Succeeded &&
            file.CopySource == BackupCopySource.VolumeShadowCopy);
}

public sealed record BackupFileResult(
    string RelativePath,
    IReadOnlyList<string> ItemIds,
    long Length,
    bool Succeeded,
    string? ErrorCode,
    string? ErrorMessage,
    BackupCopySource CopySource = BackupCopySource.Direct,
    bool Skipped = false);

public enum BackupCopySource
{
    Direct,
    VolumeShadowCopy,
}

public sealed record BackupProgress(
    BackupProgressStage Stage,
    int CompletedFiles,
    int TotalFiles,
    long CompletedBytes,
    long TotalBytes,
    string CurrentRelativePath,
    string Message);

public enum BackupProgressStage
{
    Copying,
    ProcessingSupplement,
    ApplyingAccessControl,
    WritingManifest,
    Finished,
}

public enum BackupRunStatus
{
    Completed,
    CompletedWithWarnings,
    Cancelled,
}

public sealed record BackupFinalizationResult(
    bool Attempted,
    bool Succeeded,
    int ProcessedEntries,
    int FailedEntries,
    IReadOnlyList<string> Errors,
    string? SkipReason = null)
{
    public static BackupFinalizationResult NotAttempted { get; } =
        new(false, false, 0, 0, []);

    public static BackupFinalizationResult Skipped(string reason) =>
        new(false, true, 0, 0, [], reason);
}

public sealed record BackupFinalizationProgress(
    int ProcessedEntries,
    int FailedEntries,
    string CurrentRelativePath);

public sealed record BackupSupplementResult(
    string ItemId,
    bool Attempted,
    bool Succeeded,
    int ProcessedItems,
    IReadOnlyList<string> Errors);

public sealed record BackupSupplementProgress(
    int ProcessedItems,
    int TotalItems,
    string Message);
