namespace HonorPCHelper;

// Шаг яркости через ACPI-WMI Honor: `06 35 <направление>`.
// Значение меняет EC/прошивка, поэтому Windows рисует свой штатный OSD - в отличие от
// WmiMonitorBrightnessMethods.WmiSetBrightness, который OSD не вызывает.
// Шаг задаёт прошивка (10%), поэтому запасной шаг на этот путь не влияет.
internal static class HonorAcpiBrightness
{
    private const byte GroupDisplay = 0x06;
    private const byte CommandBrightness = 0x35;
    private const byte DirectionUp = 0x00;
    private const byte DirectionDown = 0x01;

    // Возвращает false, если ACPI-путь недоступен - вызывающий откатывается на WMI.
    internal static bool TryStep(bool up)
        => HonorAcpiDirect.TryCall(GroupDisplay, CommandBrightness, up ? DirectionUp : DirectionDown);
}
