using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace HonorPCHelper;

/// <summary>
/// Выключатель камеры (клавиша F8).
///
/// Камера остаётся включённым устройством: кадры гасит Device MFT в драйвере
/// камеры. Он читает `HKLM\SOFTWARE\HONOR\ASvidDMFT\Settings\CameraStatus`
/// прямо в обработке кадра (`ControlPrivacy` в SonixDeviceMFT.dll), поэтому
/// запись значения меняет картинку сразу и на живом потоке - ни переподключения
/// устройства, ни перезапуска приложений не требуется. 1 - камера видит,
/// 0 - приложения получают чёрный кадр.
///
/// Тем же значением пользуется Honor PC Manager: снято зондом с работающего
/// MagicBook Pro 14 - по нажатию F8 (событие OemWMIEvent 0x288) он переключает
/// именно CameraStatus, не трогая ни выключатели приватности Windows,
/// ни состояние самого устройства.
///
/// Ветка принадлежит администраторам, поэтому право записи выдаётся текущему
/// пользователю один раз - привилегированной задачей, как и для ACPI-WMI.
/// </summary>
internal static class CameraMuteController
{
    private const string SettingsPath = @"SOFTWARE\HONOR\ASvidDMFT\Settings";
    private const string CameraStatusValue = "CameraStatus";

    /// <summary>
    /// true - камера сейчас выключена. Значения нет (драйвер с этим MFT
    /// не установлен) - считаем камеру включённой.
    /// </summary>
    internal static bool IsCameraOff()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SettingsPath);
            return key?.GetValue(CameraStatusValue) as int? == 0;
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read the camera switch", exception);
            return false;
        }
    }

    /// <summary>
    /// true - право записи уже выдано и значение пишется прямо отсюда.
    /// Проверка ничего не меняет: ветка только открывается на запись.
    /// </summary>
    internal static bool IsWriteAllowed()
    {
        using var key = OpenForWrite();
        return key is not null;
    }

    /// <summary>
    /// Быстрый путь: запись из обычного процесса. Возвращает false, пока право
    /// на запись не выдано, - тогда вызывающий идёт через привилегированную задачу.
    /// </summary>
    internal static bool TrySetCameraOff(bool off)
    {
        using var key = OpenForWrite();
        if (key is null)
            return false;

        try
        {
            key.SetValue(CameraStatusValue, off ? 0 : 1, RegistryValueKind.DWord);
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not write the camera switch", exception);
            return false;
        }
    }

    private static RegistryKey? OpenForWrite()
    {
        try
        {
            // Права выданы точечно, на два действия со значениями: открытие
            // ветки целиком на запись просило бы ещё и создание подразделов
            // и упиралось бы в отказ.
            return Registry.LocalMachine.OpenSubKey(
                SettingsPath,
                RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryRights.QueryValues | RegistryRights.SetValue);
        }
        catch (Exception)
        {
            // Отказ в доступе здесь штатный: ветка открыта на запись только
            // администраторам, пока право не выдано.
            return null;
        }
    }

    // Выполняется в привилегированном экземпляре приложения.
    internal static void SetCameraOff(bool off)
    {
        using var key = Registry.LocalMachine.CreateSubKey(SettingsPath, true)
            ?? throw new InvalidOperationException(L.T(
                "Не удалось открыть ветку выключателя камеры.",
                "Could not open the camera switch key.",
                "无法打开摄像头开关注册表项。"));
        key.SetValue(CameraStatusValue, off ? 0 : 1, RegistryValueKind.DWord);
    }

    // Выполняется в привилегированном экземпляре приложения.
    internal static void GrantAccess()
    {
        var sid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Не удалось определить SID пользователя.");

        // Ветку заводит установщик драйвера камеры, но на свежей системе её
        // может ещё не быть: сначала создаём, потом выдаём права.
        Registry.LocalMachine.CreateSubKey(SettingsPath, true)?.Dispose();

        // Смена списка доступа требует отдельного права, в обычное открытие
        // на запись оно не входит.
        using var key = Registry.LocalMachine.OpenSubKey(
            SettingsPath,
            RegistryKeyPermissionCheck.ReadWriteSubTree,
            RegistryRights.ReadPermissions | RegistryRights.ChangePermissions)
            ?? throw new InvalidOperationException("Не удалось открыть ветку выключателя камеры.");

        // Права выдаются точечно - на чтение и запись значений этой ветки:
        // остальное её содержимое настраивает драйвер камеры.
        var security = key.GetAccessControl(AccessControlSections.Access);
        security.AddAccessRule(new RegistryAccessRule(
            sid,
            RegistryRights.QueryValues | RegistryRights.SetValue,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        key.SetAccessControl(security);
    }
}
