namespace Ytec.WindowsBackup.Core.Models;

public sealed record WifiContainerContents(
    DateTimeOffset CreatedAt,
    IReadOnlyList<byte[]> Profiles);

public sealed record WifiBackupOperationResult(
    string OutputDirectory,
    string ContainerPath,
    int ProfileCount,
    BackupFinalizationResult AccessControl);

public sealed record WifiRestoreOperationResult(
    int TotalProfiles,
    int ImportedProfiles,
    int FailedProfiles,
    IReadOnlyList<string> Errors);

public sealed record WifiOperationProgress(
    WifiOperationStage Stage,
    int CompletedProfiles,
    int TotalProfiles,
    string Message);

public enum WifiOperationStage
{
    Exporting,
    Encrypting,
    ApplyingAccessControl,
    Decrypting,
    Restoring,
    Finished,
}
