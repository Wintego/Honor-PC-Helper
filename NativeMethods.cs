using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HonorPCHelper;

internal static partial class NativeMethods
{
    internal const uint MfString = 0x00000000;
    internal const uint MfSeparator = 0x00000800;
    internal const uint MfPopup = 0x00000010;
    internal const uint MfEnabled = 0x00000000;
    internal const uint MfGrayed = 0x00000001;
    internal const uint MfDisabled = 0x00000002;
    internal const uint MfChecked = 0x00000008;
    internal const uint TpmReturnCmd = 0x0100;
    internal const uint TpmLeftAlign = 0x0000;
    internal const uint TpmTopAlign = 0x0000;
    internal const uint TpmRightButton = 0x0002;
    internal const uint TtsAlwaysTip = 0x01;
    internal const uint TtsNoPrefix = 0x02;
    internal const uint TtfTrack = 0x0020;
    internal const uint TtfAbsolute = 0x0080;
    internal const uint TtfTransparent = 0x0100;
    internal const uint TtfIdIsHwnd = 0x0001;
    internal const uint TtmAddToolW = 0x0400 + 50;
    internal const uint TtmDelToolW = 0x0400 + 51;
    internal const uint TtmSetMaxTipWidth = 0x0400 + 24;
    internal const uint TtmTrackActivate = 0x0400 + 17;
    internal const uint TtmTrackPosition = 0x0400 + 18;
    internal const uint TtmUpdateTipTextW = 0x0400 + 57;
    internal const uint WsPopup = 0x80000000;
    internal const uint WsExTopmost = 0x00000008;
    internal const int CwUseDefault = unchecked((int)0x80000000);
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoActivate = 0x0010;
    internal static readonly IntPtr HwndTopmost = new(-1);

    internal const uint DigcfPresent = 0x00000002;
    internal const uint DigcfDeviceInterface = 0x00000010;
    internal const uint GenericRead = 0x80000000;
    internal const uint GenericWrite = 0x40000000;
    internal const uint FileShareRead = 0x00000001;
    internal const uint FileShareWrite = 0x00000002;
    internal const uint OpenExisting = 3;
    internal const uint FileFlagOverlapped = 0x40000000;
    internal const int HidpStatusSuccess = 0x00110000;

    internal const int WmPowerBroadcast = 0x0218;
    internal const int PbtPowerSettingChange = 0x8013;
    internal const uint DeviceNotifyWindowHandle = 0x00000000;
    internal static readonly Guid GuidConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    internal const int WmDeviceChange = 0x0219;
    internal const int DbtDeviceArrival = 0x8000;
    internal const int DbtDeviceRemoveComplete = 0x8004;
    internal const int DbtDevTypDeviceInterface = 5;
    // DEV_BROADCAST_DEVICEINTERFACE_W: dbcc_size(4) + dbcc_devicetype(4) + dbcc_reserved(4) + dbcc_classguid(16).
    internal const int DevBroadcastNameOffset = 28;


    [StructLayout(LayoutKind.Sequential)]
    internal struct PowerBroadcastSetting
    {
        internal Guid PowerSetting;
        internal uint DataLength;
        internal byte Data;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial IntPtr RegisterPowerSettingNotification(IntPtr recipient, in Guid powerSettingGuid, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    internal struct DevBroadcastDeviceInterface
    {
        internal int Size;
        internal int DeviceType;
        internal int Reserved;
        internal Guid ClassGuid;
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterDeviceNotificationW", SetLastError = true)]
    internal static partial IntPtr RegisterDeviceNotification(IntPtr recipient, IntPtr notificationFilter, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterDeviceNotification(IntPtr handle);

    // Подписывает окно на WM_DEVICECHANGE по HID-классу устройств.
    internal static IntPtr RegisterHidDeviceNotification(IntPtr windowHandle)
    {
        HidD_GetHidGuid(out var hidGuid);
        var filter = new DevBroadcastDeviceInterface
        {
            Size = Marshal.SizeOf<DevBroadcastDeviceInterface>(),
            DeviceType = DbtDevTypDeviceInterface,
            ClassGuid = hidGuid
        };
        var buffer = Marshal.AllocHGlobal(filter.Size);
        try
        {
            Marshal.StructureToPtr(filter, buffer, false);
            return RegisterDeviceNotification(windowHandle, buffer, DeviceNotifyWindowHandle);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterPowerSettingNotification(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    internal struct ToolInfo
    {
        internal uint Size;
        internal uint Flags;
        internal IntPtr Window;
        internal IntPtr Id;
        internal Rect Rect;
        internal IntPtr Instance;
        internal IntPtr Text;
        internal IntPtr Param;
        internal IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { internal int Left; internal int Top; internal int Right; internal int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpDeviceInterfaceData
    {
        internal uint Size;
        internal Guid InterfaceClassGuid;
        internal uint Flags;
        internal IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HiddAttributes
    {
        internal int Size;
        internal ushort VendorId;
        internal ushort ProductId;
        internal ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HidpCaps
    {
        internal ushort Usage;
        internal ushort UsagePage;
        internal ushort InputReportByteLength;
        internal ushort OutputReportByteLength;
        internal ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] internal ushort[] Reserved;
        internal ushort NumberLinkCollectionNodes;
        internal ushort NumberInputButtonCaps;
        internal ushort NumberInputValueCaps;
        internal ushort NumberInputDataIndices;
        internal ushort NumberOutputButtonCaps;
        internal ushort NumberOutputValueCaps;
        internal ushort NumberOutputDataIndices;
        internal ushort NumberFeatureButtonCaps;
        internal ushort NumberFeatureValueCaps;
        internal ushort NumberFeatureDataIndices;
    }

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(IntPtr icon);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr CreatePopupMenu();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyMenu(IntPtr menu);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AppendMenuW(IntPtr menu, uint flags, IntPtr idNewItem, string? newItem);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    internal static partial int TrackPopupMenuEx(
        IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr tpmParams);

    [LibraryImport("user32.dll")]
    internal static partial int GetMenuItemCount(IntPtr menu);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static partial IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, ref ToolInfo lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static partial IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool HidD_GetAttributes(SafeFileHandle device, ref HiddAttributes attributes);

    [LibraryImport("hid.dll")]
    internal static partial void HidD_GetHidGuid(out Guid guid);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr data);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool HidD_FreePreparsedData(IntPtr data);

    [DllImport("hid.dll")]
    internal static extern int HidP_GetCaps(IntPtr data, out HidpCaps caps);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    internal static partial IntPtr SetupDiGetClassDevsW(
        ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
        uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiGetDeviceInterfaceDetailSize(
        IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr detailData, uint detailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr detailData, uint detailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [LibraryImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WriteFile(
        SafeFileHandle file, [In] byte[] buffer, uint bytesToWrite,
        out uint bytesWritten, IntPtr overlapped);

    internal static string GetDevicePath(IntPtr set, ref SpDeviceInterfaceData data)
    {
        SetupDiGetDeviceInterfaceDetailSize(set, ref data, IntPtr.Zero, 0, out var size, IntPtr.Zero);
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, size, out _, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return Marshal.PtrToStringUni(buffer + 4)!;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
