namespace Ytec.WindowsBackup.Windows;

public enum BrowserBookmarkKind
{
    Chrome,
    Edge,
    Firefox,
}

public sealed record BrowserBookmarkBackupEntry(
    string Id,
    BrowserBookmarkKind Browser,
    string DisplayName,
    string SourcePath,
    string BackupUser,
    string BackupProfile);

public sealed record BrowserBookmarkTarget(
    string Id,
    BrowserBookmarkKind Browser,
    string DisplayName,
    string ProfileDirectory);

public sealed record BrowserBookmarkRestoreResult(
    BrowserBookmarkKind Browser,
    string TargetDisplayName,
    string RollbackDirectory);

public sealed record BrowserRestoreEnvironment(
    string ChromeUserDataRoot,
    string EdgeUserDataRoot,
    string FirefoxProfilesRoot)
{
    public static BrowserRestoreEnvironment CurrentUser()
    {
        var local = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        return new BrowserRestoreEnvironment(
            Path.Combine(local, "Google", "Chrome", "User Data"),
            Path.Combine(local, "Microsoft", "Edge", "User Data"),
            Path.Combine(roaming, "Mozilla", "Firefox", "Profiles"));
    }
}
