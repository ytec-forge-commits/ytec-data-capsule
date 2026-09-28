using Ytec.WindowsBackup.Core.Models;

namespace Ytec.WindowsBackup.Core.Services;

public interface IBackupFinalizer
{
    Task<BackupFinalizationResult> FinalizeAsync(
        string destinationRoot,
        string outputDirectory,
        IProgress<BackupFinalizationProgress>? progress,
        CancellationToken cancellationToken);
}
