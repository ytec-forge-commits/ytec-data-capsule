namespace Ytec.WindowsBackup.Windows;

public interface IWifiProfileCommandRunner
{
    Task ExportAllProfilesAsync(
        string outputDirectory,
        CancellationToken cancellationToken);

    Task<bool> ImportProfileAsync(
        string profileXmlPath,
        CancellationToken cancellationToken);
}
