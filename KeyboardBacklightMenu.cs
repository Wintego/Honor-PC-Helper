using System.ComponentModel;
using System.Diagnostics;

namespace HonorPCHelper;

/// <summary>
/// Подменю трея «Клавиатура»: уровень подсветки, таймаут автоотключения
/// и ночное расписание. Три группы разделены заголовками-разделителями.
/// </summary>
internal static class KeyboardBacklightMenu
{
    private static readonly (KeyboardBacklightLevel Level, string Ru, string En, string Zh)[] Levels =
    [
        (KeyboardBacklightLevel.Off, "Выключена", "Off", "关闭"),
        (KeyboardBacklightLevel.Low, "Слабая", "Weak", "弱"),
        (KeyboardBacklightLevel.High, "Сильная", "Strong", "强")
    ];

    private static readonly (ushort Seconds, string Ru, string En, string Zh)[] Timeouts =
    [
        (0, "Не выключать", "Never", "不关闭"),
        (15, "15 секунд", "15 seconds", "15 秒"),
        (30, "30 секунд", "30 seconds", "30 秒"),
        (60, "1 минута", "1 minute", "1 分钟"),
        (300, "5 минут", "5 minutes", "5 分钟")
    ];

    internal static void Build(NativePopupMenu menu, BacklightScheduleService scheduleService)
    {
        AddHeader(menu, "Подсветка", "Backlight", "背光");
        var level = HardwareSettings.KeyboardBacklight;
        foreach (var (value, ru, en, zh) in Levels)
            menu.AddItem(
                L.T(ru, en, zh),
                async () => await ApplyLevelAsync(value, scheduleService),
                @checked: level == value);

        menu.AddSeparator();

        AddHeader(menu, "Таймаут", "Timeout", "超时");
        var timeout = HardwareSettings.KeyboardBacklightTimeout;
        foreach (var (seconds, ru, en, zh) in Timeouts)
            menu.AddItem(
                L.T(ru, en, zh),
                async () => await ApplyTimeoutAsync(seconds),
                @checked: timeout == seconds);

        menu.AddSeparator();

        AddHeader(menu, "Расписание", "Schedule", "计划");
        BuildScheduleItems(menu, scheduleService);
    }

    private static void AddHeader(NativePopupMenu menu, string russian, string english, string chinese)
        => menu.AddItem(L.T(russian, english, chinese), null, enabled: false);

    private static void BuildScheduleItems(NativePopupMenu menu, BacklightScheduleService scheduleService)
    {
        menu.AddItem(
            L.T("Включено", "Enabled", "启用"),
            async () =>
            {
                HardwareSettings.BacklightScheduleEnabled = !HardwareSettings.BacklightScheduleEnabled;
                await scheduleService.SettingsChangedAsync();
            },
            @checked: HardwareSettings.BacklightScheduleEnabled);

        BuildHourSubMenu(
            menu.AddSubMenu(L.T("Включать в", "Turn on at", "开启时间")),
            HardwareSettings.BacklightOnHour,
            hour => HardwareSettings.BacklightOnHour = hour,
            scheduleService.SettingsChangedAsync);
        BuildHourSubMenu(
            menu.AddSubMenu(L.T("Выключать в", "Turn off at", "关闭时间")),
            HardwareSettings.BacklightOffHour,
            hour => HardwareSettings.BacklightOffHour = hour,
            scheduleService.SettingsChangedAsync);

        var levelSub = menu.AddSubMenu(L.T("Уровень по расписанию", "Scheduled level", "计划亮度"));
        var scheduleLevel = HardwareSettings.BacklightScheduleLevel;
        foreach (var (value, ru, en, zh) in Levels)
        {
            if (value == KeyboardBacklightLevel.Off)
                continue;

            levelSub.AddItem(
                L.T(ru, en, zh),
                async () =>
                {
                    HardwareSettings.BacklightScheduleLevel = value;
                    await scheduleService.SettingsChangedAsync();
                },
                @checked: scheduleLevel == value);
        }
    }

    private static void BuildHourSubMenu(
        NativePopupMenu menu, int current, Action<int> save, Func<Task> settingsChanged)
    {
        for (var hour = 0; hour < 24; hour++)
        {
            var value = hour;
            menu.AddItem(
                $"{hour:00}:00",
                async () =>
                {
                    save(value);
                    await settingsChanged();
                },
                @checked: hour == current);
        }
    }

    private static Task ApplyLevelAsync(
        KeyboardBacklightLevel level, BacklightScheduleService scheduleService)
        => ApplyAsync(
            () => PrivilegedHardware.TryRunBacklightTaskAsync(level),
            "--set-keyboard-backlight",
            level.ToString(),
            L.T("Не удалось изменить подсветку клавиатуры.",
                "Could not change the keyboard backlight.",
                "无法更改键盘背光。"),
            () =>
            {
                HardwareSettings.KeyboardBacklight = level;
                scheduleService.SetManualOverride();
            });

    private static Task ApplyTimeoutAsync(ushort seconds)
        => ApplyAsync(
            () => PrivilegedHardware.TryRunBacklightTimeoutTaskAsync(seconds),
            "--set-keyboard-backlight-timeout",
            seconds.ToString(),
            L.T("Не удалось изменить таймаут подсветки клавиатуры.",
                "Could not change the keyboard backlight timeout.",
                "无法更改键盘背光超时时间。"),
            () => HardwareSettings.KeyboardBacklightTimeout = seconds);

    /// <summary>
    /// Применяет настройку через фоновую задачу с правами администратора,
    /// а при неудаче - перезапуском процесса с запросом UAC.
    /// </summary>
    private static async Task ApplyAsync(
        Func<Task<bool>> tryPrivilegedTask,
        string argument,
        string value,
        string errorMessage,
        Action onApplied)
    {
        try
        {
            if (await tryPrivilegedTask() || await RunElevatedAsync(argument, value, errorMessage))
                onApplied();
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            // Пользователь отменил запрос UAC - молча выходим.
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Honor PC Helper", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static async Task<bool> RunElevatedAsync(string argument, string value, string errorMessage)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException(L.T(
                "Не удалось определить путь к HonorPCHelper.exe.",
                "Could not determine the path to HonorPCHelper.exe.",
                "无法确定 HonorPCHelper.exe 的路径。"));
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Verb = "runas"
        };
        startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add(value);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(errorMessage);
        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }
}
