namespace Ytec.WindowsBackup.Core.Models;

public sealed class BackupPlan
{
    public required string JobName { get; init; }

    public required string SourceRoot { get; init; }

    public required string DestinationRoot { get; init; }

    public required WindowsSourceLayout SourceLayout { get; init; }

    public required IReadOnlyList<string> SelectedItemIds { get; init; }

    public required IReadOnlyList<string> SupplementItemIds { get; init; }

    public required IReadOnlyList<BackupCandidate> Candidates { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public int SkippedOnlineOnlyFiles { get; init; }

    public long SkippedOnlineOnlyBytes { get; init; }

    public long TotalBytes => Candidates.Sum(candidate => candidate.Length);

    public long? AvailableBytes { get; init; }

    public string? DestinationFileSystem { get; init; }

    public bool? DestinationSupportsAccessControl { get; init; }

    public bool DestinationAllowsBackup { get; init; } = true;

    public bool ApplyAccessControl => DestinationSupportsAccessControl != false;

    public bool HasEnoughSpace => AvailableBytes is null || AvailableBytes >= TotalBytes;

    public bool CanExecute =>
        (Candidates.Count > 0 || SupplementItemIds.Count > 0) &&
        HasEnoughSpace &&
        DestinationAllowsBackup;
}

public sealed record BackupCandidate(
    IReadOnlyList<string> ItemIds,
    string SourcePath,
    string RelativePath,
    long Length,
    DateTime LastWriteTimeUtc);

public sealed record BackupCopyOptions(
    int MaxConcurrentCopies,
    int MaxConcurrentLargeFiles,
    string Strategy)
{
    public static BackupCopyOptions Balanced { get; } =
        new(4, 1, "自動・標準");
}

public enum WindowsSourceLayout
{
    Modern,
    LegacyXp,
}
