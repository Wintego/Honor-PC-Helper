using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace HonorPCHelper;

/// <summary>
/// Можно ли ради команды показать запрос UAC, если фоновая задача ещё
/// не зарегистрирована или указывает на прежний путь приложения.
/// </summary>
internal enum Elevation
{
    /// <summary>Команда идёт только через задачу: её не инициировал человек.</summary>
    Never,

    /// <summary>
    /// Запрос допустим, пока человек не отклонил его в этом сеансе: иначе
    /// фоновые действия - опрос датчиков при наведении на значок - показывали
    /// бы окно UAC снова и снова.
    /// </summary>
    UnlessDeclined,

    /// <summary>Действие пользователя: спросить можно всегда.</summary>
    Interactive
}

/// <summary>
/// Привилегированный экземпляр выполнил команду, но оборудование её отклонило.
/// Сообщение - то, что записал привилегированный экземпляр.
/// </summary>
internal sealed class PrivilegedCommandException(string message) : InvalidOperationException(message);

internal static class PrivilegedHardware
{
    private const string TaskName = "Honor PC Helper Privileged Hardware";
    private const char CommandSeparator = '\u001F';
    private const string BacklightStateArgument = "--apply-keyboard-backlight-state";
    private const int PickupTimeoutMilliseconds = 10_000;
    // Задача ограничена минутой; самая долгая команда - отключение
    // ограничения заряда с секундной паузой - укладывается с запасом.
    private const int ResultTimeoutMilliseconds = 30_000;
    private static readonly object RunLock = new();

    // Человек закрыл запрос UAC. Фоновые действия больше не спрашивают
    // до конца сеанса; действия из меню спрашивают по-прежнему.
    private static volatile bool _elevationDeclined;

    // Проверка задачи создаёт COM-объект планировщика и стоит десятки
    // миллисекунд, а команда уходит на каждое изменение настройки и на каждый
    // опрос датчиков. Результат кэшируется и сбрасывается, как только запуск
    // задачи не удался, - тогда следующая попытка проверит регистрацию заново.
    private static volatile bool _tasksVerified;

    internal static void EnsureRegistered()
    {
        dynamic? service = null;
        dynamic? folder = null;
        dynamic? definition = null;
        dynamic? action = null;
        try
        {
            service = CreateService();
            folder = service.GetFolder("\\");
            definition = service.NewTask(0);
            definition.RegistrationInfo.Description = "Honor PC Helper privileged hardware control";
            definition.Principal.UserId = WindowsIdentity.GetCurrent().Name;
            definition.Principal.LogonType = 3;
            definition.Principal.RunLevel = 1;
            definition.Settings.Enabled = true;
            definition.Settings.AllowDemandStart = true;
            definition.Settings.DisallowStartIfOnBatteries = false;
            definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.ExecutionTimeLimit = "PT1M";

            action = definition.Actions.Create(0);
            action.Path = GetExecutablePath();
            action.Arguments = "--run-pending-hardware-command";
            folder.RegisterTaskDefinition(TaskName, definition, 6, null, null, 3, null);
            _tasksVerified = true;
        }
        finally
        {
            Release(action);
            Release(definition);
            Release(folder);
            Release(service);
        }
    }

    internal static bool AreTasksAvailable()
    {
        if (_tasksVerified)
            return true;

        dynamic? service = null;
        dynamic? folder = null;
        dynamic? task = null;
        dynamic? definition = null;
        dynamic? action = null;
        try
        {
            service = CreateService();
            folder = service.GetFolder("\\");
            task = folder.GetTask(TaskName);
            definition = task.Definition;
            action = definition.Actions.Item(1);
            var executablePath = Convert.ToString(action.Path);
            _tasksVerified = !string.IsNullOrWhiteSpace(executablePath)
                && File.Exists(executablePath)
                && string.Equals(Path.GetFullPath(executablePath), Path.GetFullPath(GetExecutablePath()), StringComparison.OrdinalIgnoreCase)
                && string.Equals(Convert.ToString(action.Arguments)?.Trim(), "--run-pending-hardware-command", StringComparison.Ordinal);
            return _tasksVerified;
        }
        catch
        {
            return false;
        }
        finally
        {
            Release(action);
            Release(definition);
            Release(task);
            Release(folder);
            Release(service);
        }
    }

    internal static void RemoveRegistered()
    {
        dynamic? service = null;
        dynamic? folder = null;
        try
        {
            service = CreateService();
            folder = service.GetFolder("\\");
            _tasksVerified = false;
            DeleteTask(folder, TaskName);
            DeleteTask(folder, "Honor PC Helper Hardware Command");
            DeleteTask(folder, "Honor PC Helper Hardware Settings");
        }
        finally
        {
            Release(folder);
            Release(service);
        }
    }

    internal static bool TryRunGrantBrightnessAccessTask()
        => TryRunTask(Elevation.UnlessDeclined, "--grant-brightness-access", "1");

    internal static bool TryRunCameraTask(bool off)
        => TryRunTask(Elevation.Interactive, "--apply-camera-off", off.ToString());

    internal static bool TryRunGrantCameraAccessTask(Elevation elevation)
        => TryRunTask(elevation, "--grant-camera-access", "1");

    internal static Task<bool> TryRunBacklightTaskAsync(
        KeyboardBacklightLevel level, Elevation elevation = Elevation.Interactive)
        => Task.Run(() => TryRunTask(elevation, "--apply-keyboard-backlight", level.ToString()));

    internal static Task<bool> TryRunBacklightTimeoutTaskAsync(ushort seconds)
        => Task.Run(() => TryRunTask(Elevation.Interactive, "--apply-keyboard-backlight-timeout", seconds.ToString()));

    internal static Task<bool> TryRunBatteryTaskAsync(BatteryProtectionMode mode)
        => Task.Run(() => TryRunTask(Elevation.Interactive, "--apply-battery-mode", mode.ToString()));

    internal static Task<bool> TryRunPowerUnlockTaskAsync(bool enabled, Elevation elevation = Elevation.Interactive)
        => Task.Run(() => TryRunTask(elevation, "--apply-power-unlock", enabled.ToString()));

    internal static Task<bool> TryReadSensorsTaskAsync()
        => Task.Run(() => TryReadSensorsTask(Elevation.UnlessDeclined));

    /// <summary>
    /// Восстановление настроек после пробуждения человек не инициировал,
    /// поэтому окно UAC там недопустимо: без установленной фоновой задачи
    /// команда просто не выполняется.
    /// </summary>
    internal static Task<bool> TryRunBatteryTaskSilentlyAsync(BatteryProtectionMode mode)
        => Task.Run(() => TryRunTask(Elevation.Never, "--apply-battery-mode", mode.ToString()));

    /// <summary>
    /// Уровень и таймаут подсветки одной командой: после пробуждения их
    /// переприменяют несколько раз подряд, и каждая отдельная команда стоила бы
    /// ещё одного запуска привилегированного экземпляра.
    /// </summary>
    internal static Task<bool> TryRunBacklightStateTaskSilentlyAsync(KeyboardBacklightLevel level, ushort seconds)
        => Task.Run(() => TryRunTask(Elevation.Never, BacklightStateArgument, $"{level},{seconds}"));

    internal static Task<bool> TryReadSensorsSilentlyAsync()
        => Task.Run(() => TryReadSensorsTask(Elevation.Never));

    /// <summary>Запоминает отказ от UAC, чтобы фоновые действия больше не спрашивали.</summary>
    internal static void NoteElevationDeclined() => _elevationDeclined = true;

    private static bool TryReadSensorsTask(Elevation elevation)
    {
        var requestId = Guid.NewGuid().ToString("N");
        if (!TryRunTask(elevation, "--read-sensors", requestId))
            return false;

        return WaitFor(
            () => HardwareSettings.SensorSnapshot?.StartsWith(requestId + '|', StringComparison.Ordinal) == true,
            5000);
    }

    /// <summary>
    /// Ждёт результата привилегированного экземпляра. Пауза начинается с пяти
    /// миллисекунд и растёт до пятидесяти: обычный ответ приходит почти сразу,
    /// и фиксированный шаг в 50 мс заметно задерживал бы применение настройки.
    /// </summary>
    private static bool WaitFor(Func<bool> completed, int timeoutMilliseconds)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        var delay = 5;
        while (true)
        {
            if (completed())
                return true;
            if (Environment.TickCount64 >= deadline)
                return false;
            Thread.Sleep(delay);
            if (delay < 50)
                delay *= 2;
        }
    }

    /// <summary>
    /// Точка входа фоновой задачи. Команда лежит в реестре вместе с номером
    /// запроса, и по этому же номеру вызывающий находит итог: иначе отказ BIOS
    /// выглядел бы для меню как успех - оно видело бы только, что команду забрали.
    /// </summary>
    internal static int RunPendingCommand()
    {
        var command = HardwareSettings.PendingHardwareCommand;
        if (string.IsNullOrEmpty(command))
        {
            // Задача успела стартовать уже после того, как вызывающий сдался и снял команду.
            AppLog.Error("Privileged instance found no pending command");
            return 2;
        }

        HardwareSettings.PendingHardwareCommand = null;
        var parts = command.Split(CommandSeparator);
        if (parts.Length != 3)
        {
            AppLog.Error($"Malformed pending hardware command: {command}");
            return 2;
        }

        var (requestId, argument, value) = (parts[0], parts[1], parts[2]);
        try
        {
            var exitCode = Execute(argument, value) ? 0 : 2;
            HardwareSettings.HardwareCommandResult = string.Join(CommandSeparator, requestId, exitCode);
            return exitCode;
        }
        catch (Exception exception)
        {
            AppLog.Error($"Privileged hardware command failed: {argument} {value}", exception);
            HardwareSettings.HardwareCommandResult = string.Join(CommandSeparator, requestId, 1, exception.Message);
            return 1;
        }
    }

    /// <summary>Выполняет команду; false - команда или её значение не разобраны.</summary>
    private static bool Execute(string argument, string value)
    {
        switch (argument)
        {
            case "--apply-keyboard-backlight" when Enum.TryParse<KeyboardBacklightLevel>(value, true, out var level):
                new KeyboardBacklightController().SetLevel(level);
                return true;
            case "--apply-keyboard-backlight-timeout" when ushort.TryParse(value, out var seconds):
                new KeyboardBacklightController().SetTimeout(seconds);
                return true;
            case BacklightStateArgument when TryParseBacklightState(value, out var stateLevel, out var stateSeconds):
                new KeyboardBacklightController().SetState(stateLevel, stateSeconds);
                return true;
            case "--apply-battery-mode" when Enum.TryParse<BatteryProtectionMode>(value, true, out var mode):
                new BatteryProtectionController().SetMode(mode);
                return true;
            case "--apply-power-unlock" when bool.TryParse(value, out var enabled):
                new PowerUnlockController().SetEnabled(enabled);
                return true;
            case "--apply-camera-off" when bool.TryParse(value, out var cameraOff):
                CameraMuteController.SetCameraOff(cameraOff);
                return true;
            case "--grant-brightness-access":
                HonorAcpiDirect.GrantAccess();
                return true;
            case "--grant-camera-access":
                CameraMuteController.GrantAccess();
                return true;
            case "--read-sensors":
                HardwareSensorController.ReadAndStore(value);
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseBacklightState(string value, out KeyboardBacklightLevel level, out ushort seconds)
    {
        level = default;
        seconds = 0;
        var separator = value.IndexOf(',');
        return separator > 0
            && Enum.TryParse(value.AsSpan(0, separator), true, out level)
            && ushort.TryParse(value.AsSpan(separator + 1), out seconds);
    }

    /// <summary>
    /// Отдаёт команду фоновой задаче и ждёт её итога. true - команда выполнена;
    /// false - задача недоступна или не ответила. Если привилегированный
    /// экземпляр команду выполнил, а оборудование её отклонило, бросает
    /// <see cref="PrivilegedCommandException"/>: повторять её через UAC незачем.
    /// </summary>
    private static bool TryRunTask(Elevation elevation, string argument, string value)
    {
        lock (RunLock)
        {
            if (!AreTasksAvailable())
                return CanElevate(elevation) && RunElevatedAndInstall(argument, value);

            var requestId = Guid.NewGuid().ToString("N");
            dynamic? service = null;
            dynamic? folder = null;
            dynamic? task = null;
            dynamic? runningTask = null;
            try
            {
                service = CreateService();
                folder = service.GetFolder("\\");
                task = folder.GetTask(TaskName);
                HardwareSettings.HardwareCommandResult = null;
                HardwareSettings.PendingHardwareCommand = string.Join(CommandSeparator, requestId, argument, value);
                runningTask = task.Run(null);

                if (!WaitFor(() => HardwareSettings.PendingHardwareCommand is null, PickupTimeoutMilliseconds))
                {
                    // Задача не ответила: возможно, она указывает на прежний путь
                    // приложения. Следующая команда проверит регистрацию заново.
                    _tasksVerified = false;
                    return false;
                }
            }
            catch (Exception exception)
            {
                _tasksVerified = false;
                AppLog.Error($"Could not run privileged hardware command: {argument} {value}", exception);
                return false;
            }
            finally
            {
                HardwareSettings.PendingHardwareCommand = null;
                Release(runningTask);
                Release(task);
                Release(folder);
                Release(service);
            }

            return WaitForResult(requestId, argument);
        }
    }

    private static bool CanElevate(Elevation elevation) => elevation switch
    {
        Elevation.Interactive => true,
        Elevation.UnlessDeclined => !_elevationDeclined,
        _ => false
    };

    /// <summary>Итог команды, которую привилегированный экземпляр уже забрал.</summary>
    private static bool WaitForResult(string requestId, string argument)
    {
        string[]? result = null;
        if (!WaitFor(() =>
            {
                result = HardwareSettings.HardwareCommandResult?.Split(CommandSeparator);
                return result is { Length: >= 2 } && result[0] == requestId;
            }, ResultTimeoutMilliseconds)
            || result is null)
        {
            AppLog.Error($"Privileged hardware command {argument} returned no result");
            return false;
        }

        HardwareSettings.HardwareCommandResult = null;
        return result[1] switch
        {
            "0" => true,
            "1" => throw new PrivilegedCommandException(result.Length > 2 && !string.IsNullOrWhiteSpace(result[2])
                ? result[2]
                : L.T("Команда оборудования не выполнена.",
                    "The hardware command failed.",
                    "硬件命令执行失败。")),
            _ => false
        };
    }

    private static bool RunElevatedAndInstall(string argument, string value)
    {
        var startInfo = new ProcessStartInfo(GetExecutablePath())
        {
            UseShellExecute = true,
            Verb = "runas"
        };
        startInfo.ArgumentList.Add(argument.Replace("--apply-", "--set-", StringComparison.Ordinal));
        startInfo.ArgumentList.Add(value);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return false;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            NoteElevationDeclined();
            throw;
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not initialize privileged hardware control", exception);
            return false;
        }
    }

    private static void DeleteTask(dynamic folder, string name)
    {
        try
        {
            folder.DeleteTask(name, 0);
        }
        catch (FileNotFoundException)
        {
        }
        catch (COMException exception) when ((uint)exception.HResult == 0x80070002)
        {
        }
    }

    private static dynamic CreateService()
    {
        var serviceType = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Windows Task Scheduler is unavailable.");
        dynamic service = Activator.CreateInstance(serviceType)!;
        service.Connect();
        return service;
    }

    private static string GetExecutablePath() => Environment.ProcessPath
        ?? throw new InvalidOperationException("Could not determine the path to HonorPCHelper.exe.");

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }
}
