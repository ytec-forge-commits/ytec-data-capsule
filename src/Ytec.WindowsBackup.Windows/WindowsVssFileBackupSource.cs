using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Ytec.WindowsBackup.Core.Services;

namespace Ytec.WindowsBackup.Windows;

public sealed class WindowsVssFileBackupSource : ILockedFileBackupSource
{
    private const int CopyBufferSize = 1024 * 1024;
    private const uint GenericRead = 0x80000000;
    private const uint ShareRead = 0x00000001;
    private const uint ShareWrite = 0x00000002;
    private const uint ShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagSequentialScan = 0x08000000;
    private readonly object _sync = new();
    private readonly Dictionary<string, ShadowCopyInfo> _snapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _createdShadowCopyIds = [];
    private readonly List<string> _cleanupWarnings = [];
    private bool _completed;

    public int CreatedSnapshotCount
    {
        get
        {
            lock (_sync)
            {
                return _createdShadowCopyIds.Count;
            }
        }
    }

    public int DeletedSnapshotCount { get; private set; }

    public IReadOnlyList<string> CreatedShadowCopyIds
    {
        get
        {
            lock (_sync)
            {
                return _createdShadowCopyIds.ToArray();
            }
        }
    }

    public Stream OpenRead(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("VSSで読み取るファイルを指定してください。", nameof(sourcePath));
        }

        var fullPath = Path.GetFullPath(sourcePath);
        var volumeRoot = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(volumeRoot) ||
            volumeRoot.Length != 3 ||
            volumeRoot[1] != ':' ||
            volumeRoot[2] != Path.DirectorySeparatorChar)
        {
            throw new LockedFileSnapshotException(
                "VSSはドライブ文字を持つローカルボリュームだけで利用できます。");
        }

        if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
        {
            throw new LockedFileSnapshotException(
                "64ビットWindowsでVSSを利用するにはAnyCPUまたはx64版が必要です。");
        }

        if (!WindowsBackupEnvironment.IsProcessElevated())
        {
            throw new LockedFileSnapshotException(
                "VSSスナップショットの作成には管理者権限が必要です。");
        }

        ShadowCopyInfo snapshot;
        lock (_sync)
        {
            if (_completed)
            {
                throw new ObjectDisposedException(nameof(WindowsVssFileBackupSource));
            }

            var normalizedRoot = volumeRoot.ToUpperInvariant();
            if (!_snapshots.TryGetValue(normalizedRoot, out snapshot!))
            {
                snapshot = CreateShadowCopy(normalizedRoot);
                _snapshots.Add(normalizedRoot, snapshot);
                _createdShadowCopyIds.Add(snapshot.Id);
            }
        }

        var relativePath = fullPath.Substring(volumeRoot.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var snapshotPath =
            snapshot.DeviceObject.TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar +
            relativePath;
        try
        {
            return OpenSnapshotFile(
                snapshotPath,
                CopyBufferSize);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            NotSupportedException or System.Security.SecurityException or
            Win32Exception)
        {
            throw new LockedFileSnapshotException(
                "VSSスナップショット内のファイルを開けませんでした。",
                exception);
        }
    }

    public IReadOnlyList<string> Complete()
    {
        lock (_sync)
        {
            if (_completed)
            {
                return _cleanupWarnings.ToArray();
            }

            foreach (var snapshot in _snapshots.Values.Reverse())
            {
                try
                {
                    DeleteShadowCopy(snapshot.Id);
                    DeletedSnapshotCount++;
                }
                catch (Exception exception) when (
                    exception is ManagementException or UnauthorizedAccessException or
                    InvalidOperationException)
                {
                    _cleanupWarnings.Add(
                        $"VSSスナップショットを削除できませんでした: " +
                        $"{snapshot.Id} (0x{exception.HResult:X8})");
                }
            }

            _snapshots.Clear();
            _completed = true;
            return _cleanupWarnings.ToArray();
        }
    }

    public void Dispose()
    {
        Complete();
    }

    private static ShadowCopyInfo CreateShadowCopy(string volumeRoot)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\CIMV2");
            scope.Connect();
            using var shadowClass = new ManagementClass(
                scope,
                new ManagementPath("Win32_ShadowCopy"),
                null);
            using var input = shadowClass.GetMethodParameters("Create");
            input["Volume"] = volumeRoot;
            input["Context"] = "ClientAccessible";
            using var output = shadowClass.InvokeMethod("Create", input, null)
                ?? throw new ManagementException("VSS作成結果を取得できませんでした。");
            var returnValue = Convert.ToUInt32(output["ReturnValue"]);
            if (returnValue != 0)
            {
                throw new ManagementException(
                    $"VSSスナップショットを作成できませんでした: " +
                    $"{DescribeCreateResult(returnValue)} ({returnValue})");
            }

            var shadowId = Convert.ToString(output["ShadowID"]);
            if (string.IsNullOrWhiteSpace(shadowId))
            {
                throw new ManagementException("VSSスナップショットIDがありません。");
            }

            return FindShadowCopy(scope, shadowId);
        }
        catch (Exception exception) when (
            exception is ManagementException or UnauthorizedAccessException or
            InvalidOperationException)
        {
            throw new LockedFileSnapshotException(
                "VSSスナップショットを作成できませんでした。",
                exception);
        }
    }

    private static ShadowCopyInfo FindShadowCopy(
        ManagementScope scope,
        string shadowId)
    {
        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery(
                "SELECT ID, DeviceObject FROM Win32_ShadowCopy WHERE ID = " +
                $"'{EscapeWql(shadowId)}'"));
        using var results = searcher.Get();
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                var deviceObject = Convert.ToString(item["DeviceObject"]);
                if (string.IsNullOrWhiteSpace(deviceObject))
                {
                    break;
                }

                return new ShadowCopyInfo(shadowId, deviceObject);
            }
        }

        throw new ManagementException(
            "作成したVSSスナップショットを照会できませんでした。");
    }

    private static void DeleteShadowCopy(string shadowId)
    {
        var scope = new ManagementScope(@"\\.\root\CIMV2");
        scope.Connect();
        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery(
                "SELECT * FROM Win32_ShadowCopy WHERE ID = " +
                $"'{EscapeWql(shadowId)}'"));
        using var results = searcher.Get();
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                item.Delete();
                return;
            }
        }

        throw new InvalidOperationException(
            "削除対象のVSSスナップショットが見つかりません。");
    }

    private static string EscapeWql(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");

    private static Stream OpenSnapshotFile(string snapshotPath, int bufferSize)
    {
        var handle = CreateFile(
            snapshotPath,
            GenericRead,
            ShareRead | ShareWrite | ShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagSequentialScan,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        try
        {
            return new FileStream(
                handle,
                FileAccess.Read,
                bufferSize,
                isAsync: false);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static string DescribeCreateResult(uint returnValue) =>
        returnValue switch
        {
            1 => "アクセス拒否",
            2 => "引数が不正",
            3 => "ボリュームが見つかりません",
            4 => "ボリュームが未対応",
            5 => "ボリュームがオフライン",
            6 => "空き容量不足",
            7 => "VSSプロバイダー障害",
            8 => "未指定のエラー",
            9 => "別のVSS処理が進行中",
            10 => "VSSプロバイダーが拒否",
            11 => "VSSプロバイダーが未登録",
            12 => "VSSプロバイダー障害",
            _ => "不明なVSSエラー",
        };

    private sealed record ShadowCopyInfo(string Id, string DeviceObject);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateFileW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);
}
