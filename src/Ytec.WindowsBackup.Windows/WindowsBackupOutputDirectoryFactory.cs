using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Ytec.WindowsBackup.Core.Services;

namespace Ytec.WindowsBackup.Windows;

public sealed class WindowsBackupOutputDirectoryFactory : IBackupOutputDirectoryFactory
{
    private const string ProtectedDirectorySddl =
        "O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)";
    private const uint FileReadAttributes = 0x00000080;
    private const uint ReadControl = 0x00020000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint DirectoryAttribute = 0x00000010;
    private const uint ReparsePointAttribute = 0x00000400;
    private const uint SecurityDescriptorRevision = 1;
    internal const string RootLockFileName = ".ytec-root-lock";

    public BackupOutputDirectoryLease Create(
        string destinationRoot,
        string requestedJobName,
        bool protectDuringBackup)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return new DefaultBackupOutputDirectoryFactory().Create(
                destinationRoot,
                requestedJobName,
                protectDuringBackup);
        }

        var normalizedDestination = BackupPathPolicy.NormalizeExistingDirectory(
            destinationRoot,
            nameof(destinationRoot));
        var outputDirectory = BackupFolderNamePolicy.GetOutputDirectory(
            normalizedDestination,
            requestedJobName);
        BackupFolderNamePolicy.EnsureDoesNotExist(outputDirectory);

        CreateDirectoryAtomically(outputDirectory, protectDuringBackup);
        SafeFileHandle? directoryHandle = null;
        FileStream? rootLock = null;
        try
        {
            directoryHandle = OpenDirectory(outputDirectory, out var originalIdentity);
            rootLock = new FileStream(
                Path.Combine(outputDirectory, RootLockFileName),
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.DeleteOnClose | FileOptions.WriteThrough);

            using (var verificationHandle = OpenDirectory(
                outputDirectory,
                out var verifiedIdentity))
            {
                if (!IsSameFile(originalIdentity, verifiedIdentity))
                {
                    throw new InvalidOperationException(
                        "新規バックアップフォルダーが作成直後に置き換えられました。");
                }
            }

            var capturedHandle = directoryHandle;
            var capturedRootLock = rootLock;
            directoryHandle = null;
            rootLock = null;
            return new BackupOutputDirectoryLease(
                outputDirectory,
                requiresFinalizationOnCancellation: protectDuringBackup,
                release: () =>
                {
                    capturedRootLock.Dispose();
                    capturedHandle.Dispose();
                });
        }
        catch
        {
            rootLock?.Dispose();
            directoryHandle?.Dispose();
            TryDeleteEmptyDirectory(outputDirectory);
            throw;
        }
    }

    private static SafeFileHandle OpenDirectory(
        string outputDirectory,
        out ByHandleFileInformation information)
    {
        var handle = NativeMethods.CreateFile(
            outputDirectory,
            FileReadAttributes | ReadControl,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        if (!NativeMethods.GetFileInformationByHandle(handle, out information))
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        if ((information.FileAttributes & DirectoryAttribute) == 0 ||
            (information.FileAttributes & ReparsePointAttribute) != 0)
        {
            handle.Dispose();
            throw new InvalidOperationException(
                "新規バックアップフォルダーを安全に固定できませんでした。");
        }

        return handle;
    }

    private static bool IsSameFile(
        ByHandleFileInformation left,
        ByHandleFileInformation right) =>
        left.VolumeSerialNumber == right.VolumeSerialNumber &&
        left.FileIndexHigh == right.FileIndexHigh &&
        left.FileIndexLow == right.FileIndexLow;

    private static void CreateDirectoryAtomically(
        string outputDirectory,
        bool protectDuringBackup)
    {
        IntPtr securityDescriptor = IntPtr.Zero;
        try
        {
            if (!protectDuringBackup)
            {
                if (!NativeMethods.CreateDirectory(outputDirectory, IntPtr.Zero))
                {
                    throw CreateDirectoryException();
                }

                return;
            }

            if (!NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptor(
                ProtectedDirectorySddl,
                SecurityDescriptorRevision,
                out securityDescriptor,
                out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var securityAttributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = securityDescriptor,
                InheritHandle = false,
            };
            if (!NativeMethods.CreateDirectory(outputDirectory, ref securityAttributes))
            {
                throw CreateDirectoryException();
            }
        }
        finally
        {
            if (securityDescriptor != IntPtr.Zero)
            {
                NativeMethods.LocalFree(securityDescriptor);
            }
        }
    }

    private static Exception CreateDirectoryException()
    {
        var error = Marshal.GetLastWin32Error();
        return new IOException(
            "指定した新規バックアップフォルダーを作成できませんでした。" +
            "同じ名前がないか確認してください。",
            new Win32Exception(error));
    }

    private static void TryDeleteEmptyDirectory(string outputDirectory)
    {
        try
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: false);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // 元の例外を維持する。空フォルダーは管理者で削除できる。
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;

        [MarshalAs(UnmanagedType.Bool)]
        public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    private static class NativeMethods
    {
        [DllImport(
            "kernel32.dll",
            EntryPoint = "CreateDirectoryW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateDirectory(
            string pathName,
            IntPtr securityAttributes);

        [DllImport(
            "kernel32.dll",
            EntryPoint = "CreateDirectoryW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateDirectory(
            string pathName,
            ref SecurityAttributes securityAttributes);

        [DllImport(
            "kernel32.dll",
            EntryPoint = "CreateFileW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandle(
            SafeFileHandle file,
            out ByHandleFileInformation fileInformation);

        [DllImport(
            "advapi32.dll",
            EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
            string stringSecurityDescriptor,
            uint stringSdRevision,
            out IntPtr securityDescriptor,
            out uint securityDescriptorSize);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr LocalFree(IntPtr memory);
    }
}
