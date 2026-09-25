namespace HonorPCHelper;

/// <summary>
/// Клавиша F8: выключение камеры.
///
/// Прошивка по нажатию только шлёт событие OemWMIEvent 0x288 - саму камеру она
/// не трогает. Honor PC Manager по этому событию переключал значение
/// <see cref="CameraMuteController"/>; без него клавиша мертва.
///
/// Ветка реестра принадлежит администраторам, поэтому первое нажатие идёт
/// через привилегированную задачу - она выдаёт текущему пользователю право
/// записи. Дальше значение пишется прямо отсюда, и клавиша отрабатывает сразу.
/// </summary>
internal sealed class CameraMuteService
{
    private readonly Lock _gate = new();
    private bool _accessRequested;

    /// <summary>
    /// Готовит быстрый путь заранее, при запуске приложения. Иначе выдача права
    /// записи - запуск привилегированной задачи, около секунды - доставалась
    /// первому нажатию F8, и клавиша отрабатывала с заметной задержкой ровно
    /// один раз за установку.
    ///
    /// Окно UAC здесь недопустимо: запуск приложения человек с камерой
    /// не связывает. Поэтому без зарегистрированной фоновой задачи подготовка
    /// молча пропускается, и права выдаются по первому нажатию, как раньше.
    /// </summary>
    internal void Prepare()
    {
        lock (_gate)
        {
            if (_accessRequested || CameraMuteController.IsWriteAllowed())
                return;

            if (!PrivilegedHardware.AreTasksAvailable())
                return;

            // Неудача при запуске не должна лишать первое нажатие собственной
            // попытки: во время загрузки системы планировщик бывает занят.
            try
            {
                if (!GrantAccessOnce(Elevation.Never))
                    _accessRequested = false;
            }
            catch (Exception exception)
            {
                AppLog.Error("Could not prepare camera switch access", exception);
                _accessRequested = false;
            }
        }
    }

    /// <summary>
    /// Переключает камеру и возвращает новое состояние: true - выключена.
    /// null - переключить не удалось, причина уже в журнале.
    /// </summary>
    internal bool? Toggle()
    {
        lock (_gate)
        {
            var target = !CameraMuteController.IsCameraOff();
            if (!Apply(target))
                return null;

            AppLog.Info($"Camera {(target ? "disabled" : "enabled")} by Fn key");
            return target;
        }
    }

    private bool Apply(bool off)
    {
        if (CameraMuteController.TrySetCameraOff(off))
            return true;

        try
        {
            // Права ещё не выданы. Сначала выдаём их: запуск задачи стоит секунд,
            // и платить эту цену на каждом нажатии незачем - после выдачи значение
            // пишется прямо отсюда.
            if (GrantAccessOnce(Elevation.Interactive) && CameraMuteController.TrySetCameraOff(off))
                return true;

            // Выдать права не удалось - остаётся длинный путь: значение пишет
            // сама привилегированная задача.
            if (PrivilegedHardware.TryRunCameraTask(off))
                return true;
        }
        catch (Exception exception)
        {
            // В том числе отказ от запроса UAC - клавиша тогда просто не сработала.
            AppLog.Error("Could not switch the camera", exception);
            return false;
        }

        AppLog.Error("Could not switch the camera");
        return false;
    }

    /// <summary>
    /// Один раз за сеанс просит привилегированную задачу выдать текущему
    /// пользователю право записи. Повторные попытки не делаются: если задача
    /// недоступна, она недоступна и для следующего нажатия.
    /// </summary>
    private bool GrantAccessOnce(Elevation elevation)
    {
        if (_accessRequested)
            return false;
        _accessRequested = true;

        if (!PrivilegedHardware.TryRunGrantCameraAccessTask(elevation))
            return false;

        AppLog.Info("Camera switch write access granted");
        return true;
    }
}
