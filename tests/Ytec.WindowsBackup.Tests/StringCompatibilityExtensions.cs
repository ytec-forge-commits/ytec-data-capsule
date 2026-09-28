namespace Ytec.WindowsBackup.Tests;

internal static class StringCompatibilityExtensions
{
    public static bool Contains(
        this string source,
        string value,
        StringComparison comparisonType) =>
        source.IndexOf(value, comparisonType) >= 0;
}
