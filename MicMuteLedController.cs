namespace HonorPCHelper;

/// <summary>
/// Индикатор на клавише микрофона (F7).
///
/// Прошивка сама его не зажигает: клавиша только шлёт событие OemWMIEvent,
/// а лампочку выставляет софт отдельной командой. В Linux этим же кодом
/// управляет huawei-wmi (MICMUTE_LED_SET, метод ACPI \SMLS), где светодиод
/// зарегистрирован как обычный led_classdev с триггером audio-micmute -
/// то есть его состоянием тоже распоряжается система, а не оборудование.
/// Поэтому после удаления Honor PC Manager лампочка остаётся в том положении,
/// в котором её бросили.
/// </summary>
internal static class MicMuteLedController
{
    private const byte GroupPower = 0x04;
    private const byte CommandMicMuteLed = 0x0B;

    /// <summary>Возвращает false, если BIOS недоступен - лампочка тогда просто не меняется.</summary>
    internal static bool TrySet(bool on)
        => HonorAcpiDirect.TryCall(GroupPower, CommandMicMuteLed, on ? (byte)1 : (byte)0);
}
