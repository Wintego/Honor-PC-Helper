namespace HonorPCHelper;

internal static class PerformanceModePolicy
{
    internal static bool CanEnable(out string reason)
    {
        var status = SystemInformation.PowerStatus;
        if (status.PowerLineStatus != PowerLineStatus.Online)
        {
            reason = L.T(
                "Производительный режим доступен только при подключённом источнике питания.",
                "Performance mode is available only when AC power is connected.",
                "仅在连接电源适配器时可使用高性能模式。");
            return false;
        }

        if (status.BatteryLifePercent >= 0 && status.BatteryLifePercent < 0.20f)
        {
            reason = L.T(
                "Для производительного режима заряд батареи должен быть не менее 20%.",
                "Performance mode requires at least 20% battery charge.",
                "高性能模式要求电池电量不低于 20%。");
            return false;
        }

        if (status.BatteryLifePercent < 0)
            AppLog.Info("Battery charge is unknown; performance mode allowed because AC power is connected");

        reason = string.Empty;
        return true;
    }
}
