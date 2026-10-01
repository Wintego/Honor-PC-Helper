namespace HonorPCHelper;

internal static class PowerUnlockMenu
{
    internal static void Build(NativePopupMenu menu, Action modeChanged)
    {
        if (HardwareSettings.HunterModeActive)
        {
            // Как включить HUNTER и выйти из него, известно только PC Manager:
            // команда умного режима поверх HUNTER не проверена.
            menu.AddItem(
                L.T("Производительный режим: HUNTER", "Performance mode: HUNTER", "高性能模式：HUNTER"),
                null,
                enabled: false,
                tooltip: L.T(
                    "Включён режим HUNTER. Его переключает HONOR PC Manager.",
                    "HUNTER mode is on. It is switched in HONOR PC Manager.",
                    "已开启 HUNTER 模式，请在 HONOR PC Manager 中切换。"));
            return;
        }

        menu.AddItem(
            L.T("Производительный режим", "Performance mode", "高性能模式"),
            async () => await ApplyModeAsync(modeChanged),
            @checked: HardwareSettings.PerformanceModeActive,
            tooltip: L.T(
                "Галочка: производительный режим. Без галочки: умный режим. Переключение также доступно через Fn+P.",
                "Checked: performance mode. Unchecked: smart mode. Fn+P also switches modes.",
                "勾选：高性能模式。取消勾选：智能模式。也可用 Fn+P 切换。"));
    }

    private static Task ApplyModeAsync(Action modeChanged)
    {
        // Меню могло пролежать открытым, пока PC Manager включал HUNTER.
        if (HardwareSettings.HunterModeActive)
            return Task.CompletedTask;

        var target = !HardwareSettings.PerformanceModeActive;
        if (target && !PerformanceModePolicy.CanEnable(out var reason))
        {
            MessageBox.Show(reason, "Honor PC Helper", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return Task.CompletedTask;
        }

        return HardwareCommand.ApplyAsync(
            () => PrivilegedHardware.TryRunPowerUnlockTaskAsync(target),
            "--set-power-unlock",
            target.ToString(),
            L.T("Не удалось изменить режим производительности.",
                "Could not change performance mode.",
                "无法切换性能模式。"),
            () =>
            {
                HardwareSettings.PowerUnlock = target;
                HardwareSettings.PerformanceModeActive = target;
                modeChanged();
            });
    }
}
