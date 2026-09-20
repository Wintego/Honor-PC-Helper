using System.Runtime.InteropServices;

namespace HonorPCHelper;

/// <summary>
/// Клавиша F7: выключение микрофона вместе с индикатором на самой клавише.
///
/// Прошивка по нажатию только шлёт событие OemWMIEvent 0x287 - ни звук,
/// ни лампочку она не трогает. Honor PC Manager делал и то, и другое; без него
/// клавиша мертва, а индикатор остаётся в прежнем положении. Здесь переключается
/// общий mute конечной точки захвата (его видят все программы и параметры звука
/// Windows), а следом лампочка приводится к тому же состоянию.
///
/// Состояние микрофона слушается через IAudioEndpointVolumeCallback, поэтому
/// выключение микрофона мимо клавиши - из микшера или параметров звука -
/// тоже доходит до индикатора.
/// </summary>
internal sealed class MicMuteService : IDisposable
{
    private readonly Lock _gate = new();
    private CoreAudio.IMMDeviceEnumerator? _enumerator;
    private CoreAudio.IAudioEndpointVolume? _volume;
    private MuteCallback? _callback;
    private string? _endpointId;
    private bool _failureLogged;
    private bool _disposed;

    /// <summary>Приводит индикатор к текущему состоянию микрофона.</summary>
    internal void Sync()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            var volume = Resolve();
            if (volume is null || volume.GetMute(out var muted) != 0)
                return;

            MicMuteLedController.TrySet(muted);
        }
    }

    /// <summary>Переключает микрофон и индикатор. Вызывается из обработчика события WMI.</summary>
    internal void Toggle()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            var volume = Resolve();
            if (volume is null)
                return;

            if (volume.GetMute(out var muted) != 0)
            {
                Release();
                return;
            }

            var target = !muted;
            if (volume.SetMute(target, IntPtr.Zero) != 0)
            {
                // Конечная точка могла исчезнуть вместе с устройством - следующий
                // вызов найдёт её заново.
                Release();
                return;
            }

            MicMuteLedController.TrySet(target);
            AppLog.Info($"Microphone {(target ? "muted" : "unmuted")} by Fn key");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Release();
            ReleaseComObject(_enumerator);
            _enumerator = null;
        }
    }

    /// <summary>
    /// Возвращает регулятор конечной точки по умолчанию, переоткрывая его,
    /// когда устройство ввода сменилось: иначе клавиша продолжала бы выключать
    /// прежний микрофон.
    /// </summary>
    private CoreAudio.IAudioEndpointVolume? Resolve()
    {
        try
        {
            _enumerator ??= CoreAudio.CreateEnumerator();

            var status = _enumerator.GetDefaultAudioEndpoint(
                CoreAudio.DataFlowCapture, CoreAudio.RoleConsole, out var device);
            if (status != 0 || device is null)
            {
                // На машине без микрофона это штатный ответ, а не поломка.
                if (status != CoreAudio.ErrorNotFound)
                    LogFailureOnce($"No default capture endpoint (0x{status:X8})");
                Release();
                return null;
            }

            try
            {
                if (device.GetId(out var id) != 0 || id is null)
                {
                    Release();
                    return null;
                }

                if (_volume is not null && id == _endpointId)
                    return _volume;

                Release();

                var interfaceId = CoreAudio.AudioEndpointVolumeId;
                if (device.Activate(ref interfaceId, CoreAudio.ClsCtxInprocServer, IntPtr.Zero, out var instance) != 0
                    || instance is not CoreAudio.IAudioEndpointVolume volume)
                {
                    LogFailureOnce("Could not activate the capture endpoint volume");
                    return null;
                }

                _volume = volume;
                _endpointId = id;
                _callback = new MuteCallback(this);
                if (volume.RegisterControlChangeNotify(_callback) != 0)
                    _callback = null;

                _failureLogged = false;
                return volume;
            }
            finally
            {
                ReleaseComObject(device);
            }
        }
        catch (Exception exception)
        {
            LogFailureOnce($"Microphone mute is unavailable: {exception.Message}");
            Release();
            ReleaseComObject(_enumerator);
            _enumerator = null;
            return null;
        }
    }

    // Вызывается только под _gate.
    private void Release()
    {
        if (_volume is not null)
        {
            if (_callback is not null)
            {
                try
                {
                    _volume.UnregisterControlChangeNotify(_callback);
                }
                catch (COMException)
                {
                }
            }

            ReleaseComObject(_volume);
        }

        _volume = null;
        _callback = null;
        _endpointId = null;
    }

    private void LogFailureOnce(string message)
    {
        if (_failureLogged)
            return;

        _failureLogged = true;
        AppLog.Error(message);
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }

    /// <summary>
    /// Уведомление приходит из звукового движка, и задерживать его нельзя,
    /// поэтому обращение к BIOS уходит в пул потоков.
    /// </summary>
    private sealed class MuteCallback(MicMuteService owner) : CoreAudio.IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr notificationData)
        {
            if (notificationData == IntPtr.Zero || owner._disposed)
                return 0;

            var muted = Marshal.ReadInt32(notificationData, CoreAudio.MutedOffset) != 0;
            _ = Task.Run(() => MicMuteLedController.TrySet(muted));
            return 0;
        }
    }
}
