namespace HonorPCHelper;

internal static class TouchpadHapticsMenu
{
    internal static void Build(NativePopupMenu menu)
    {
        var current = HardwareSettings.TouchpadHaptics;

        AddLevel(menu, L.T("Низкая", "Low"), TouchpadHapticsLevel.Low, current);
        AddLevel(menu, L.T("Умеренная", "Medium"), TouchpadHapticsLevel.Medium, current);
        AddLevel(menu, L.T("Высокая", "High"), TouchpadHapticsLevel.High, current);
    }

    private static void AddLevel(
        NativePopupMenu menu, string text, TouchpadHapticsLevel level, TouchpadHapticsLevel? current)
    {
        menu.AddItem(text, () => Apply(level), @checked: current == level);
    }

    private static void Apply(TouchpadHapticsLevel level)
    {
        try
        {
            TouchpadHapticsController.SetLevel(level);
            HardwareSettings.TouchpadHaptics = level;
        }
        catch (Exception exception)
        {
            AppLog.Error("Failed to set touchpad haptics level", exception);
            MessageBox.Show(exception.Message, "Honor PC Helper",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
