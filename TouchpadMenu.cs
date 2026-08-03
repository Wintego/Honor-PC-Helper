namespace HonorPCHelper;

/// <summary>
/// Единое подменю трея «Тачпад»: интенсивность виброотклика и жесты на краях экрана.
/// Обе группы живут в прошивке тачпада и переприменяются в одних и тех же точках.
/// </summary>
internal static class TouchpadMenu
{
    internal static void Build(NativePopupMenu menu)
    {
        menu.AddItem(L.T("Интенсивность вибрации", "Vibration strength", "振动强度"), null, enabled: false);
        TouchpadHapticsMenu.Build(menu);

        menu.AddSeparator();

        menu.AddItem(L.T("Жесты на краях тачпада", "Touchpad edge gestures", "触控板边缘手势"), null, enabled: false);
        TouchpadGesturesMenu.Build(menu);
    }
}
