using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HonorPCHelper;

internal sealed record HidDeviceInfo(
    string Path, ushort VendorId, ushort ProductId, ushort UsagePage, ushort Usage,
    ushort InputReportLength, ushort OutputReportLength = 0)
{
    internal SafeFileHandle OpenForReading() => NativeMethods.CreateFile(
        Path, NativeMethods.GenericRead,
        NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
        IntPtr.Zero, NativeMethods.OpenExisting, NativeMethods.FileFlagOverlapped, IntPtr.Zero);
}

internal static class HidDevice
{
    internal static IReadOnlyList<HidDeviceInfo> Enumerate()
    {
        NativeMethods.HidD_GetHidGuid(out var hidGuid);
        var set = NativeMethods.SetupDiGetClassDevsW(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            NativeMethods.DigcfPresent | NativeMethods.DigcfDeviceInterface);
        if (set == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var devices = new List<HidDeviceInfo>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new NativeMethods.SpDeviceInterfaceData
                {
                    Size = (uint)Marshal.SizeOf<NativeMethods.SpDeviceInterfaceData>()
                };
                if (!NativeMethods.SetupDiEnumDeviceInterfaces(
                        set, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                    break;

                var path = NativeMethods.GetDevicePath(set, ref interfaceData);
                using var handle = NativeMethods.CreateFile(path, 0,
                    NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
                    IntPtr.Zero, NativeMethods.OpenExisting, 0, IntPtr.Zero);
                if (handle.IsInvalid)
                    continue;

                var attributes = new NativeMethods.HiddAttributes
                {
                    Size = Marshal.SizeOf<NativeMethods.HiddAttributes>()
                };
                if (!NativeMethods.HidD_GetAttributes(handle, ref attributes))
                    continue;

                ushort usagePage = 0, usage = 0, inputLength = 64, outputLength = 0;
                if (NativeMethods.HidD_GetPreparsedData(handle, out var preparsed))
                {
                    try
                    {
                        if (NativeMethods.HidP_GetCaps(preparsed, out var caps) == NativeMethods.HidpStatusSuccess)
                        {
                            usagePage = caps.UsagePage;
                            usage = caps.Usage;
                            inputLength = caps.InputReportByteLength;
                            outputLength = caps.OutputReportByteLength;
                        }
                    }
                    finally
                    {
                        NativeMethods.HidD_FreePreparsedData(preparsed);
                    }
                }

                devices.Add(new HidDeviceInfo(path, attributes.VendorId, attributes.ProductId,
                    usagePage, usage, inputLength, outputLength));
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(set);
        }
        return devices;
    }
}
