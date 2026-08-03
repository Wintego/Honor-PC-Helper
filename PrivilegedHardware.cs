using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace HonorPCHelper;

internal static class PrivilegedHardware
{
    private const string TaskName = "Honor PC Helper Privileged Hardware";
    private const char CommandSeparator = '\u001F';
    private static readonly object RunLock = new();

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
            return !string.IsNullOrWhiteSpace(executablePath)
                && File.Exists(executablePath)
                && string.Equals(Path.GetFullPath(executablePath), Path.GetFullPath(GetExecutablePath()), StringComparison.OrdinalIgnoreCase)
                && string.Equals(Convert.ToString(action.Arguments)?.Trim(), "--run-pending-hardware-command", StringComparison.Ordinal);
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

    internal static bool TryRunBacklightTask(KeyboardBacklightLevel level)
        => TryRunTask("--apply-keyboard-backlight", level.ToString());

    internal static bool TryRunBacklightTimeoutTask(ushort seconds)
        => TryRunTask("--apply-keyboard-backlight-timeout", seconds.ToString());

    internal static bool TryRunBatteryTask(BatteryProtectionMode mode)
        => TryRunTask("--apply-battery-mode", mode.ToString());

    internal static bool TryRunPowerUnlockTask(bool enabled)
        => TryRunTask("--apply-power-unlock", enabled.ToString());

    internal static bool TryRunGrantBrightnessAccessTask()
        => TryRunTask("--grant-brightness-access", "1");

    internal static Task<bool> TryRunBacklightTaskAsync(KeyboardBacklightLevel level)
        => Task.Run(() => TryRunBacklightTask(level));

    internal static Task<bool> TryRunBacklightTimeoutTaskAsync(ushort seconds)
        => Task.Run(() => TryRunBacklightTimeoutTask(seconds));

    internal static Task<bool> TryRunBatteryTaskAsync(BatteryProtectionMode mode)
        => Task.Run(() => TryRunBatteryTask(mode));

    internal static Task<bool> TryRunPowerUnlockTaskAsync(bool enabled)
        => Task.Run(() => TryRunPowerUnlockTask(enabled));

    internal static Task<bool> TryReadSensorsTaskAsync()
        => Task.Run(TryReadSensorsTask);

    private static bool TryReadSensorsTask()
    {
        var requestId = Guid.NewGuid().ToString("N");
        if (!TryRunTask("--read-sensors", requestId))
            return false;

        var deadline = Environment.TickCount64 + 5000;
        while (Environment.TickCount64 < deadline)
        {
            if (HardwareSettings.SensorSnapshot?.StartsWith(requestId + '|', StringComparison.Ordinal) == true)
                return true;
            Thread.Sleep(50);
        }
        return false;
    }

    internal static int RunPendingCommand()
    {
        var command = HardwareSettings.PendingHardwareCommand;
        if (string.IsNullOrEmpty(command))
            return 2;

        HardwareSettings.PendingHardwareCommand = null;
        var parts = command.Split(CommandSeparator);
        if (parts.Length != 2)
            return 2;

        try
        {
            if (parts[0] == "--apply-keyboard-backlight"
                && Enum.TryParse<KeyboardBacklightLevel>(parts[1], true, out var level))
                new KeyboardBacklightController().SetLevel(level);
            else if (parts[0] == "--apply-keyboard-backlight-timeout"
                && ushort.TryParse(parts[1], out var seconds))
                new KeyboardBacklightController().SetTimeout(seconds);
            else if (parts[0] == "--apply-battery-mode"
                && Enum.TryParse<BatteryProtectionMode>(parts[1], true, out var mode))
                new BatteryProtectionController().SetMode(mode);
            else if (parts[0] == "--apply-power-unlock"
                && bool.TryParse(parts[1], out var enabled))
                new PowerUnlockController().SetEnabled(enabled);
            else if (parts[0] == "--grant-brightness-access")
                HonorAcpiBrightness.GrantAccess();
            else if (parts[0] == "--read-sensors")
                HardwareSensorController.ReadAndStore(parts[1]);
            else
                return 2;
            return 0;
        }
        catch (Exception exception)
        {
            AppLog.Error($"Privileged hardware command failed: {command}", exception);
            return 1;
        }
    }

    private static bool TryRunTask(params string[] arguments)
    {
        lock (RunLock)
        {
            if (!AreTasksAvailable())
                return RunElevatedAndInstall(arguments);

            dynamic? service = null;
            dynamic? folder = null;
            dynamic? task = null;
            dynamic? runningTask = null;
            try
            {
                service = CreateService();
                folder = service.GetFolder("\\");
                task = folder.GetTask(TaskName);
                HardwareSettings.PendingHardwareCommand = string.Join(CommandSeparator, arguments);
                runningTask = task.Run(null);

                var deadline = Environment.TickCount64 + 10000;
                while (Environment.TickCount64 < deadline)
                {
                    if (HardwareSettings.PendingHardwareCommand is null)
                        return true;
                    Thread.Sleep(50);
                }
                return false;
            }
            catch (Exception exception)
            {
                AppLog.Error($"Could not run privileged hardware command: {string.Join(' ', arguments)}", exception);
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
        }
    }

    private static bool RunElevatedAndInstall(string[] arguments)
    {
        var startInfo = new ProcessStartInfo(GetExecutablePath())
        {
            UseShellExecute = true,
            Verb = "runas"
        };
        startInfo.ArgumentList.Add(arguments[0].Replace("--apply-", "--set-", StringComparison.Ordinal));
        foreach (var argument in arguments.Skip(1))
            startInfo.ArgumentList.Add(argument);

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
