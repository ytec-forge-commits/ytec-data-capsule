using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Ytec.WindowsBackup.Core.Models;

namespace Ytec.WindowsBackup.Windows;

public static class WindowsCopyTuning
{
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const int StorageDeviceSeekPenaltyProperty = 7;
    private const int PropertyStandardQuery = 0;

    public static BackupCopyOptions Resolve(
        string sourcePath,
        string destinationPath)
    {
        var sourceSeekPenalty = TryGetSeekPenalty(sourcePath);
        var destinationSeekPenalty = TryGetSeekPenalty(destinationPath);
        if (sourceSeekPenalty == false && destinationSeekPenalty == false)
        {
            return new BackupCopyOptions(
                Clamp(Environment.ProcessorCount * 2, 4, 12),
                Clamp(Environment.ProcessorCount / 2, 2, 4),
                "SSD高速");
        }

        if (sourceSeekPenalty == true || destinationSeekPenalty == true)
        {
            return new BackupCopyOptions(2, 1, "HDD安定");
        }

        return new BackupCopyOptions(4, 2, "自動・標準");
    }

    public static bool? TryGetSeekPenalty(string path)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return null;
        }

        string? root;
        try
        {
            root = Path.GetPathRoot(Path.GetFullPath(path));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(root) ||
            root.Length < 2 ||
            root[1] != ':')
        {
            return null;
        }

        var volumePath = $@"\\.\{char.ToUpperInvariant(root[0])}:";
        using var handle = NativeMethods.CreateFile(
            volumePath,
            desiredAccess: 0,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            flagsAndAttributes: 0,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return null;
        }

        var query = new StoragePropertyQuery
        {
            PropertyId = StorageDeviceSeekPenaltyProperty,
            QueryType = PropertyStandardQuery,
            AdditionalParameters = 0,
        };
        if (!NativeMethods.DeviceIoControl(
            handle,
            IoctlStorageQueryProperty,
            ref query,
            (uint)Marshal.SizeOf<StoragePropertyQuery>(),
            out DeviceSeekPenaltyDescriptor descriptor,
            (uint)Marshal.SizeOf<DeviceSeekPenaltyDescriptor>(),
            out _,
            IntPtr.Zero))
        {
            return null;
        }

        return descriptor.IncursSeekPenalty;
    }

    private static int Clamp(int value, int minimum, int maximum) =>
        Math.Max(minimum, Math.Min(maximum, value));

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery
    {
        public int PropertyId;
        public int QueryType;
        public byte AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceSeekPenaltyDescriptor
    {
        public uint Version;
        public uint Size;

        [MarshalAs(UnmanagedType.U1)]
        public bool IncursSeekPenalty;
    }

    private static class NativeMethods
    {
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
        internal static extern bool DeviceIoControl(
            SafeFileHandle device,
            uint controlCode,
            ref StoragePropertyQuery inputBuffer,
            uint inputBufferSize,
            out DeviceSeekPenaltyDescriptor outputBuffer,
            uint outputBufferSize,
            out uint bytesReturned,
            IntPtr overlapped);
    }
}
