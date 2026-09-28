using System.Security.Principal;

namespace Ytec.WindowsBackup.Windows;

public static class WindowsBackupEnvironment
{
    public static string GetDefaultSourceRoot()
    {
        var systemDirectory = Environment.SystemDirectory;
        return Path.GetPathRoot(systemDirectory) ?? "C:\\";
    }

    public static string GetCatalogPath() =>
        Path.Combine(AppContext.BaseDirectory, "config", "backup-items.v1.json");

    public static bool IsProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
