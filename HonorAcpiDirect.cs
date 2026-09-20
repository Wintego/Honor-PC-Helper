using System.Management;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace HonorPCHelper;

/// <summary>
/// Прямой вызов ACPI-WMI Honor из обычного пользовательского процесса:
/// OemWMIMethod.OemWMIfun, вход 64 байта `&lt;группа&gt; &lt;команда&gt; &lt;значение&gt;`.
/// Тем же путём идёт Honor PC Manager (снято хуком IWbemServices::ExecMethod
/// в MBAMessageCenter.exe).
///
/// Этот путь нужен там, где команда идёт в ответ на нажатие клавиши и обязана
/// отработать сразу: круг через привилегированную задачу стоит сотни
/// миллисекунд, и шаг яркости или индикатор микрофона заметно отставали бы.
/// Блок данных по умолчанию доступен только администраторам, поэтому право
/// вызывать его выдаётся текущему пользователю один раз - привилегированной
/// задачей. Разрешение выдаётся на весь блок, поэтому одной выдачи хватает
/// всем командам этого пути.
/// </summary>
internal static class HonorAcpiDirect
{
    private const string Namespace = @"root\WMI";
    private const string ClassName = "OemWMIMethod";
    private const string MethodName = "OemWMIfun";
    private const string PreferredInstance = "HWMI_0";
    private const int InputLength = 64;

    // GUID блока данных ACPI-WMI (класс OemWMIMethod).
    private const string DataBlockGuid = "abbc0f5b-8ea1-11d1-a000-c90629100000";
    private const string SecurityKeyPath = @"SYSTEM\CurrentControlSet\Control\WMI\Security";

    private static readonly Lock Gate = new();
    private static ManagementObject? _method;
    private static bool _accessDenied;
    private static bool _grantRequested;

    /// <summary>
    /// Возвращает false, если прямой путь недоступен - вызывающий решает,
    /// чем его заменить: яркость откатывается на WmiSetBrightness,
    /// индикатор микрофона просто остаётся как есть.
    /// </summary>
    internal static bool TryCall(byte group, byte command, byte value)
    {
        lock (Gate)
        {
            if (_accessDenied)
                return false;

            try
            {
                return CallCore(group, command, value);
            }
            catch (Exception)
            {
                // Кэш мог устареть (перезапуск WMI) - одна повторная попытка с нуля.
                Reset();
                try
                {
                    return CallCore(group, command, value);
                }
                catch (Exception retryException)
                {
                    _accessDenied = true;
                    AppLog.Error(
                        $"Honor ACPI direct command {group:X2} {command:X2} unavailable",
                        retryException);
                    RequestAccessOnce();
                    return false;
                }
            }
        }
    }

    private static bool CallCore(byte group, byte command, byte value)
    {
        _method ??= FindInstance();
        if (_method is null)
            return false;

        var input = new byte[InputLength];
        input[0] = group;
        input[1] = command;
        input[2] = value;

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
            AppLog.Info("Honor ACPI direct access granted");
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
