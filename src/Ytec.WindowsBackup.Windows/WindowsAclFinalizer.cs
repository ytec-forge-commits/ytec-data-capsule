using System.ComponentModel;
using System.Runtime.InteropServices;
using Ytec.WindowsBackup.Core.Models;
using Ytec.WindowsBackup.Core.Services;

namespace Ytec.WindowsBackup.Windows;

public sealed class WindowsAclFinalizer : IBackupFinalizer
{
    private const uint FileAllAccess = 0x001F01FF;
    private const uint OwnerSecurityInformation = 0x00000001;
    private const uint DaclSecurityInformation = 0x00000004;
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;
    private const int ErrorNotAllAssigned = 1300;
    private const int SetAccess = 2;
    private const int TrusteeIsSid = 0;
    private const int TrusteeIsWellKnownGroup = 5;
    private const uint SubContainersAndObjectsInherit = 0x00000003;
    private const uint SeFileObject = 1;
    private const int MaximumRecordedErrors = 50;

    public Task<BackupFinalizationResult> FinalizeAsync(
        string destinationRoot,
        string outputDirectory,
        IProgress<BackupFinalizationProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => FinalizeCore(
                destinationRoot,
                outputDirectory,
                progress,
                cancellationToken),
            cancellationToken);
    }

    private static BackupFinalizationResult FinalizeCore(
        string destinationRoot,
        string outputDirectory,
        IProgress<BackupFinalizationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return new BackupFinalizationResult(
                true,
                false,
                0,
                1,
                ["Windows以外ではACLを設定できません。"]);
        }

        var normalizedDestination =
            BackupPathPolicy.NormalizeExistingDirectory(destinationRoot, nameof(destinationRoot));
        var normalizedOutput =
            BackupPathPolicy.NormalizeExistingDirectory(outputDirectory, nameof(outputDirectory));
        if ((File.GetAttributes(normalizedOutput) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "ACL対象のジョブフォルダーに再解析ポイントは使用できません。");
        }

        BackupPathPolicy.EnsurePathUnderRoot(
            normalizedDestination,
            normalizedOutput,
            "ACL対象がバックアップ先の外側を指しています。");

        var outputParent = Directory.GetParent(normalizedOutput)?.FullName;
        if (outputParent is null ||
            !string.Equals(
                Path.GetFullPath(outputParent),
                Path.GetFullPath(normalizedDestination),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "ACL対象はバックアップ先直下の新規ジョブフォルダーに限定されています。");
        }

        var errors = new List<string>();
        var processed = 0;
        var failed = 0;

        IntPtr everyoneSid = IntPtr.Zero;
        IntPtr everyoneAcl = IntPtr.Zero;
        try
        {
            everyoneSid = CreateEveryoneSid();
            everyoneAcl = CreateEveryoneFullControlAcl(everyoneSid);

            using var takeOwnership =
                PrivilegeScope.TryEnable("SeTakeOwnershipPrivilege", errors);
            using var restore = PrivilegeScope.TryEnable("SeRestorePrivilege", errors);
            failed += errors.Count;

            foreach (var path in EnumerateTargetPaths(normalizedOutput, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = path == normalizedOutput
                    ? "."
                    : BackupPathPolicy.ToManifestPath(
                        BackupPathPolicy.GetRelativePath(normalizedOutput, path));
                try
                {
                    var result = NativeMethods.SetNamedSecurityInfo(
                        path,
                        SeFileObject,
                        OwnerSecurityInformation | DaclSecurityInformation,
                        everyoneSid,
                        IntPtr.Zero,
                        everyoneAcl,
                        IntPtr.Zero);
                    if (result != 0)
                    {
                        throw new Win32Exception((int)result);
                    }

                    processed++;
                }
                catch (Exception exception) when (
                    exception is Win32Exception or UnauthorizedAccessException or IOException)
                {
                    failed++;
                    if (errors.Count < MaximumRecordedErrors)
                    {
                        errors.Add($"{relative}: {FormatErrorCode(exception)}");
                    }
                }

                progress?.Report(new BackupFinalizationProgress(processed, failed, relative));
            }
        }
        catch (Exception exception) when (
            exception is Win32Exception or UnauthorizedAccessException or IOException)
        {
            failed++;
            if (errors.Count < MaximumRecordedErrors)
            {
                errors.Add($"ACL初期化: {FormatErrorCode(exception)}");
            }
        }
        finally
        {
            if (everyoneAcl != IntPtr.Zero)
            {
                NativeMethods.LocalFree(everyoneAcl);
            }

            if (everyoneSid != IntPtr.Zero)
            {
                NativeMethods.LocalFree(everyoneSid);
            }
        }

        return new BackupFinalizationResult(
            true,
            failed == 0 && errors.Count == 0,
            processed,
            failed,
            errors);
    }

    private static string FormatErrorCode(Exception exception) =>
        exception is Win32Exception win32Exception
            ? $"Win32 {win32Exception.NativeErrorCode} (0x{win32Exception.NativeErrorCode:X8})"
            : $"0x{exception.HResult:X8}";

    private static IEnumerable<string> EnumerateTargetPaths(
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<PendingPath>();
        pending.Push(new PendingPath(outputDirectory, expanded: false));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pendingPath = pending.Pop();
            var current = pendingPath.Path;
            var expanded = pendingPath.Expanded;
            var currentAttributes = File.GetAttributes(current);
            if ((currentAttributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            if ((currentAttributes & FileAttributes.Directory) == 0 || expanded)
            {
                yield return current;
                continue;
            }

            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(current);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException)
            {
                throw new IOException("ACL対象を列挙できません。", exception);
            }

            // 親フォルダー、特に保護中のジョブルートは子要素の後に処理し、
            // Everyone がルートへ到達できる時間を最終段階まで作らない。
            pending.Push(new PendingPath(current, expanded: true));
            foreach (var entry in entries.OrderByDescending(
                path => path,
                StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.Equals(
                    Path.GetFileName(entry),
                    WindowsBackupOutputDirectoryFactory.RootLockFileName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                pending.Push(new PendingPath(entry, expanded: false));
            }
        }
    }

    private sealed class PendingPath
    {
        public PendingPath(string path, bool expanded)
        {
            Path = path;
            Expanded = expanded;
        }

        public string Path { get; }

        public bool Expanded { get; }
    }

    private static IntPtr CreateEveryoneSid()
    {
        if (!NativeMethods.ConvertStringSidToSid("S-1-1-0", out var sid))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return sid;
    }

    private static IntPtr CreateEveryoneFullControlAcl(IntPtr everyoneSid)
    {
        var access = new ExplicitAccess
        {
            AccessPermissions = FileAllAccess,
            AccessMode = SetAccess,
            Inheritance = SubContainersAndObjectsInherit,
            Trustee = new Trustee
            {
                MultipleTrustee = IntPtr.Zero,
                MultipleTrusteeOperation = 0,
                TrusteeForm = TrusteeIsSid,
                TrusteeType = TrusteeIsWellKnownGroup,
                Name = everyoneSid,
            },
        };

        var result = NativeMethods.SetEntriesInAcl(
            1,
            ref access,
            IntPtr.Zero,
            out var acl);
        if (result != 0)
        {
            throw new Win32Exception((int)result);
        }

        return acl;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes
    {
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public LuidAndAttributes Privileges;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Trustee
    {
        public IntPtr MultipleTrustee;
        public int MultipleTrusteeOperation;
        public int TrusteeForm;
        public int TrusteeType;
        public IntPtr Name;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ExplicitAccess
    {
        public uint AccessPermissions;
        public int AccessMode;
        public uint Inheritance;
        public Trustee Trustee;
    }

    private sealed class PrivilegeScope : IDisposable
    {
        private readonly IntPtr _token;
        private readonly TokenPrivileges _previousState;
        private bool _disposed;

        private PrivilegeScope(IntPtr token, TokenPrivileges previousState)
        {
            _token = token;
            _previousState = previousState;
        }

        public static PrivilegeScope? TryEnable(string privilegeName, List<string> errors)
        {
            if (!NativeMethods.OpenProcessToken(
                NativeMethods.GetCurrentProcess(),
                TokenAdjustPrivileges | TokenQuery,
                out var token))
            {
                errors.Add($"{privilegeName}: 0x{Marshal.GetHRForLastWin32Error():X8}");
                return null;
            }

            if (!NativeMethods.LookupPrivilegeValue(null, privilegeName, out var luid))
            {
                errors.Add($"{privilegeName}: 0x{Marshal.GetHRForLastWin32Error():X8}");
                NativeMethods.CloseHandle(token);
                return null;
            }

            var requested = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Privileges = new LuidAndAttributes
                {
                    Luid = luid,
                    Attributes = SePrivilegeEnabled,
                },
            };
            var bufferLength = (uint)Marshal.SizeOf<TokenPrivileges>();
            if (!NativeMethods.AdjustTokenPrivileges(
                token,
                false,
                ref requested,
                bufferLength,
                out var previous,
                out _))
            {
                errors.Add($"{privilegeName}: 0x{Marshal.GetHRForLastWin32Error():X8}");
                NativeMethods.CloseHandle(token);
                return null;
            }

            var lastError = Marshal.GetLastWin32Error();
            if (lastError == ErrorNotAllAssigned)
            {
                errors.Add($"{privilegeName}: 権限が割り当てられていません。");
                NativeMethods.CloseHandle(token);
                return null;
            }

            return new PrivilegeScope(token, previous);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            var previous = _previousState;
            NativeMethods.AdjustTokenPrivileges(
                _token,
                false,
                ref previous,
                0,
                out _,
                out _);
            NativeMethods.CloseHandle(_token);
            _disposed = true;
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        internal static extern IntPtr LocalFree(IntPtr memory);

        [DllImport(
            "advapi32.dll",
            EntryPoint = "ConvertStringSidToSidW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ConvertStringSidToSid(
            string stringSid,
            out IntPtr sid);

        [DllImport("advapi32.dll", EntryPoint = "SetEntriesInAclW")]
        internal static extern uint SetEntriesInAcl(
            uint countOfExplicitEntries,
            ref ExplicitAccess explicitEntries,
            IntPtr oldAcl,
            out IntPtr newAcl);

        [DllImport(
            "advapi32.dll",
            EntryPoint = "SetNamedSecurityInfoW",
            CharSet = CharSet.Unicode)]
        internal static extern uint SetNamedSecurityInfo(
            string objectName,
            uint objectType,
            uint securityInfo,
            IntPtr owner,
            IntPtr group,
            IntPtr dacl,
            IntPtr sacl);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(
            IntPtr processHandle,
            uint desiredAccess,
            out IntPtr tokenHandle);

        [DllImport(
            "advapi32.dll",
            EntryPoint = "LookupPrivilegeValueW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool LookupPrivilegeValue(
            string? systemName,
            string name,
            out Luid luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AdjustTokenPrivileges(
            IntPtr tokenHandle,
            [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
            ref TokenPrivileges newState,
            uint bufferLength,
            out TokenPrivileges previousState,
            out uint returnLength);
    }
}
