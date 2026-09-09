namespace HonorPCHelper;

internal enum BatteryProtectionMode
{
    Disabled,
    Home,
    Office,
    Travel
}

internal sealed class BatteryProtectionController
{
    private const ulong SetThresholdsCommand = 0x00001003;

    /// <summary>Пороги начала и конца зарядки, которые режим пишет в EC.</summary>
    internal static (int Start, int End) Thresholds(BatteryProtectionMode mode) => mode switch
    {
        BatteryProtectionMode.Home => (40, 70),
        BatteryProtectionMode.Office => (70, 90),
        BatteryProtectionMode.Travel => (95, 100),
        BatteryProtectionMode.Disabled => (0, 100),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    /// <summary>Режим по порогам, прочитанным из EC; null - пара порогов не наша.</summary>
    internal static BatteryProtectionMode? FromThresholds(int? start, int? end) => (start, end) switch
    {
        (40, 70) => BatteryProtectionMode.Home,
        (70, 90) => BatteryProtectionMode.Office,
        (95, 100) => BatteryProtectionMode.Travel,
        (0, 100) => BatteryProtectionMode.Disabled,
        _ => null
    };

    internal void SetMode(BatteryProtectionMode mode)
    {
        var thresholds = Thresholds(mode);

        using var session = new HonorWmiSession();
        if (mode == BatteryProtectionMode.Disabled)
        {
            SetThresholds(session, 0, 0);
            Thread.Sleep(1000);
        }

        SetThresholds(session, thresholds.Start, thresholds.End);
    }

    private static void SetThresholds(HonorWmiSession session, int start, int end)
    {
        var command = SetThresholdsCommand | ((ulong)start << 16) | ((ulong)end << 24);
        session.Call(command);
    }
}
