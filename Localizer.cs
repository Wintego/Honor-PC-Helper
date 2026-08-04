using System.Globalization;

namespace HonorPCHelper;

internal enum AppLanguage
{
    English,
    Russian,
    Chinese
}

internal static class L
{
    private static readonly AppLanguage Language = Detect();

    private static AppLanguage Detect() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant() switch
    {
        "ru" => AppLanguage.Russian,
        "zh" => AppLanguage.Chinese,
        _ => AppLanguage.English
    };

    internal static string T(string russian, string english, string? chinese = null) => Language switch
    {
        AppLanguage.Russian => russian,
        AppLanguage.Chinese => string.IsNullOrEmpty(chinese) ? english : chinese,
        _ => english
    };
}
