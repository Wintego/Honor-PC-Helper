using System.ComponentModel;
using System.Diagnostics;

namespace HonorPCHelper;

/// <summary>
/// Общий сценарий применения аппаратной настройки из меню трея.
///
/// Сначала команда уходит фоновой задаче планировщика, которая уже работает
/// с правами администратора и не показывает запрос UAC. Если задача ещё
/// не установлена или не ответила, приложение перезапускается с повышением
/// прав: этот запуск и выполняет настройку, и регистрирует задачу на будущее.
/// </summary>
internal static class HardwareCommand
{
    /// <param name="tryPrivilegedTask">Попытка выполнить команду через фоновую задачу.</param>
    /// <param name="argument">Аргумент командной строки для запасного запуска с UAC.</param>
    /// <param name="value">Значение аргумента.</param>
    /// <param name="startFailedMessage">Сообщение, если процесс не удалось запустить.</param>
    /// <param name="onApplied">Вызывается только после успешного применения.</param>
    internal static async Task ApplyAsync(
        Func<Task<bool>> tryPrivilegedTask,
        string argument,
        string value,
        string startFailedMessage,
        Action onApplied)
    {
        try
        {
            if (await tryPrivilegedTask() || await RunElevatedAsync(argument, value, startFailedMessage))
                onApplied();
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            // Пользователь отменил запрос UAC - молча выходим.
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Honor PC Helper", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static async Task<bool> RunElevatedAsync(string argument, string value, string startFailedMessage)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException(L.T(
                "Не удалось определить путь к HonorPCHelper.exe.",
                "Could not determine the path to HonorPCHelper.exe.",
                "无法确定 HonorPCHelper.exe 的路径。"));
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Verb = "runas"
        };
        startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add(value);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(startFailedMessage);
        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }
}
