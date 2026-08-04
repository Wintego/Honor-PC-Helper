namespace HonorPCHelper;

/// <summary>Жест на краю экрана тачпада; значение - подкоманда vendor-репорта.</summary>
internal enum TouchpadEdgeGesture : byte
{
    /// <summary>Регулировка яркости, левый край. Реестр PC Manager: EdgeGestureAdjusBrightness.</summary>
    Brightness = TouchpadVendorLink.EdgeBrightnessCommand,

    /// <summary>Регулировка громкости, правый край. Реестр PC Manager: EdgeGestureAdjusVolume.</summary>
    Volume = TouchpadVendorLink.EdgeVolumeCommand
}

/// <summary>
/// Жесты на краях тачпада: вертикальный свайп одним пальцем вдоль левого края
/// меняет яркость, вдоль правого - громкость. Настройка живёт в прошивке и,
/// как и вибрация, сбрасывается при переподключении устройства и после сна,
/// поэтому её нужно переприменять.
///
/// Проверено на живом устройстве (Goodix HID\TOPS010, COL07):
///
/// - Жест яркости включён (0E 07 01) - прошивка шлёт input-репорты
///   0E 03 &lt;направление&gt;, которые обрабатывает <see cref="TouchpadBrightnessService"/>.
///   Сама прошивка яркость не меняет, только сообщает о жесте.
/// - Жест яркости выключен (0E 07 00) - репортов нет вообще, обработчик молчит.
///   Дублирования действия не возникает: флаг просто открывает и закрывает
///   источник события, а решение о смене яркости принимает только наш код.
/// - Жест громкости (0E 08) работает целиком внутри прошивки: при свайпе вдоль
///   правого края громкость меняется, но ни на одной доступной из пользовательского
///   режима коллекции (col02 Consumer Control, col04, col05, col06, col07) при этом
///   не появляется ни одного input-репорта. Обрабатывать его в HonorPCHelper
///   не нужно и, судя по всему, невозможно - достаточно включать и выключать флаг.
/// </summary>
internal static class TouchpadGesturesController
{
    private static readonly TouchpadEdgeGesture[] All =
        [TouchpadEdgeGesture.Brightness, TouchpadEdgeGesture.Volume];

    /// <summary>
    /// Включает или выключает жест в прошивке тачпада. Бросает исключение,
    /// если устройство не найдено или репорт не удалось записать.
    /// </summary>
    internal static void SetEnabled(TouchpadEdgeGesture gesture, bool enabled)
        => TouchpadVendorLink.Send((byte)gesture, enabled ? (byte)1 : (byte)0);

    internal static bool IsEnabled(TouchpadEdgeGesture gesture)
        => HardwareSettings.GetTouchpadEdgeGesture(gesture) ?? true;

    internal static void Apply(TouchpadEdgeGesture gesture, bool enabled)
    {
        SetEnabled(gesture, enabled);
        HardwareSettings.SetTouchpadEdgeGesture(gesture, enabled);
    }

    /// <summary>
    /// Переприменяет сохранённые состояния жестов. Значения, которых пользователь
    /// не касался, не трогаем: в прошивке они и так включены по умолчанию.
    /// </summary>
    internal static void Reapply()
    {
        foreach (var gesture in All)
        {
            var enabled = HardwareSettings.GetTouchpadEdgeGesture(gesture);
            if (enabled is null)
                continue;

            try
            {
                SetEnabled(gesture, enabled.Value);
            }
            catch (Exception exception)
            {
                AppLog.Error($"Failed to reapply touchpad edge gesture {gesture}", exception);
            }
        }
    }
}
