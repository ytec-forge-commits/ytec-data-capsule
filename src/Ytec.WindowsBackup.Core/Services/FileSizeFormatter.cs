namespace Ytec.WindowsBackup.Core.Services;

public static class FileSizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        var value = Math.Max(0, bytes);
        var unitIndex = 0;
        var displayValue = (double)value;
        while (displayValue >= 1024 && unitIndex < Units.Length - 1)
        {
            displayValue /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{value:N0} {Units[unitIndex]}"
            : $"{displayValue:N2} {Units[unitIndex]}";
    }
}
