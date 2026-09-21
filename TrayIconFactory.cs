using Microsoft.Win32;
using System.Drawing.Drawing2D;

namespace HonorPCHelper;

internal static class TrayIconFactory
{
    // Значков всего восемь: две темы на два режима и на два состояния
    // обновления. UserPreferenceChanged приходит пачками при любой смене
    // системных настроек, поэтому готовые значки переиспользуются, а не
    // рисуются заново вместе с GDI-хендлом.
    private static readonly Lock Gate = new();
    private static readonly Dictionary<(int Size, bool Dark, bool Performance, bool Update), Icon> Cache = [];

    /// <summary>Точка обновления - тот же красный, что и в окне драйверов.</summary>
    private static readonly Color UpdateAccent = Color.FromArgb(255, 45, 45);

    internal static bool IsDarkTheme
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("SystemUsesLightTheme") ?? key?.GetValue("AppsUseLightTheme");
            return value is int light && light == 0;
        }
    }

    /// <summary>
    /// Возвращает значок трея. Значок принадлежит фабрике и живёт до конца
    /// работы процесса - освобождать его у вызывающей стороны не нужно.
    /// </summary>
    internal static Icon Create(bool performanceMode, bool updateAvailable = false)
    {
        const int smCxSmallIcon = 49;
        var size = Math.Max(16, NativeMethods.GetSystemMetrics(smCxSmallIcon));
        var dark = IsDarkTheme;
        lock (Gate)
        {
            if (Cache.TryGetValue((size, dark, performanceMode, updateAvailable), out var cached))
                return cached;

            var icon = Render(size, dark, performanceMode, updateAvailable);
            Cache[(size, dark, performanceMode, updateAvailable)] = icon;
            return icon;
        }
    }

    private static Icon Render(int size, bool dark, bool performanceMode, bool updateAvailable)
    {
        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.PixelOffsetMode = PixelOffsetMode.None;
            graphics.Clear(Color.Transparent);
            var foreground = dark ? Color.White : Color.FromArgb(30, 30, 30);
            var scale = size / 16f;
            var stroke = Math.Max(1f, MathF.Round(scale));
            var outer = RectangleF.FromLTRB(2 * scale, 2 * scale, 14 * scale, 14 * scale);
            var left = new RectangleF(5.5f * scale - stroke / 2, 4 * scale, stroke, 8 * scale);
            var right = new RectangleF(10.5f * scale - stroke / 2, 4 * scale, stroke, 8 * scale);
            var crossbar = new RectangleF(
                5.5f * scale - stroke / 2,
                8 * scale - stroke / 2,
                5 * scale + stroke,
                stroke);

            if (performanceMode)
            {
                using var fill = new SolidBrush(foreground);
                graphics.FillRectangle(fill, outer);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                using var transparent = new SolidBrush(Color.Transparent);
                graphics.FillRectangle(transparent, left);
                graphics.FillRectangle(transparent, right);
                graphics.FillRectangle(transparent, crossbar);
            }
            else
            {
                using var fill = new SolidBrush(foreground);
                graphics.FillRectangle(fill, outer.Left, outer.Top, outer.Width, stroke);
                graphics.FillRectangle(fill, outer.Left, outer.Bottom - stroke, outer.Width, stroke);
                graphics.FillRectangle(fill, outer.Left, outer.Top, stroke, outer.Height);
                graphics.FillRectangle(fill, outer.Right - stroke, outer.Top, stroke, outer.Height);
                graphics.FillRectangle(fill, left);
                graphics.FillRectangle(fill, right);
                graphics.FillRectangle(fill, crossbar);
            }

            if (updateAvailable)
                DrawUpdateDot(graphics, size, scale, stroke);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    /// <summary>
    /// Рисует точку в правом верхнем углу. Под точкой значок стирается
    /// начисто: иначе рамка приложения просвечивала бы сквозь сглаженный
    /// край и точка теряла бы форму на светлой теме.
    /// </summary>
    private static void DrawUpdateDot(Graphics graphics, int size, float scale, float stroke)
    {
        var diameter = Math.Max(5f, MathF.Round(5f * scale));
        var dot = new RectangleF(size - diameter, 0, diameter, diameter);
        var halo = RectangleF.Inflate(dot, stroke * 0.75f, stroke * 0.75f);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingMode = CompositingMode.SourceCopy;
        using (var transparent = new SolidBrush(Color.Transparent))
            graphics.FillEllipse(transparent, halo);
        graphics.CompositingMode = CompositingMode.SourceOver;
        using var accent = new SolidBrush(UpdateAccent);
        graphics.FillEllipse(accent, dot);
    }
}
