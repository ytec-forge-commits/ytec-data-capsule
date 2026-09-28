using System.Globalization;

namespace Ytec.WindowsBackup.Windows;

public static class WindowsSourceDriveDiscovery
{
    public static IReadOnlyList<DetectedWindowsDrive> Discover()
    {
        var drives = new List<DetectedWindowsDrive>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady ||
                    drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                {
                    continue;
                }

                var rootPath = drive.RootDirectory.FullName;
                if (!ContainsWindowsDirectory(rootPath))
                {
                    continue;
                }

                var volumeLabel = drive.VolumeLabel.Trim();
                var detectedText = IsJapaneseUi()
                    ? "Windowsを検出"
                    : "Windows detected";
                var displayName = string.IsNullOrWhiteSpace(volumeLabel)
                    ? $"{rootPath}  ({detectedText})"
                    : $"{rootPath}  {volumeLabel} ({detectedText})";
                drives.Add(new DetectedWindowsDrive(rootPath, displayName));
            }
            catch (Exception exception) when (
                exception is ArgumentException or IOException or
                UnauthorizedAccessException or System.Security.SecurityException)
            {
                // 読み取れないドライブは候補へ表示しない。
            }
        }

        return drives
            .OrderBy(drive => drive.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<DetectedWindowsDrive> DiscoverFromRoots(
        IEnumerable<string> candidateRoots)
    {
        if (candidateRoots is null)
        {
            throw new ArgumentNullException(nameof(candidateRoots));
        }

        var drives = new List<DetectedWindowsDrive>();
        foreach (var candidateRoot in candidateRoots)
        {
            if (string.IsNullOrWhiteSpace(candidateRoot))
            {
                continue;
            }

            string rootPath;
            try
            {
                rootPath = Path.GetFullPath(candidateRoot);
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or
                PathTooLongException)
            {
                continue;
            }

            if (!ContainsWindowsDirectory(rootPath))
            {
                continue;
            }

            drives.Add(new DetectedWindowsDrive(
                rootPath,
                IsJapaneseUi()
                    ? $"{rootPath}  （Windowsを検出）"
                    : $"{rootPath}  (Windows detected)"));
        }

        return drives
            .GroupBy(drive => drive.RootPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(drive => drive.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ContainsWindowsDirectory(string rootPath)
    {
        try
        {
            if (!Directory.Exists(rootPath))
            {
                return false;
            }

            var windowsDirectory = Path.Combine(rootPath, "Windows");
            return Directory.Exists(windowsDirectory) &&
                (File.GetAttributes(windowsDirectory) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or
            UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool IsJapaneseUi() =>
        string.Equals(
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            "ja",
            StringComparison.OrdinalIgnoreCase);
}

public sealed class DetectedWindowsDrive
{
    public DetectedWindowsDrive(string rootPath, string displayName)
    {
        RootPath = rootPath;
        DisplayName = displayName;
    }

    public string RootPath { get; }

    public string DisplayName { get; }
}
