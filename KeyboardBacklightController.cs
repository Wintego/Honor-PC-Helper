namespace HonorPCHelper;

internal enum KeyboardBacklightLevel
{
    Off,
    Low,
    High
}

internal sealed class KeyboardBacklightController
{
    private const ulong SetModeCommand = 0x00001406;
    private const ulong SetAutoLevelCommand = 0x00001506;
    private const ulong SetTimeoutCommand = 0x00001106;
    private const byte ModeOff = 0x02;
    private const byte ModeLow = 0x03;
    private const byte ModeHigh = 0x04;
    private const byte ModeAuto = 0x10;

    internal void SetLevel(KeyboardBacklightLevel level)
    {
        using var session = new HonorWmiSession();
        SetLevel(session, level);
    }

    /// <summary>Уровень и таймаут за одно подключение к BIOS.</summary>
    internal void SetState(KeyboardBacklightLevel level, ushort timeoutSeconds)
    {
        using var session = new HonorWmiSession();
        SetLevel(session, level);
        session.Call(SetTimeoutCommand | ((ulong)timeoutSeconds << 16));
    }

    private static void SetLevel(HonorWmiSession session, KeyboardBacklightLevel level)
    {
        var mode = level switch
        {
            KeyboardBacklightLevel.Off => ModeOff,
            KeyboardBacklightLevel.Low => ModeLow,
            KeyboardBacklightLevel.High => ModeHigh,
            _ => throw new ArgumentOutOfRangeException(nameof(level))
        };

        try
        {
            session.Call(SetModeCommand | ((ulong)mode << 16));
        }
        catch (HonorWmiCommandException exception) when (exception.ErrorCode == 1)
        {
            var brightness = level switch
            {
                KeyboardBacklightLevel.Off => 0,
                KeyboardBacklightLevel.Low => 50,
                KeyboardBacklightLevel.High => 100,
                _ => 0
            };
            session.Call(SetModeCommand | ((ulong)ModeAuto << 16));
            Thread.Sleep(10);
            session.Call(SetAutoLevelCommand | ((ulong)brightness << 16));
        }
    }

    internal void SetTimeout(ushort seconds)
    {
        HonorWmi.Call(SetTimeoutCommand | ((ulong)seconds << 16));
    }
}
