namespace Ytec.WindowsBackup.Windows;

public static class TrustedWindowsTools
{
    public static string NetshExecutablePath => ResolveExistingExecutable(
        Environment.SpecialFolder.System,
        "netsh.exe");

    public static string ExplorerExecutablePath => ResolveExistingExecutable(
        Environment.SpecialFolder.Windows,
        "explorer.exe");

    private static string ResolveExistingExecutable(
        Environment.SpecialFolder folder,
        string executableName)
    {
        var root = Environment.GetFolderPath(folder);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                "Windowsのシステムフォルダーを解決できませんでした。");
        }

        var executablePath = Path.GetFullPath(Path.Combine(root, executableName));
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "Windowsのシステム実行ファイルが見つかりません。",
                executablePath);
        }

        return executablePath;
    }
}
