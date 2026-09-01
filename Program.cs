using System.Diagnostics;

namespace HonorPCHelper;

internal static class Program
{
    /// <summary>
    /// Применяет одну аппаратную настройку. Возвращает false, если значение
    /// аргумента не разобрано; при отказе оборудования бросает исключение.
    /// </summary>
    private delegate bool HardwareAction(string? value);

    /// <summary>
    /// Без аргументов приложение работает как значок в трее. С аргументами это
    /// служебный запуск одной командой:
    ///
    /// - --set-*   запуск с повышением прав через UAC: применяет настройку,
    ///             регистрирует фоновую задачу и показывает ошибку пользователю;
    /// - --apply-* тот же набор настроек из фоновой задачи, молча;
    /// - --run-pending-hardware-command  точка входа самой фоновой задачи;
    /// - --restart-after &lt;pid&gt;  запуск после самообновления: ждём выхода прежней сборки.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
            return RunTray();

        var value = args.Length > 1 ? args[1] : null;
        return args[0] switch
        {
            "--set-battery-mode" => Interactive(AlsoRegister(SetBatteryMode), value),
            "--set-power-unlock" => Interactive(AlsoRegister(SetPowerUnlock), value),
            "--set-keyboard-backlight" => Interactive(AlsoRegister(SetKeyboardBacklight), value),
            "--set-keyboard-backlight-timeout" => Interactive(AlsoRegister(SetKeyboardBacklightTimeout), value),
            "--read-sensors" => Interactive(AlsoRegister(ReadSensors), value),

            "--apply-battery-mode" => Silent(SetBatteryMode, value),
            "--apply-power-unlock" => Silent(SetPowerUnlock, value),
            "--apply-keyboard-backlight" => Silent(SetKeyboardBacklight, value),
            "--apply-keyboard-backlight-timeout" => Silent(SetKeyboardBacklightTimeout, value),

            // Приходит и со значением (через фоновую задачу), и без него,
            // когда задача ещё не установлена и приложение поднимается через UAC.
            // Пользователь этот запуск не инициировал, поэтому окно с ошибкой не показываем.
            "--grant-brightness-access" => Logged(AlsoRegister(GrantBrightnessAccess), value,
                "Could not grant Honor ACPI brightness access"),

            "--install-privileged-tasks" => Interactive(InstallPrivilegedTasks, value),
            "--uninstall-privileged-tasks" => Interactive(UninstallPrivilegedTasks, value),
            "--run-pending-hardware-command" => PrivilegedHardware.RunPendingCommand(),
            "--restart-after" => RunTrayAfter(value),

            _ => RunTray()
        };
    }

    /// <summary>
    /// Запуск обновлённой сборки: прежний процесс ещё держит mutex
    /// единственного экземпляра, поэтому сначала дожидаемся его выхода.
    /// </summary>
    private static int RunTrayAfter(string? processIdText)
    {
        if (int.TryParse(processIdText, out var processId))
        {
            try
            {
                using var previous = Process.GetProcessById(processId);
                previous.WaitForExit(TimeSpan.FromSeconds(30));
            }
            catch (ArgumentException)
            {
                // Прежний процесс уже завершился.
            }
        }

        return RunTray();
    }

    private static int RunTray()
    {
        using var mutex = new Mutex(false, "HonorPCHelper.SingleInstance", out var createdNew);
        if (!createdNew)
            return 0;

        Task.Run(ApplicationUpdateService.RemoveUpdateLeftovers);
        ApplicationConfiguration.Initialize();
        Application.Run(new HelperApplicationContext());
        return 0;
    }

    /// <summary>
    /// Пользовательский запуск: об ошибке нужно сообщить, иначе UAC-окно
    /// просто мигнёт и человек не поймёт, почему настройка не применилась.
    /// </summary>
    private static int Interactive(HardwareAction action, string? value)
    {
        try
        {
            return action(value) ? 0 : 2;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Honor PC Helper", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    /// <summary>Запуск из фоновой задачи: интерфейса нет, результат читается по коду возврата.</summary>
    private static int Silent(HardwareAction action, string? value)
    {
        try
        {
            return action(value) ? 0 : 2;
        }
        catch
        {
            return 1;
        }
    }

    private static int Logged(HardwareAction action, string? value, string message)
    {
        try
        {
            return action(value) ? 0 : 2;
        }
        catch (Exception exception)
        {
            AppLog.Error(message, exception);
            return 1;
        }
    }

    /// <summary>
    /// Добавляет к действию регистрацию фоновой задачи: раз уж процесс
    /// поднят с правами администратора, следующий раз можно обойтись без UAC.
    /// </summary>
    private static HardwareAction AlsoRegister(HardwareAction action) => value =>
    {
        if (!action(value))
            return false;

        PrivilegedHardware.EnsureRegistered();
        return true;
    };

    private static bool SetBatteryMode(string? value)
    {
        if (!Enum.TryParse<BatteryProtectionMode>(value, true, out var mode))
            return false;

        new BatteryProtectionController().SetMode(mode);
        return true;
    }

    private static bool SetPowerUnlock(string? value)
    {
        if (!bool.TryParse(value, out var enabled))
            return false;

        new PowerUnlockController().SetEnabled(enabled);
        return true;
    }

    private static bool SetKeyboardBacklight(string? value)
    {
        if (!Enum.TryParse<KeyboardBacklightLevel>(value, true, out var level))
            return false;

        new KeyboardBacklightController().SetLevel(level);
        return true;
    }

    private static bool SetKeyboardBacklightTimeout(string? value)
    {
        if (!ushort.TryParse(value, out var seconds))
            return false;

        new KeyboardBacklightController().SetTimeout(seconds);
        return true;
    }

    private static bool ReadSensors(string? requestId)
    {
        if (string.IsNullOrEmpty(requestId))
            return false;

        HardwareSensorController.ReadAndStore(requestId);
        return true;
    }

    // Выдаёт текущему пользователю право вызывать ACPI-WMI блок яркости.
    private static bool GrantBrightnessAccess(string? _)
    {
        HonorAcpiBrightness.GrantAccess();
        return true;
    }

    private static bool InstallPrivilegedTasks(string? _)
    {
        PrivilegedHardware.EnsureRegistered();
        return true;
    }

    private static bool UninstallPrivilegedTasks(string? _)
    {
        PrivilegedHardware.RemoveRegistered();
        return true;
    }
}
