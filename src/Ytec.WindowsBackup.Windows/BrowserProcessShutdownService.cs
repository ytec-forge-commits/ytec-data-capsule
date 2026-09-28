using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace Ytec.WindowsBackup.Windows;

public enum BrowserProcessKind
{
    InternetExplorer,
    Chrome,
    Edge,
    Firefox,
}

public sealed record BrowserProcessRule(
    BrowserProcessKind Kind,
    string DisplayName,
    string ProcessName,
    IReadOnlyList<string> ExpectedExecutablePaths);

public sealed record BrowserProcessShutdownResult(
    int MatchedProcessCount,
    int NormalCloseRequestCount,
    int ForcedTerminationCount);

/// <summary>
/// 現在のWindowsセッションにある対象ブラウザーだけを、実行ファイルの
/// 完全パスを確認して終了します。
/// </summary>
public sealed class BrowserProcessShutdownService
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromMilliseconds(150);

    public static IReadOnlyList<BrowserProcessRule> ForBackupItems(
        IEnumerable<string> itemIds)
    {
        if (itemIds is null)
        {
            throw new ArgumentNullException(nameof(itemIds));
        }

        var ids = new HashSet<string>(
            itemIds,
            StringComparer.OrdinalIgnoreCase);
        var kinds = new List<BrowserProcessKind>();
        if (ids.Contains("favorites"))
        {
            kinds.Add(BrowserProcessKind.InternetExplorer);
        }

        if (ids.Contains("chrome-bookmarks"))
        {
            kinds.Add(BrowserProcessKind.Chrome);
        }

        if (ids.Contains("edge-bookmarks"))
        {
            kinds.Add(BrowserProcessKind.Edge);
        }

        if (ids.Contains("firefox-bookmarks"))
        {
            kinds.Add(BrowserProcessKind.Firefox);
        }

        return CreateRules(kinds);
    }

    public static IReadOnlyList<BrowserProcessRule> ForBookmarkRestore(
        BrowserBookmarkKind browser) =>
        CreateRules(
        [
            browser switch
            {
                BrowserBookmarkKind.Chrome => BrowserProcessKind.Chrome,
                BrowserBookmarkKind.Edge => BrowserProcessKind.Edge,
                BrowserBookmarkKind.Firefox => BrowserProcessKind.Firefox,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(browser),
                    browser,
                    "未対応のブラウザーです。"),
            },
        ]);

    public static bool IsCurrentWindowsRoot(string sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            return false;
        }

        var windowsDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.Windows);
        var currentRoot = Path.GetPathRoot(windowsDirectory);
        if (string.IsNullOrWhiteSpace(currentRoot))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(sourceRoot)
                    .TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(currentRoot)
                    .TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or
            PathTooLongException)
        {
            return false;
        }
    }

    public static bool ExecutablePathMatches(
        string? actualPath,
        IReadOnlyList<string> expectedPaths)
    {
        if (string.IsNullOrWhiteSpace(actualPath) || expectedPaths is null)
        {
            return false;
        }

        string normalizedActual;
        try
        {
            normalizedActual = Path.GetFullPath(actualPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or
            PathTooLongException)
        {
            return false;
        }

        foreach (var expectedPath in expectedPaths)
        {
            if (string.IsNullOrWhiteSpace(expectedPath))
            {
                continue;
            }

            try
            {
                if (string.Equals(
                    normalizedActual,
                    Path.GetFullPath(expectedPath),
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or
                PathTooLongException)
            {
                // 不正な候補だけを無視します。
            }
        }

        return false;
    }

    public async Task<BrowserProcessShutdownResult> StopAsync(
        IReadOnlyList<BrowserProcessRule> rules,
        TimeSpan normalCloseWait,
        TimeSpan forceCloseWait,
        CancellationToken cancellationToken = default)
    {
        if (rules is null)
        {
            throw new ArgumentNullException(nameof(rules));
        }

        if (normalCloseWait < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(normalCloseWait));
        }

        if (forceCloseWait < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(forceCloseWait));
        }

        var normalizedRules = rules
            .Where(rule =>
                !string.IsNullOrWhiteSpace(rule.ProcessName) &&
                rule.ExpectedExecutablePaths.Count > 0)
            .GroupBy(
                rule => $"{rule.ProcessName}\0{rule.Kind}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (normalizedRules.Length == 0)
        {
            return new BrowserProcessShutdownResult(0, 0, 0);
        }

        var initial = FindMatchingProcesses(
            normalizedRules,
            cancellationToken);
        var initialCount = initial.Count;
        var normalRequests = 0;
        try
        {
            foreach (var process in initial)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (process.CloseMainWindow())
                    {
                        normalRequests++;
                    }
                }
                catch (Exception exception) when (
                    exception is Win32Exception or InvalidOperationException or
                    NotSupportedException)
                {
                    // 通常終了できないプロセスは強制終了前の残存確認へ回します。
                }
            }
        }
        finally
        {
            DisposeAll(initial);
        }

        await WaitForNoMatchingProcessesAsync(
            normalizedRules,
            normalCloseWait,
            cancellationToken).ConfigureAwait(false);

        var remaining = FindMatchingProcesses(
            normalizedRules,
            cancellationToken);
        var forced = 0;
        try
        {
            foreach (var process in remaining)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    process.Kill();
                    forced++;
                }
                catch (Exception exception) when (
                    exception is Win32Exception or InvalidOperationException or
                    NotSupportedException)
                {
                    // 最後の残存確認で安全側に失敗させます。
                }
            }
        }
        finally
        {
            DisposeAll(remaining);
        }

        await WaitForNoPotentialProcessesAsync(
            normalizedRules,
            forceCloseWait,
            cancellationToken).ConfigureAwait(false);
        var stillRunning = FindPotentialProcessNames(
            normalizedRules,
            cancellationToken);
        if (stillRunning.Count > 0)
        {
            throw new InvalidOperationException(
                "次のブラウザーを安全に終了できなかったため、処理を開始しません。\n" +
                string.Join("、", stillRunning) +
                "\nブラウザーを手動で終了してから、もう一度実行してください。");
        }

        return new BrowserProcessShutdownResult(
            initialCount,
            normalRequests,
            forced);
    }

    private static IReadOnlyList<BrowserProcessRule> CreateRules(
        IEnumerable<BrowserProcessKind> kinds)
    {
        return kinds
            .Distinct()
            .Select(kind => kind switch
            {
                BrowserProcessKind.InternetExplorer => CreateRule(
                    kind,
                    "Internet Explorer",
                    "iexplore",
                    "iexplore.exe",
                    Path.Combine("Internet Explorer", "iexplore.exe")),
                BrowserProcessKind.Chrome => CreateRule(
                    kind,
                    "Google Chrome",
                    "chrome",
                    "chrome.exe",
                    Path.Combine(
                        "Google",
                        "Chrome",
                        "Application",
                        "chrome.exe")),
                BrowserProcessKind.Edge => CreateRule(
                    kind,
                    "Microsoft Edge",
                    "msedge",
                    "msedge.exe",
                    Path.Combine(
                        "Microsoft",
                        "Edge",
                        "Application",
                        "msedge.exe")),
                BrowserProcessKind.Firefox => CreateRule(
                    kind,
                    "Mozilla Firefox",
                    "firefox",
                    "firefox.exe",
                    Path.Combine("Mozilla Firefox", "firefox.exe")),
                _ => throw new ArgumentOutOfRangeException(nameof(kinds)),
            })
            .ToArray();
    }

    private static BrowserProcessRule CreateRule(
        BrowserProcessKind kind,
        string displayName,
        string processName,
        string appPathExecutableName,
        string installedRelativePath)
    {
        var candidates = new List<string>();
        AddInstalledPath(
            candidates,
            Environment.SpecialFolder.ProgramFiles,
            installedRelativePath);
        AddInstalledPath(
            candidates,
            Environment.SpecialFolder.ProgramFilesX86,
            installedRelativePath);
        AddInstalledPath(
            candidates,
            Environment.SpecialFolder.LocalApplicationData,
            installedRelativePath);
        candidates.AddRange(ReadRegisteredAppPaths(appPathExecutableName));

        return new BrowserProcessRule(
            kind,
            displayName,
            processName,
            candidates
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static void AddInstalledPath(
        ICollection<string> candidates,
        Environment.SpecialFolder folder,
        string relativePath)
    {
        var root = Environment.GetFolderPath(folder);
        if (!string.IsNullOrWhiteSpace(root))
        {
            candidates.Add(Path.Combine(root, relativePath));
        }
    }

    private static IEnumerable<string> ReadRegisteredAppPaths(
        string executableName)
    {
        var paths = new List<string>();
        var subKey =
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{executableName}";
        foreach (var hive in new[]
        {
            RegistryHive.CurrentUser,
            RegistryHive.LocalMachine,
        })
        {
            foreach (var view in new[]
            {
                RegistryView.Registry32,
                RegistryView.Registry64,
            })
            {
                RegistryKey? baseKey = null;
                RegistryKey? appKey = null;
                try
                {
                    baseKey = RegistryKey.OpenBaseKey(hive, view);
                    appKey = baseKey.OpenSubKey(subKey, writable: false);
                    var value = appKey?.GetValue(null) as string;
                    var path = NormalizeRegisteredPath(value);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        paths.Add(path!);
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or
                    System.Security.SecurityException or ArgumentException)
                {
                    // 読み取れない登録場所だけを無視します。
                }
                finally
                {
                    appKey?.Dispose();
                    baseKey?.Dispose();
                }
            }
        }

        return paths;
    }

    private static string? NormalizeRegisteredPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(value!.Trim());
        if (expanded.StartsWith("\"", StringComparison.Ordinal))
        {
            var closingQuote = expanded.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                expanded = expanded.Substring(1, closingQuote - 1);
            }
        }

        return expanded.Trim().Trim('"');
    }

    private static List<Process> FindMatchingProcesses(
        IReadOnlyList<BrowserProcessRule> rules,
        CancellationToken cancellationToken)
    {
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var result = new Dictionary<int, Process>();
        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var process in Process.GetProcessesByName(rule.ProcessName))
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (process.SessionId == currentSessionId &&
                        ExecutablePathMatches(
                            process.MainModule?.FileName,
                            rule.ExpectedExecutablePaths) &&
                        !result.ContainsKey(process.Id))
                    {
                        result.Add(process.Id, process);
                        continue;
                    }
                }
                catch (Exception exception) when (
                    exception is Win32Exception or InvalidOperationException or
                    NotSupportedException)
                {
                    // 完全パスを確認できないプロセスは操作しません。
                }

                process.Dispose();
            }
        }

        return result.Values.ToList();
    }

    private static IReadOnlyList<string> FindPotentialProcessNames(
        IReadOnlyList<BrowserProcessRule> rules,
        CancellationToken cancellationToken)
    {
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var process in Process.GetProcessesByName(rule.ProcessName))
            {
                using (process)
                {
                    try
                    {
                        if (process.SessionId == currentSessionId)
                        {
                            names.Add(rule.DisplayName);
                        }
                    }
                    catch (Exception exception) when (
                        exception is Win32Exception or InvalidOperationException or
                        NotSupportedException)
                    {
                        names.Add(rule.DisplayName);
                    }
                }
            }
        }

        return names
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task WaitForNoMatchingProcessesAsync(
        IReadOnlyList<BrowserProcessRule> rules,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            var processes = FindMatchingProcesses(rules, cancellationToken);
            try
            {
                if (processes.Count == 0)
                {
                    return;
                }
            }
            finally
            {
                DisposeAll(processes);
            }

            if (DateTime.UtcNow >= deadline)
            {
                return;
            }

            await Task.Delay(PollInterval, cancellationToken)
                .ConfigureAwait(false);
        }
        while (true);
    }

    private static async Task WaitForNoPotentialProcessesAsync(
        IReadOnlyList<BrowserProcessRule> rules,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            if (FindPotentialProcessNames(rules, cancellationToken).Count == 0)
            {
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return;
            }

            await Task.Delay(PollInterval, cancellationToken)
                .ConfigureAwait(false);
        }
        while (true);
    }

    private static void DisposeAll(IEnumerable<Process> processes)
    {
        foreach (var process in processes)
        {
            process.Dispose();
        }
    }
}
