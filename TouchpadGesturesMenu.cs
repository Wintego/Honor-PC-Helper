namespace HonorPCHelper;

internal static class TouchpadGesturesMenu
{
    internal static void Build(NativePopupMenu menu)
    {
        AddGesture(menu,
            L.T("Яркость (слева)", "Brightness (left)", "亮度（左侧）"),
            L.T("Проведите вертикально вдоль левого края одним пальцем.",
                "Swipe vertically along the left edge with one finger.",
                "用一根手指沿左边缘垂直滑动。"),
            TouchpadEdgeGesture.Brightness);

        AddGesture(menu,
            L.T("Громкость (справа)", "Volume (right)", "音量（右侧）"),
            L.T("Проведите вертикально вдоль правого края одним пальцем.",
                "Swipe vertically along the right edge with one finger.",
                "用一根手指沿右边缘垂直滑动。"),
            TouchpadEdgeGesture.Volume);
    }

    private static void AddGesture(
        NativePopupMenu menu, string text, string tooltip, TouchpadEdgeGesture gesture)
    {
        var enabled = TouchpadGesturesController.IsEnabled(gesture);
        menu.AddItem(text, () => Toggle(gesture, !enabled), @checked: enabled, tooltip: tooltip);
    }

    private static void Toggle(TouchpadEdgeGesture gesture, bool enabled)
    {
        try
        {
            TouchpadGesturesController.Apply(gesture, enabled);
        }
        catch (Exception exception)
        {
            AppLog.Error($"Failed to set touchpad edge gesture {gesture}", exception);
            MessageBox.Show(exception.Message, "Honor PC Helper",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
