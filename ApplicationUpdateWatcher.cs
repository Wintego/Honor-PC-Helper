namespace HonorPCHelper;

/// <summary>
/// Фоновая проверка новой версии приложения. Окно драйверов узнаёт об
/// обновлении только пока открыто, а значок в трее должен показывать точку
/// и без него, поэтому найденное обновление хранится здесь: и значок, и меню,
/// и окно берут его из одного места.
/// </summary>
internal sealed class ApplicationUpdateWatcher : IDisposable
{
    // Первая проверка отложена: при запуске сеть занята автозагрузкой,
    // а сама точка в трее ничего не ждёт от первых секунд работы.
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private static readonly Lock Gate = new();
    private static ApplicationUpdate? _available;

    /// <summary>Сообщает, что найденное обновление появилось или сменилось.</summary>
    internal static event Action? Changed;

    /// <summary>Доступное обновление приложения или null, если его нет.</summary>
    internal static ApplicationUpdate? Available
    {
        get { lock (Gate) return _available; }
    }

    /// <summary>
    /// Запоминает результат проверки. Вызывается и отсюда, и из окна драйверов:
    /// проверка там свежее фоновой, поэтому её результат тоже гасит или
    /// зажигает точку на значке.
    /// </summary>
    internal static void Publish(ApplicationUpdate? update)
    {
        lock (Gate)
        {
            if (_available?.Version == update?.Version)
                return;
            _available = update;
        }
        Changed?.Invoke();
    }

    private readonly ApplicationUpdateService _service = new();
    private readonly CancellationTokenSource _cancellation = new();

    internal void Start() => _ = RunAsync(_cancellation.Token);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var delay = FirstDelay;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, cancellationToken);
                Publish((await _service.CheckAsync(cancellationToken)).Update);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                // Сеть недоступна или GitHub ответил ошибкой - к следующей
                // проверке это пройдёт само, поэтому только запись в журнал.
                AppLog.Error("Background application update check failed", exception);
            }
            delay = Interval;
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
