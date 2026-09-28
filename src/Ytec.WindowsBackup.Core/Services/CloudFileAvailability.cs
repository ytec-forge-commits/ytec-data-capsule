namespace Ytec.WindowsBackup.Core.Services;

public static class CloudFileAvailability
{
    private const FileAttributes RecallOnOpen =
        (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess =
        (FileAttributes)0x00400000;

    public static bool IsNotFullyPresentLocally(FileAttributes attributes) =>
        (attributes & FileAttributes.Offline) != 0 ||
        (attributes & RecallOnOpen) != 0 ||
        (attributes & RecallOnDataAccess) != 0;
}
