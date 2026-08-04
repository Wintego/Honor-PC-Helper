using System.Management;

namespace HonorPCHelper;

internal sealed class HonorWmiCommandException(int errorCode, string message) : InvalidOperationException(message)
{
    internal int ErrorCode { get; } = errorCode;
}

/// <summary>
/// Reusable HONOR BIOS WMI connection. Connecting to the scope and locating the
/// OemWMIMethod instance is expensive, so a single session can be shared across
/// several commands (e.g. one sensor refresh) instead of reconnecting per call.
/// </summary>
internal sealed class HonorWmiSession : IDisposable
{
    private ManagementObject? _instance;

    private ManagementObject GetInstance()
    {
        if (_instance is not null)
            return _instance;

        var scope = new ManagementScope(@"\\.\root\wmi");
        scope.Options.EnablePrivileges = true;
        scope.Connect();

        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM OemWMIMethod"));
        using var instances = searcher.Get();
        return _instance = instances.Cast<ManagementObject>().FirstOrDefault()
            ?? throw new InvalidOperationException(L.T(
                "Интерфейс HONOR BIOS WMI не найден.",
                "HONOR BIOS WMI interface was not found.",
                "未找到 HONOR BIOS WMI 接口。"));
    }

    internal byte[] Call(ulong command)
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var instance = GetInstance();
                using var parameters = instance.GetMethodParameters("OemWMIfun");
                var input = new byte[64];
                BitConverter.GetBytes(command).CopyTo(input, 0);
                parameters["u8Input"] = input;
                using var result = instance.InvokeMethod("OemWMIfun", parameters, null);
                var output = (byte[]?)result?["u8Output"]
                    ?? throw new InvalidOperationException(L.T(
                        "BIOS не вернул ответ.",
                        "BIOS did not return a response.",
                        "BIOS 未返回响应。"));

                if (output.Length > 0 && output[0] == 0)
                    return output;

                // BIOS ответил отказом - команда дошла, повторять её незачем.
                var errorCode = output.Length > 0 ? output[0] : 255;
                lastError = new HonorWmiCommandException(errorCode, L.T(
                    $"BIOS отклонил команду (код {errorCode}).",
                    $"BIOS rejected the command (code {errorCode}).",
                    $"BIOS 拒绝了该命令（代码 {errorCode}）。"));
                break;
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new InvalidOperationException(L.T(
                    "Для обращения к HONOR BIOS нужны права администратора.",
                    "Administrator privileges are required to access HONOR BIOS.",
                    "访问 HONOR BIOS 需要管理员权限。"), exception);
            }
            catch (ManagementException exception)
            {
                lastError = new InvalidOperationException(L.T(
                    "Ошибка HONOR BIOS WMI: ",
                    "HONOR BIOS WMI error: ",
                    "HONOR BIOS WMI 错误：") + exception.Message, exception);
                // Drop the cached instance so the next attempt reconnects.
                _instance?.Dispose();
                _instance = null;
            }
        }

        var finalError = lastError ?? new InvalidOperationException(L.T(
            "Команда BIOS не выполнена.",
            "BIOS command was not executed.",
            "BIOS 命令未执行。"));
        AppLog.Error($"HONOR WMI command 0x{command:X} failed", finalError);
        throw finalError;
    }

    public void Dispose()
    {
        _instance?.Dispose();
        _instance = null;
    }
}

internal static class HonorWmi
{
    internal static byte[] Call(ulong command)
    {
        using var session = new HonorWmiSession();
        return session.Call(command);
    }
}
