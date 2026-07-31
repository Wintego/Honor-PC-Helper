using System.Management;

namespace HonorPCHelper;

internal static class DiagnosticsService
{
    // NotifyIcon.Text не может быть длиннее 127 символов.
    private const int MaxTooltipLength = 127;

    private static long _powerCacheTicks;
    private static bool _powerCacheValid;
    private static double? _powerCache;

    // Подсказка пересобирается при каждом наведении на иконку, поэтому строки
    // складываются в буфер, а локализуются только литералы: интерполяция
    // обоих языковых вариантов сразу удваивала бы аллокации.
    [ThreadStatic] private static System.Text.StringBuilder? _buffer;

    internal static string BuildCompactToolTip()
    {
        var state = HardwareSettings.ReadTooltipState();
        var hasHardwareState = HardwareSensorSnapshot.TryParse(
            state.SensorSnapshot, out var hardwareState) && hardwareState.IsFresh;
        var mode = state.PerformanceModeActive
            ? L.T("производительный", "performance")
            : L.T("умный", "smart");
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
            KeyboardBacklightLevel.Off => L.T("выкл.", "off"),
            KeyboardBacklightLevel.Low => L.T("слабая", "weak"),
            KeyboardBacklightLevel.High => L.T("сильная", "strong"),
            _ => "?"
        };
        var protection = hasHardwareState && hardwareState.ChargeStart.HasValue && hardwareState.ChargeEnd.HasValue
            ? $"{hardwareState.ChargeStart}–{hardwareState.ChargeEnd}%"
            : state.BatteryProtection switch
            {
                BatteryProtectionMode.Home => "40–70%",
                BatteryProtectionMode.Office => "70–90%",
                BatteryProtectionMode.Travel => "95–100%",
                BatteryProtectionMode.Disabled => L.T("выкл.", "off"),
                _ => "?"
            };
        var text = _buffer ??= new System.Text.StringBuilder(MaxTooltipLength + 32);
        text.Clear();
        text.Append(L.T("Режим: ", "Mode: ")).Append(mode);
        text.AppendLine().Append(L.T("Подсветка: ", "Backlight: ")).Append(backlight);
        text.AppendLine().Append(L.T("Ограничение заряда: ", "Charge limit: ")).Append(protection);

        var power = ReadBatteryPowerWatts();
        if (power.HasValue)
        {
            text.AppendLine().Append(L.T("Питание: ", "Power: "));
            AppendPower(text, power.Value);
        }

        if (hasHardwareState)
        {
            if (hardwareState.CpuTemperature.HasValue || hardwareState.BatteryTemperature.HasValue)
            {
                text.AppendLine().Append("CPU: ");
                AppendTemperature(text, hardwareState.CpuTemperature);
                text.Append(L.T("; батарея: ", "; battery: "));
                AppendTemperature(text, hardwareState.BatteryTemperature);
            }
            if (hardwareState.Fan1Rpm.HasValue || hardwareState.Fan2Rpm.HasValue)
            {
                text.AppendLine().Append(L.T("Вентиляторы: ", "Fans: "));
                AppendFan(text, hardwareState.Fan1Rpm);
                text.Append('/');
                AppendFan(text, hardwareState.Fan2Rpm);
                text.Append(L.T(" об/мин", " RPM"));
            }
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

    private static void AppendFan(System.Text.StringBuilder text, int? value)
    {
        if (value.HasValue)
            text.Append(value.Value);
        else
            text.Append('?');
    }

    private static void AppendPower(System.Text.StringBuilder text, double watts)
    {
        if (Math.Abs(watts) < 0.05)
        {
            text.Append(L.T("0 Вт", "0 W"));
            return;
        }

        if (watts > 0)
            text.Append('+');
        text.Append(watts.ToString("0.0")).Append(L.T(" Вт", " W"));
    }

    // Charge/discharge power in watts: positive while charging, negative while
    // discharging. Read from the standard root\wmi BatteryStatus class, which is
    // available without administrator rights, and cached briefly so repeated
    // tooltip refreshes during a hover don't re-query WMI each time.
    private static double? ReadBatteryPowerWatts()
    {
        // Кэшируется и отсутствие значения: иначе на машине без батареи WMI
        // опрашивался бы при каждой перерисовке подсказки.
        if (_powerCacheValid
            && Environment.TickCount64 - Interlocked.Read(ref _powerCacheTicks) < 2000)
            return _powerCache;

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

        _powerCache = result;
        _powerCacheValid = true;
        Interlocked.Exchange(ref _powerCacheTicks, Environment.TickCount64);
        return result;
    }
}
