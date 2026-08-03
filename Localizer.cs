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

    private static AppLanguage Detect()
    {
        var code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        if (code.Equals("ru", StringComparison.OrdinalIgnoreCase))
        {
            return AppLanguage.Russian;
        }

        if (code.Equals("zh", StringComparison.OrdinalIgnoreCase))
        {
            return AppLanguage.Chinese;
        }

        return AppLanguage.English;
    }

    internal static string T(string russian, string english, string? chinese = null) => Language switch
    {
        AppLanguage.Russian => russian,
        AppLanguage.Chinese => string.IsNullOrEmpty(chinese) ? english : chinese,
        _ => english
    };
}
