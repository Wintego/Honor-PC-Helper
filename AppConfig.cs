using System.Text.Json;

namespace HonorPCHelper;

/// <summary>
/// Пользовательские настройки из config.json рядом с exe. Файл необязателен и
/// автоматически не создаётся: без него действуют значения по умолчанию.
/// Ошибки чтения не фатальны - приложение продолжает работать на умолчаниях.
/// </summary>
internal sealed class AppConfig
{
    public int BrightnessStepPercent { get; set; } = 5;
    public int SensorRefreshIntervalMs { get; set; } = 5_000;
    public bool TouchpadBrightnessEnabled { get; set; } = true;
    public bool HotkeysEnabled { get; set; } = true;

    internal static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "config.json");

    // Важно: объявлено до Current - статические члены инициализируются в порядке объявления,
    // а Load() использует SerializerOptions.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    internal static AppConfig Current { get; } = Load();

    private static AppConfig Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppConfig();

            var config = JsonSerializer.Deserialize<AppConfig>(
                File.ReadAllText(FilePath), SerializerOptions) ?? new AppConfig();
            config.Clamp();
            return config;
        }
        catch (Exception exception)
        {
            AppLog.Error("Failed to load config.json, using defaults", exception);
            return new AppConfig();
        }
    }

    private void Clamp()
    {
        BrightnessStepPercent = Math.Clamp(BrightnessStepPercent, 1, 25);
        SensorRefreshIntervalMs = Math.Clamp(SensorRefreshIntervalMs, 1_000, 60_000);
    }
}
