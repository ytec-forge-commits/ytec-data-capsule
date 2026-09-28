using System.Globalization;
using System.Security;
using Microsoft.Win32;

namespace Ytec.WindowsBackup.Windows;

public static class WindowsRuntimeInfo
{
    private const int Windows11FirstBuild = 22000;

    public static string BuildDescription(bool japanese = true)
    {
        var productName = "Windows";
        var buildNumber = Environment.OSVersion.Version.Build;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            productName = key?.GetValue("ProductName") as string ?? productName;
            var currentBuild = key?.GetValue("CurrentBuildNumber") as string;
            if (int.TryParse(
                currentBuild,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var registryBuild))
            {
                buildNumber = registryBuild;
            }
        }
        catch (SecurityException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        productName = NormalizeProductName(productName, buildNumber);
        var osBits = Environment.Is64BitOperatingSystem
            ? japanese ? "64ビットOS" : "64-bit OS"
            : japanese ? "32ビットOS" : "32-bit OS";
        var processBits = Environment.Is64BitProcess
            ? japanese ? "64ビット動作" : "64-bit process"
            : japanese ? "32ビット動作" : "32-bit process";
        return japanese
            ? $"{productName} / {osBits} / {processBits}（自動判定）"
            : $"{productName} / {osBits} / {processBits} (auto-detected)";
    }

    public static string NormalizeProductName(string? productName, int buildNumber)
    {
        var normalized = string.IsNullOrWhiteSpace(productName)
            ? "Windows"
            : productName!.Trim();
        if (buildNumber >= Windows11FirstBuild &&
            normalized.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows 11" + normalized.Substring("Windows 10".Length);
        }

        return normalized;
    }
}
