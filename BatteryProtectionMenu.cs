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
            L.T("Дом (40-70%) - рекомендуется", "Home (40-70%) - recommended", "居家 (40-70%) - 推荐"),
            async () => await ApplyModeAsync(BatteryProtectionMode.Home),
            @checked: currentMode == BatteryProtectionMode.Home,
            tooltip: L.T(
                "Прекращение зарядки при 70 % и возобновление при 40 %.",
                "Stop charging at 70% and resume at 40%.",
                "充至 70% 停止充电，降至 40% 恢复充电。"));
        menu.AddItem(
            L.T("Офис (70-90%)", "Office (70-90%)", "办公 (70-90%)"),
            async () => await ApplyModeAsync(BatteryProtectionMode.Office),
            @checked: currentMode == BatteryProtectionMode.Office,
            tooltip: L.T(
                "Остановка зарядки при 90 % и возобновление при 70 %.",
                "Stop charging at 90% and resume at 70%.",
                "充至 90% 停止充电，降至 70% 恢复充电。"));
        menu.AddItem(
            L.T("Путешествия (95-100%)", "Travel (95-100%)", "出行 (95-100%)"),
            async () => await ApplyModeAsync(BatteryProtectionMode.Travel),
            @checked: currentMode == BatteryProtectionMode.Travel,
            tooltip: L.T(
                "Прекращение зарядки при 100 % и возобновление при 95 %.",
                "Stop charging at 100% and resume at 95%.",
                "充至 100% 停止充电，降至 95% 恢复充电。"));
    }

    private static Task ApplyModeAsync(BatteryProtectionMode mode)
        => HardwareCommand.ApplyAsync(
            () => PrivilegedHardware.TryRunBatteryTaskAsync(mode),
            "--set-battery-mode",
            mode.ToString(),
            L.T("Не удалось запустить настройку батареи.",
                "Could not start battery configuration.",
                "无法启动电池设置。"),
            () => HardwareSettings.BatteryProtection = mode);
}
