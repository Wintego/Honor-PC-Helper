namespace HonorPCHelper;

internal static class BatteryProtectionMenu
{
    internal static void Build(NativePopupMenu menu)
    {
        var currentMode = HardwareSettings.BatteryProtection ?? BatteryProtectionMode.Home;

        menu.AddItem(
            L.T("Отключено", "Disabled", "关闭"),
            async () => await ApplyModeAsync(BatteryProtectionMode.Disabled),
            @checked: currentMode == BatteryProtectionMode.Disabled,
            tooltip: L.T(
                "Разрешить обычную зарядку до 100 %.",
                "Allow normal charging to 100%.",
                "允许正常充电至 100%。"));
        menu.AddSeparator();
        menu.AddItem(
            L.T("Дом (до 70%)", "Home (up to 70%)", "居家 (上限 70%)"),
            async () => await ApplyModeAsync(BatteryProtectionMode.Home),
            @checked: currentMode == BatteryProtectionMode.Home,
            tooltip: L.T(
                "Зарядка останавливается на 70 %.",
                "Charging stops at 70%.",
                "充至 70% 停止充电。"));
        menu.AddItem(
            L.T("Офис (до 90%)", "Office (up to 90%)", "办公 (上限 90%)"),
            async () => await ApplyModeAsync(BatteryProtectionMode.Office),
            @checked: currentMode == BatteryProtectionMode.Office,
            tooltip: L.T(
                "Зарядка останавливается на 90 %.",
                "Charging stops at 90%.",
                "充至 90% 停止充电。"));
        menu.AddItem(
            L.T("Путешествия (до 100%)", "Travel (up to 100%)", "出行 (上限 100%)"),
            async () => await ApplyModeAsync(BatteryProtectionMode.Travel),
            @checked: currentMode == BatteryProtectionMode.Travel,
            tooltip: L.T(
                "Зарядка останавливается на 100 %.",
                "Charging stops at 100%.",
                "充至 100% 停止充电。"));
    }

    private static Task ApplyModeAsync(BatteryProtectionMode mode)
        => HardwareCommand.ApplyAsync(
            () => PrivilegedHardware.TryRunBatteryTaskAsync(mode),
            "--set-battery-mode",
            mode.ToString(),
            L.T("Не удалось запустить настройку батареи.",
                "Could not start battery configuration.",
                "无法启动电池设置。"),
            () =>
            {
                HardwareSettings.BatteryProtection = mode;
                // Выбор человека нужен и после пробуждения: опрос датчиков
                // перепишет BatteryProtection тем, что осталось в EC.
                HardwareSettings.PreferredBatteryProtection = mode;
            });
}
