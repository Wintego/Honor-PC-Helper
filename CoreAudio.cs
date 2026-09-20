using System.Runtime.InteropServices;

namespace HonorPCHelper;

/// <summary>
/// Минимальный интероп Core Audio: конечная точка захвата по умолчанию
/// и её общий признак «выключен».
///
/// Клавиша F7 у Honor гасит не микшер приложения, а сам вход, поэтому здесь
/// используется IAudioEndpointVolume конечной точки, а не сеансовый регулятор:
/// такой mute видят все программы и показывают настройки звука Windows.
///
/// Методы интерфейсов объявлены с PreserveSig и в порядке таблицы вызовов;
/// хвост, который приложению не нужен, опущен - на раскладку это не влияет.
/// </summary>
internal static class CoreAudio
{
    internal const int DataFlowCapture = 1;
    // eConsole - то самое «устройство по умолчанию», которое показывают параметры звука.
    internal const int RoleConsole = 0;
    internal const int ClsCtxInprocServer = 1;

    internal const int ErrorNotFound = unchecked((int)0x80070490);

    private static readonly Guid DeviceEnumeratorClass = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    internal static Guid AudioEndpointVolumeId = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    // Смещение поля bMuted в AUDIO_VOLUME_NOTIFICATION_DATA:
    // GUID guidEventContext (16 байт), затем BOOL bMuted.
    internal const int MutedOffset = 16;

    internal static IMMDeviceEnumerator CreateEnumerator()
    {
        var type = Type.GetTypeFromCLSID(DeviceEnumeratorClass)
            ?? throw new InvalidOperationException("Core Audio device enumerator is unavailable.");
        return (IMMDeviceEnumerator)(Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Could not create the Core Audio device enumerator."));
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice? device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(
            ref Guid interfaceId, int classContext, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object? instance);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string? id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        [PreserveSig] int GetChannelCount(out int count);
        [PreserveSig] int SetMasterVolumeLevel(float level, IntPtr eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(int channel, float level, IntPtr eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(int channel, float level, IntPtr eventContext);
        [PreserveSig] int GetChannelVolumeLevel(int channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(int channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr notificationData);
    }
}
