using System.Management;

namespace HonorPCHelper;

internal static class DiagnosticsService
{
    // NotifyIcon.Text не может быть длиннее 127 символов.
    private const int MaxTooltipLength = 127;

    private const int PowerCacheMilliseconds = 2000;

    /// <summary>Снимок мощности вместе со временем измерения: читается и пишется целиком.</summary>
    private sealed record PowerSample(double? Watts, long Ticks);

    private static PowerSample? _power;

    // Подсказка пересобирается при каждом наведении на иконку, поэтому строки
    // складываются в буфер, а локализуются только литералы: интерполяция
    // обоих языковых вариантов сразу удваивала бы аллокации.
    [ThreadStatic] private static System.Text.StringBuilder? _buffer;

    internal static string BuildCompactToolTip()
    {
        var state = HardwareSettings.ReadTooltipState();
        var hasHardwareState = HardwareSensorSnapshot.TryParse(
            state.SensorSnapshot, out var hardwareState) && hardwareState.IsFresh;
        var mode = state.HunterModeActive
            ? "HUNTER"
            : state.PerformanceModeActive
                ? L.T("производительный", "performance", "高性能")
                : L.T("умный", "smart", "智能");
        var backlightLevel = hasHardwareState
            ? hardwareState.KeyboardBacklightMode switch
            {
                0x02 => KeyboardBacklightLevel.Off,
                0x03 => KeyboardBacklightLevel.Low,
                0x04 => KeyboardBacklightLevel.High,
                _ => state.KeyboardBacklight
            }
            : state.KeyboardBacklight;
        var backlight = backlightLevel switch
        {
            KeyboardBacklightLevel.Off => L.T("выкл.", "off", "关"),
            KeyboardBacklightLevel.Low => L.T("слабая", "weak", "弱"),
            KeyboardBacklightLevel.High => L.T("сильная", "strong", "强"),
            _ => "?"
        };
        var protection = hasHardwareState && hardwareState.ChargeStart.HasValue && hardwareState.ChargeEnd.HasValue
            ? $"{hardwareState.ChargeStart}–{hardwareState.ChargeEnd}%"
            : state.BatteryProtection switch
            {
                BatteryProtectionMode.Home => "40–70%",
                BatteryProtectionMode.Office => "70–90%",
                BatteryProtectionMode.Travel => "95–100%",
                BatteryProtectionMode.Disabled => L.T("выкл.", "off", "关"),
                _ => "?"
            };
        var text = _buffer ??= new System.Text.StringBuilder(MaxTooltipLength + 32);
        text.Clear();
        // Строки разделяются одним \n: подсказка ограничена 127 символами,
        // и пары \r\n отняли бы у неё ещё пять.
        text.Append(L.T("Режим: ", "Mode: ", "模式：")).Append(mode);
        text.Append('\n').Append(L.T("Подсветка: ", "Backlight: ", "背光：")).Append(backlight);
        text.Append('\n').Append(L.T("Заряд: ", "Charge limit: ", "充电限制：")).Append(protection);

        var power = ReadBatteryPowerWatts();
        if (power.HasValue)
        {
            text.Append('\n').Append(L.T("Питание: ", "Power: ", "功率："));
            AppendPower(text, power.Value);
        }

        if (hasHardwareState)
        {
            if (hardwareState.CpuTemperature.HasValue || hardwareState.BatteryTemperature.HasValue)
            {
                text.Append('\n').Append("CPU: ");
                AppendTemperature(text, hardwareState.CpuTemperature);
                text.Append(L.T("; батарея: ", "; battery: ", "；电池："));
                AppendTemperature(text, hardwareState.BatteryTemperature);
            }
            if (hardwareState.Fan1Rpm.HasValue || hardwareState.Fan2Rpm.HasValue)
                AppendFans(text, hardwareState);
        }

        if (text.Length > MaxTooltipLength)
            text.Length = MaxTooltipLength;
        return text.ToString();
    }

    private static void AppendTemperature(System.Text.StringBuilder text, int? value)
    {
        if (value.HasValue)
            text.Append(value.Value).Append("°C");
        else
            text.Append('?');
    }

    /// <summary>
    /// Обороты вентиляторов вместе с целевыми, к которым их ведёт прошивка.
    /// Если строка с целью не влезает в подсказку, остаются одни фактические:
    /// обрезка посреди числа показала бы неверные обороты.
    /// </summary>
    private static void AppendFans(System.Text.StringBuilder text, HardwareSensorSnapshot state)
    {
        var start = text.Length;
        text.Append('\n').Append(L.T("Вент.: ", "Fans: ", "风扇："));
        var valuesStart = text.Length;
        // MagicBook Pro 14 цель не сообщает: байты под неё нулевые и при вращении.
        var target1 = state.Fan1TargetRpm is > 0 ? state.Fan1TargetRpm : null;
        var target2 = state.Fan2TargetRpm is > 0 ? state.Fan2TargetRpm : null;
        if (target1.HasValue || target2.HasValue)
        {
            AppendFan(text, state.Fan1Rpm, target1);
            text.Append(" / ");
            AppendFan(text, state.Fan2Rpm, target2);
            if (text.Length <= MaxTooltipLength)
                return;
            text.Length = valuesStart;
        }

        AppendFan(text, state.Fan1Rpm, null);
        text.Append('/');
        AppendFan(text, state.Fan2Rpm, null);
        text.Append(L.T(" об/мин", " RPM", " 转/分"));
        if (text.Length > MaxTooltipLength)
            text.Length = start;
    }

    private static void AppendFan(System.Text.StringBuilder text, int? value, int? target)
    {
        if (value.HasValue)
            text.Append(value.Value);
        else
            text.Append('?');
        if (target.HasValue)
            text.Append('→').Append(target.Value);
    }

    private static void AppendPower(System.Text.StringBuilder text, double watts)
    {
        if (Math.Abs(watts) < 0.05)
        {
            text.Append(L.T("0 Вт", "0 W", "0 瓦"));
            return;
        }

        if (watts > 0)
            text.Append('+');
        text.Append(watts.ToString("0.0")).Append(L.T(" Вт", " W", " 瓦"));
    }

    // Charge/discharge power in watts: positive while charging, negative while
    // discharging. Read from the standard root\wmi BatteryStatus class, which is
    // available without administrator rights, and cached briefly so repeated
    // tooltip refreshes during a hover don't re-query WMI each time.
    private static double? ReadBatteryPowerWatts()
    {
        // Кэшируется и отсутствие значения: иначе на машине без батареи WMI
        // опрашивался бы при каждой перерисовке подсказки.
        var cached = Volatile.Read(ref _power);
        if (cached is not null && Environment.TickCount64 - cached.Ticks < PowerCacheMilliseconds)
            return cached.Watts;

        double? result = null;
        try
        {
            var scope = new ManagementScope(@"\\.\root\wmi");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(
                scope, new ObjectQuery("SELECT ChargeRate, DischargeRate FROM BatteryStatus"));
            using var items = searcher.Get();
            foreach (ManagementBaseObject item in items)
            {
                using (item)
                {
                    var charge = Convert.ToInt64(item["ChargeRate"] ?? 0L);
                    var discharge = Convert.ToInt64(item["DischargeRate"] ?? 0L);
                    result = (charge - discharge) / 1000.0;
                }
                break;
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read battery power", exception);
        }

        Volatile.Write(ref _power, new PowerSample(result, Environment.TickCount64));
        return result;
    }
}
