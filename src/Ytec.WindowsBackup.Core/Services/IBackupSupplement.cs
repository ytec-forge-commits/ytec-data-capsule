using Ytec.WindowsBackup.Core.Models;

namespace Ytec.WindowsBackup.Core.Services;

public interface IBackupSupplement
{
    string ItemId { get; }

    Task<BackupSupplementResult> ExecuteAsync(
        string outputDirectory,
        IProgress<BackupSupplementProgress>? progress,
        CancellationToken cancellationToken);
}
