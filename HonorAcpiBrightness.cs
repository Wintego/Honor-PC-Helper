using System.Management;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace HonorPCHelper;

// Шаг яркости через ACPI-WMI Honor: OemWMIMethod.OemWMIfun, вход 64 байта `06 35 <направление>`.
// Тем же путём идёт Honor PC Manager (снято хуком IWbemServices::ExecMethod в MBAMessageCenter.exe).
// Значение меняет EC/прошивка, поэтому Windows рисует свой штатный OSD - в отличие от
// WmiMonitorBrightnessMethods.WmiSetBrightness, который OSD не вызывает.
// Шаг задаёт прошивка (10%), поэтому запасной шаг на этот путь не влияет.
internal static class HonorAcpiBrightness
{
    private const string Namespace = @"root\WMI";
    private const string ClassName = "OemWMIMethod";
    private const string MethodName = "OemWMIfun";
    private const string PreferredInstance = "HWMI_0";

    private const byte GroupDisplay = 0x06;
    private const byte CommandBrightness = 0x35;
    private const byte DirectionUp = 0x00;
    private const byte DirectionDown = 0x01;
    private const int InputLength = 64;

    // GUID блока данных ACPI-WMI (класс OemWMIMethod). По умолчанию доступ есть только
    // у администраторов; разрешение выдаётся один раз через привилегированную задачу.
    private const string DataBlockGuid = "abbc0f5b-8ea1-11d1-a000-c90629100000";
    private const string SecurityKeyPath = @"SYSTEM\CurrentControlSet\Control\WMI\Security";

    private static readonly Lock Gate = new();
    private static ManagementObject? _method;
    private static bool _accessDenied;
    private static bool _grantRequested;

    // Возвращает false, если ACPI-путь недоступен - вызывающий откатывается на WMI.
    internal static bool TryStep(bool up)
    {
        lock (Gate)
        {
            if (_accessDenied)
                return false;

            try
            {
                return StepCore(up);
            }
            catch (Exception)
            {
                // Кэш мог устареть (перезапуск WMI) - одна повторная попытка с нуля.
                Reset();
                try
                {
                    return StepCore(up);
                }
                catch (Exception retryException)
                {
                    _accessDenied = true;
                    AppLog.Error("Honor ACPI brightness unavailable", retryException);
                    RequestAccessOnce();
                    return false;
                }
            }
        }
    }

    private static bool StepCore(bool up)
    {
        _method ??= FindInstance();
        if (_method is null)
            return false;

        var input = new byte[InputLength];
        input[0] = GroupDisplay;
        input[1] = CommandBrightness;
        input[2] = up ? DirectionUp : DirectionDown;

        var parameters = _method.GetMethodParameters(MethodName);
        parameters["u8Input"] = input;
        _method.InvokeMethod(MethodName, parameters, null);
        return true;
    }

    private static ManagementObject? FindInstance()
    {
        using var searcher = new ManagementObjectSearcher(Namespace, $"SELECT * FROM {ClassName}");
        var instances = searcher.Get().Cast<ManagementObject>().ToArray();
        var preferred = instances.FirstOrDefault(instance =>
            instance["InstanceName"] is string name
            && name.EndsWith(PreferredInstance, StringComparison.OrdinalIgnoreCase));
        return preferred ?? instances.FirstOrDefault();
    }

    private static void Reset()
    {
        _method?.Dispose();
        _method = null;
    }

    // Один раз за сеанс просит привилегированную задачу выдать текущему пользователю
    // право вызывать блок данных. После успеха путь снова становится доступен.
    private static void RequestAccessOnce()
    {
        if (_grantRequested)
            return;
        _grantRequested = true;

        _ = Task.Run(() =>
        {
            if (!PrivilegedHardware.TryRunGrantBrightnessAccessTask())
                return;
            lock (Gate)
            {
                _accessDenied = false;
                Reset();
            }
            AppLog.Info("Honor ACPI brightness access granted");
        });
    }

    // Выполняется в привилегированном экземпляре приложения.
    internal static void GrantAccess()
    {
        var sid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Не удалось определить SID пользователя.");

        var descriptor = new RawSecurityDescriptor(
            $"O:BAG:BAD:(A;;0x1fffff;;;SY)(A;;0x1fffff;;;BA)(A;;0x2001f;;;{sid.Value})");
        var binary = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(binary, 0);

        using var key = Registry.LocalMachine.CreateSubKey(SecurityKeyPath, true)
            ?? throw new InvalidOperationException("Не удалось открыть ветку безопасности WMI.");
        key.SetValue(DataBlockGuid, binary, RegistryValueKind.Binary);
    }
}
