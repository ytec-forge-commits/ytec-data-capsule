using Ytec.WindowsBackup.Core.Models;

namespace Ytec.WindowsBackup.Core.Services;

public static class WindowsSourceDetector
{
    private static readonly HashSet<string> ModernExcludedProfiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "All Users",
            "Default",
            "Default User",
            "defaultuser0",
            "Public",
            "WDAGUtilityAccount",
        };

    private static readonly HashSet<string> LegacyExcludedProfiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "All Users",
            "Default User",
            "LocalService",
            "NetworkService",
        };

    public static WindowsSourceDetection Detect(string sourceRoot)
    {
        var modernProfiles = Path.Combine(sourceRoot, "Users");
        if (Directory.Exists(Path.Combine(sourceRoot, "Windows")) &&
            Directory.Exists(modernProfiles))
        {
            return new WindowsSourceDetection(WindowsSourceLayout.Modern, modernProfiles);
        }

        var legacyProfiles = Path.Combine(sourceRoot, "Documents and Settings");
        if (Directory.Exists(Path.Combine(sourceRoot, "WINDOWS")) &&
            Directory.Exists(legacyProfiles))
        {
            return new WindowsSourceDetection(WindowsSourceLayout.LegacyXp, legacyProfiles);
        }

        throw new InvalidOperationException(
            "Windowsフォルダーとユーザープロファイルを確認できませんでした。元のWindowsドライブを指定してください。");
    }

    public static IReadOnlyList<string> GetUserProfileDirectories(
        WindowsSourceLayout layout,
        string profilesRoot)
    {
        var exclusions = layout == WindowsSourceLayout.Modern
            ? ModernExcludedProfiles
            : LegacyExcludedProfiles;

        try
        {
            return Directory
                .EnumerateDirectories(profilesRoot, "*", SearchOption.TopDirectoryOnly)
                .Where(path => !exclusions.Contains(Path.GetFileName(path)))
                .Where(path =>
                    (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException(
                "ユーザープロファイル一覧を読み取れません。管理者権限を確認してください。",
                exception);
        }
    }
}

public sealed class WindowsSourceDetection
{
    public WindowsSourceDetection(WindowsSourceLayout layout, string profilesRoot)
    {
        Layout = layout;
        ProfilesRoot = profilesRoot;
    }

    public WindowsSourceLayout Layout { get; }

    public string ProfilesRoot { get; }
}
