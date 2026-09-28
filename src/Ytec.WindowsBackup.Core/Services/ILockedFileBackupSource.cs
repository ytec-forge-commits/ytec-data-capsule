namespace Ytec.WindowsBackup.Core.Services;

public interface ILockedFileBackupSource : IDisposable
{
    Stream OpenRead(string sourcePath);

    IReadOnlyList<string> Complete();
}

public sealed class LockedFileSnapshotException : IOException
{
    public LockedFileSnapshotException(string message)
        : base(message)
    {
    }

    public LockedFileSnapshotException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
