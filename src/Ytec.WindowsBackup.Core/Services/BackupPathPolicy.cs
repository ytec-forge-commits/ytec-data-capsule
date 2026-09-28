namespace Ytec.WindowsBackup.Core.Services;

public static class BackupPathPolicy
{
    public static string NormalizeExistingDirectory(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("フォルダーを指定してください。", parameterName);
        }

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"フォルダーが見つかりません: {fullPath}");
        }

        return TrimEndingSeparators(fullPath);
    }

    public static void EnsureDestinationOutsideSource(string sourceRoot, string destinationRoot)
    {
        if (IsSameOrSubPath(destinationRoot, sourceRoot))
        {
            throw new InvalidOperationException("バックアップ先は元ドライブの外側を指定してください。");
        }
    }

    public static void EnsureSafeRelativeDefinitionPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            throw new InvalidDataException($"相対パスではありません: {path}");
        }

        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split(
            new[] { '/' },
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 ||
            segments.Length > 32 ||
            normalized.Length > 1024 ||
            segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException($"安全でない相対パスです: {path}");
        }

        var invalidNameCharacters = new HashSet<char>(
            Path.GetInvalidFileNameChars()
                .Where(character => character is not '*' and not '?'));
        foreach (var segment in segments)
        {
            if (segment.IndexOfAny(invalidNameCharacters.ToArray()) >= 0 ||
                segment.IndexOf("**", StringComparison.Ordinal) >= 0 ||
                segment.EndsWith(" ", StringComparison.Ordinal) ||
                segment.EndsWith(".", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"安全でないパス要素です: {path}");
            }
        }

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new InvalidDataException($"使用できない文字を含むパスです: {path}");
        }
    }

    public static string EnsurePathUnderRoot(string root, string candidate, string message)
    {
        var normalizedRoot = TrimEndingSeparators(Path.GetFullPath(root));
        var normalizedCandidate = Path.GetFullPath(candidate);
        if (!IsSameOrSubPath(normalizedCandidate, normalizedRoot))
        {
            throw new InvalidOperationException(message);
        }

        return normalizedCandidate;
    }

    public static bool IsSameOrSubPath(string candidate, string root)
    {
        var comparison = StringComparison.OrdinalIgnoreCase;
        var normalizedCandidate = AppendSeparator(TrimEndingSeparators(Path.GetFullPath(candidate)));
        var normalizedRoot = AppendSeparator(TrimEndingSeparators(Path.GetFullPath(root)));
        return normalizedCandidate.StartsWith(normalizedRoot, comparison);
    }

    public static string GetRelativePath(string root, string candidate)
    {
        var normalizedRoot = TrimEndingSeparators(Path.GetFullPath(root));
        var normalizedCandidate = Path.GetFullPath(candidate);
        if (string.Equals(
                normalizedRoot,
                normalizedCandidate,
                StringComparison.OrdinalIgnoreCase))
        {
            return ".";
        }

        var rootWithSeparator = AppendSeparator(normalizedRoot);
        if (!normalizedCandidate.StartsWith(
                rootWithSeparator,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("基準フォルダー外の相対パスは作成できません。");
        }

        return normalizedCandidate.Substring(rootWithSeparator.Length);
    }

    public static string ToManifestPath(string relativePath) =>
        relativePath.Replace('\\', '/');

    private static string TrimEndingSeparators(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var end = path.Length;
        while (end > 0 &&
               (path[end - 1] == Path.DirectorySeparatorChar ||
                path[end - 1] == Path.AltDirectorySeparatorChar))
        {
            end--;
        }

        return path.Substring(0, end);
    }

    private static string AppendSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
        path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
}
