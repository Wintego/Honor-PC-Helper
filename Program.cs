namespace HonorPCHelper;

using System.ComponentModel;
using System.Diagnostics;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--set-battery-mode")
            return SetBatteryMode(args[1]);
        if (args.Length == 2 && args[0] == "--apply-battery-mode")
            return ApplyBatteryMode(args[1]);
        if (args.Length == 2 && args[0] == "--set-power-unlock")
            return SetPowerUnlock(args[1]);
        if (args.Length == 2 && args[0] == "--apply-power-unlock")
            return ApplyPowerUnlock(args[1]);
        if (args.Length == 2 && args[0] == "--set-keyboard-backlight")
            return SetKeyboardBacklight(args[1]);
        if (args.Length == 2 && args[0] == "--apply-keyboard-backlight")
            return ApplyKeyboardBacklight(args[1]);
        if (args.Length == 2 && args[0] == "--set-keyboard-backlight-timeout")
            return SetKeyboardBacklightTimeout(args[1]);
        if (args.Length == 2 && args[0] == "--apply-keyboard-backlight-timeout")
            return ApplyKeyboardBacklightTimeout(args[1]);
        if (args.Length == 2 && args[0] == "--read-sensors")
            return ReadSensors(args[1]);
        if (args.Length == 1 && args[0] == "--restore-hardware-settings")
            return RestoreHardwareSettings();
        if (args.Length == 1 && args[0] == "--install-privileged-tasks")
            return InstallPrivilegedTasks();
        if (args.Length == 1 && args[0] == "--uninstall-privileged-tasks")
            return UninstallPrivilegedTasks();
        if (args.Length == 1 && args[0] == "--run-pending-hardware-command")
            return PrivilegedHardware.RunPendingCommand();

        using var mutex = new Mutex(false, "HonorPCHelper.SingleInstance", out var createdNew);
        if (!createdNew)
            return 0;

        ApplicationConfiguration.Initialize();
        Application.Run(new HelperApplicationContext());
        return 0;
    }

    private static int ReadSensors(string requestId)
    {
        try
        {
            HardwareSensorController.ReadAndStore(requestId);
            PrivilegedHardware.EnsureRegistered();
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Honor PC Helper", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int SetBatteryMode(string value)
    {
        try
        {
            if (!Enum.TryParse<BatteryProtectionMode>(value, true, out var mode))
                return 2;

            new BatteryProtectionController().SetMode(mode);
            PrivilegedHardware.EnsureRegistered();
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Honor PC Helper", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int ApplyBatteryMode(string value)
    {
        try
        {
            if (!Enum.TryParse<BatteryProtectionMode>(value, true, out var mode))
                return 2;
            new BatteryProtectionController().SetMode(mode);
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static int SetPowerUnlock(string value)
    {
        try
        {
            if (!bool.TryParse(value, out var enabled))
                return 2;

            new PowerUnlockController().SetEnabled(enabled);
            PrivilegedHardware.EnsureRegistered();
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Honor PC Helper", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int ApplyPowerUnlock(string value)
    {
        try
        {
            if (!bool.TryParse(value, out var enabled))
                return 2;
            new PowerUnlockController().SetEnabled(enabled);
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static int SetKeyboardBacklight(string value)
    {
        try
        {
            if (!Enum.TryParse<KeyboardBacklightLevel>(value, true, out var level))
                return 2;

            new KeyboardBacklightController().SetLevel(level);
            PrivilegedHardware.EnsureRegistered();
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Honor PC Helper", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int ApplyKeyboardBacklight(string value)
    {
        try
        {
            if (!Enum.TryParse<KeyboardBacklightLevel>(value, true, out var level))
                return 2;

            new KeyboardBacklightController().SetLevel(level);
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static int SetKeyboardBacklightTimeout(string value)
    {
        try
        {
            if (!ushort.TryParse(value, out var seconds))
                return 2;

            new KeyboardBacklightController().SetTimeout(seconds);
            PrivilegedHardware.EnsureRegistered();
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Honor PC Helper", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int ApplyKeyboardBacklightTimeout(string value)
    {
        try
        {
            if (!ushort.TryParse(value, out var seconds))
                return 2;

            new KeyboardBacklightController().SetTimeout(seconds);
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static int RestoreHardwareSettings()
    {
        try
        {
            var level = HardwareSettings.KeyboardBacklight;
            if (level.HasValue)
                new KeyboardBacklightController().SetLevel(level.Value);
            new KeyboardBacklightController().SetTimeout(HardwareSettings.KeyboardBacklightTimeout);
            var batteryMode = HardwareSettings.BatteryProtection;
            if (batteryMode.HasValue)
                new BatteryProtectionController().SetMode(batteryMode.Value);
            new PowerUnlockController().SetEnabled(false);
            HardwareSettings.PowerUnlock = false;
            HardwareSettings.PerformanceModeActive = false;
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static int InstallPrivilegedTasks()
    {
        try
        {
            PrivilegedHardware.EnsureRegistered();
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Honor PC Helper", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int UninstallPrivilegedTasks()
    {
        try
        {
            PrivilegedHardware.RemoveRegistered();
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Honor PC Helper", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
