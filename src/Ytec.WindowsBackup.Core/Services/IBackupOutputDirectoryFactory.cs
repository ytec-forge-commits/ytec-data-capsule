namespace Ytec.WindowsBackup.Core.Services;

public interface IBackupOutputDirectoryFactory
{
    BackupOutputDirectoryLease Create(
        string destinationRoot,
        string requestedJobName,
        bool protectDuringBackup);
}

public sealed class BackupOutputDirectoryLease : IDisposable
{
    private readonly Action? _release;
    private bool _disposed;

    public BackupOutputDirectoryLease(
        string outputDirectory,
        bool requiresFinalizationOnCancellation = false,
        Action? release = null)
    {
        OutputDirectory = outputDirectory ??
            throw new ArgumentNullException(nameof(outputDirectory));
        RequiresFinalizationOnCancellation = requiresFinalizationOnCancellation;
        _release = release;
    }

    public string OutputDirectory { get; }

    public bool RequiresFinalizationOnCancellation { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _release?.Invoke();
        _disposed = true;
    }
}

public sealed class DefaultBackupOutputDirectoryFactory : IBackupOutputDirectoryFactory
{
    public BackupOutputDirectoryLease Create(
        string destinationRoot,
        string requestedJobName,
        bool protectDuringBackup)
    {
        var outputDirectory =
            BackupFolderNamePolicy.GetOutputDirectory(destinationRoot, requestedJobName);
        BackupFolderNamePolicy.EnsureDoesNotExist(outputDirectory);

        var reservationDirectory = Path.Combine(
            destinationRoot,
            $".ytec-backup-reserve-{Guid.NewGuid():N}");
        Directory.CreateDirectory(reservationDirectory);
        try
        {
            Directory.Move(reservationDirectory, outputDirectory);
            return new BackupOutputDirectoryLease(outputDirectory);
        }
        catch (IOException exception)
        {
            throw new IOException(
                "指定した新規バックアップフォルダーを作成できませんでした。" +
                "同じ名前がないか確認してください。",
                exception);
        }
        finally
        {
            if (Directory.Exists(reservationDirectory))
            {
                Directory.Delete(reservationDirectory, recursive: false);
            }
        }
    }
}
