namespace Ytec.WindowsBackup.Core.Services;

public static class BackupFolderNamePolicy
{
    private const int MaximumFolderNameLength = 100;
    private static readonly HashSet<string> ReservedDeviceNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "LPT1",
            "LPT2",
            "LPT3",
            "LPT4",
            "LPT5",
            "LPT6",
            "LPT7",
            "LPT8",
            "LPT9",
        };

    public static string Validate(string? requestedName)
    {
        var rawName = requestedName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawName))
        {
            throw new InvalidOperationException(
                "新規バックアップフォルダー名を入力してください。");
        }

        if (rawName.EndsWith(".", StringComparison.Ordinal) ||
            char.IsWhiteSpace(rawName[rawName.Length - 1]))
        {
            throw new InvalidOperationException(
                "新規バックアップフォルダー名の末尾に空白やピリオドは使用できません。");
        }

        var name = rawName.Trim();
        if (name.Length > MaximumFolderNameLength)
        {
            throw new InvalidOperationException(
                $"新規バックアップフォルダー名は{MaximumFolderNameLength:N0}文字以内で入力してください。");
        }

        if (name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains(Path.DirectorySeparatorChar) ||
            name.Contains(Path.AltDirectorySeparatorChar) ||
            name.EndsWith(".", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "新規バックアップフォルダー名に、パス区切り・禁止文字・末尾の空白やピリオドは使用できません。");
        }

        var deviceName = name.Split('.')[0].TrimEnd();
        if (ReservedDeviceNames.Contains(deviceName))
        {
            throw new InvalidOperationException(
                "この新規バックアップフォルダー名はWindowsで予約されているため使用できません。");
        }

        return name;
    }

    public static string GetOutputDirectory(string destinationRoot, string requestedName)
    {
        var normalizedDestination = BackupPathPolicy.NormalizeExistingDirectory(
            destinationRoot,
            nameof(destinationRoot));
        var name = Validate(requestedName);
        var outputDirectory = BackupPathPolicy.EnsurePathUnderRoot(
            normalizedDestination,
            Path.Combine(normalizedDestination, name),
            "新規バックアップフォルダーが保存場所の外側を指しています。");
        var parent = Directory.GetParent(outputDirectory)?.FullName;
        if (parent is null ||
            !string.Equals(
                Path.GetFullPath(parent),
                Path.GetFullPath(normalizedDestination),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "新規バックアップフォルダーは保存場所の直下に作成してください。");
        }

        return outputDirectory;
    }

    public static void EnsureDoesNotExist(string outputDirectory)
    {
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
        {
            throw new InvalidOperationException(
                "同じ名前のファイルまたはフォルダーがすでにあります。別の新規フォルダー名を入力してください。");
        }
    }
}
