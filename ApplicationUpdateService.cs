using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HonorPCHelper;

internal sealed record ApplicationUpdate(
    Version Version,
    Uri DownloadUrl,
    Uri ReleasePageUrl,
    long Size,
    string? Sha256);

internal sealed record ApplicationUpdateCheck(
    Version? LatestVersion,
    ApplicationUpdate? Update);

internal sealed class ApplicationUpdateService
{
    private static readonly Uri LatestReleaseApi =
        new("https://api.github.com/repos/Wintego/honor-pc-helper/releases/latest");

    private static readonly HttpClient Http = CreateHttpClient();

    internal Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

    internal async Task<ApplicationUpdateCheck> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var checkCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        checkCancellation.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await Http.GetAsync(LatestReleaseApi, checkCancellation.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(checkCancellation.Token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: checkCancellation.Token);
        var root = document.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        if (!TryParseVersion(tag, out var latestVersion))
            return new ApplicationUpdateCheck(null, null);
        if (latestVersion <= CurrentVersion)
            return new ApplicationUpdateCheck(latestVersion, null);

        if (!root.TryGetProperty("assets", out var assets))
            return new ApplicationUpdateCheck(latestVersion, null);
        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var name)
                || !string.Equals(name.GetString(), "HonorPCHelper.exe", StringComparison.OrdinalIgnoreCase)
                || !asset.TryGetProperty("browser_download_url", out var downloadText)
                || !Uri.TryCreate(downloadText.GetString(), UriKind.Absolute, out var downloadUrl))
                continue;

            var pageText = root.TryGetProperty("html_url", out var page) ? page.GetString() : null;
            var releasePage = Uri.TryCreate(pageText, UriKind.Absolute, out var pageUrl)
                ? pageUrl
                : new Uri("https://github.com/Wintego/honor-pc-helper/releases/latest");
            var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var value)
                ? value
                : 0;
            var digest = asset.TryGetProperty("digest", out var digestElement)
                ? digestElement.GetString()
                : null;
            var sha256 = digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true
                ? digest[7..]
                : null;
            return new ApplicationUpdateCheck(latestVersion,
                new ApplicationUpdate(latestVersion, downloadUrl, releasePage, size, sha256));
        }
        return new ApplicationUpdateCheck(latestVersion, null);
    }

    internal async Task DownloadAndRestartAsync(
        ApplicationUpdate update,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HonorPCHelper", "AppUpdates", update.Version.ToString());
        Directory.CreateDirectory(updateDirectory);
        var downloadedPath = Path.Combine(updateDirectory, "HonorPCHelper.exe");

        using (var response = await Http.GetAsync(update.DownloadUrl,
                   HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? update.Size;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(
                downloadedPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
                if (total > 0)
                    progress?.Report((int)Math.Clamp(written * 100 / total, 0, 100));
            }
        }

        await ValidateDownloadedApplicationAsync(downloadedPath, update, cancellationToken);
        ScheduleReplacement(downloadedPath);
        Application.Exit();
    }

    private static async Task ValidateDownloadedApplicationAsync(
        string path,
        ApplicationUpdate update,
        CancellationToken cancellationToken)
    {
        if (update.Size > 0 && new FileInfo(path).Length != update.Size)
            throw new InvalidDataException(L.T(
                "Размер загруженного обновления не совпадает с размером файла релиза.",
                "The downloaded update size does not match the release asset.",
                "下载的更新大小与发布文件不匹配。"));

        using (var stream = File.OpenRead(path))
        {
            if (stream.Length < 1024 * 1024 || stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
                throw new InvalidDataException(L.T(
                    "Загруженный файл обновления повреждён.",
                    "The downloaded update file is invalid.",
                    "下载的更新文件无效。"));
        }

        if (!string.IsNullOrWhiteSpace(update.Sha256))
        {
            await using var stream = File.OpenRead(path);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!actualHash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L.T(
                    "Контрольная сумма обновления приложения не совпадает.",
                    "The application update checksum does not match.",
                    "应用程序更新校验和不匹配。"));
        }

        var fileVersionText = FileVersionInfo.GetVersionInfo(path).FileVersion;
        if (!TryParseVersion(fileVersionText, out var fileVersion)
            || fileVersion.Major != update.Version.Major
            || fileVersion.Minor != update.Version.Minor
            || fileVersion.Build != update.Version.Build)
            throw new InvalidDataException(L.T(
                "Версия загруженного приложения не совпадает с версией релиза.",
                "The downloaded application version does not match the release.",
                "下载的应用程序版本与发布版本不匹配。"));
    }

    private static void ScheduleReplacement(string downloadedPath)
    {
        var targetPath = Environment.ProcessPath
            ?? throw new InvalidOperationException(L.T(
                "Не удалось определить путь к приложению.",
                "The application path is unavailable.",
                "无法确定应用程序路径。"));
        if (!string.Equals(Path.GetExtension(targetPath), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.T(
                "Автообновление доступно только для собранного exe-файла.",
                "Automatic update is available only for the packaged exe.",
                "自动更新仅适用于打包后的 exe 文件。"));

        static string PsLiteral(string value) => "'" + value.Replace("'", "''") + "'";
        var script = "$ErrorActionPreference='Stop';"
            + $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue;"
            + $"Copy-Item -LiteralPath {PsLiteral(downloadedPath)} -Destination {PsLiteral(targetPath)} -Force;"
            + $"Start-Process -FilePath {PsLiteral(targetPath)}";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException(L.T(
                "Не удалось запустить обновление приложения.",
                "Could not start the application updater.",
                "无法启动应用程序更新程序。"));
    }

    private static bool TryParseVersion(string? text, out Version version)
    {
        var value = (text ?? string.Empty).Trim().TrimStart('v', 'V');
        var suffix = value.IndexOfAny(['-', '+', ' ']);
        if (suffix >= 0)
            value = value[..suffix];
        return Version.TryParse(value, out version!);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HonorPCHelper/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
