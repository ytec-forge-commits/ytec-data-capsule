using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Ytec.WindowsBackup.Windows;

public sealed class NetshWifiProfileCommandRunner : IWifiProfileCommandRunner
{
    public async Task ExportAllProfilesAsync(
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var exitCode = await RunNetshAsync(
            ["wlan", "export", "profile", "key=clear", $"folder={outputDirectory}"],
            cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Wi-Fi設定を取得できませんでした。netsh終了コード: {exitCode}");
        }
    }

    public async Task<bool> ImportProfileAsync(
        string profileXmlPath,
        CancellationToken cancellationToken)
    {
        var exitCode = await RunNetshAsync(
            ["wlan", "add", "profile", $"filename={profileXmlPath}", "user=all"],
            cancellationToken).ConfigureAwait(false);
        return exitCode == 0;
    }

    private static async Task<int> RunNetshAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = TrustedWindowsTools.NetshExecutablePath,
            Arguments = string.Join(" ", arguments.Select(QuoteArgument)),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("netshを起動できませんでした。");
            }

            try
            {
                await Task.Run(
                    () =>
                    {
                        while (!process.WaitForExit(200))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch (InvalidOperationException)
                {
                }

                throw;
            }

            return process.ExitCode;
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "WindowsのWi-Fi管理機能を起動できませんでした。",
                exception);
        }
    }

    private static string QuoteArgument(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        if (argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
        {
            return argument;
        }

        var builder = new StringBuilder(argument.Length + 2);
        builder.Append('"');
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            builder.Append('\\', backslashes);
            backslashes = 0;
            builder.Append(character);
        }

        builder.Append('\\', backslashes * 2);
        builder.Append('"');
        return builder.ToString();
    }
}
