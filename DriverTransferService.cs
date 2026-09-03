using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace HonorPCHelper;

internal sealed record DriverTransferResult(
    bool Succeeded,
    int PackageCount,
    bool RebootRequired,
    string? Error);

/// <summary>
/// Экспорт и импорт сторонних драйверов Windows через pnputil.
///
/// Экспорт выгружает хранилище драйверов целиком во временный каталог и
/// упаковывает его в один zip: это резервная копия, которая переживает
/// переустановку системы, даже если каталоги HONOR к тому времени перестанут
/// отдавать пакеты для модели. Импорт распаковывает такой архив и возвращает
/// пакеты в хранилище с установкой на устройства.
///
/// Обе операции требуют прав администратора, поэтому выполняются не в самом
/// приложении, а в дочернем процессе, поднятом через UAC. Своих окон дочерний
/// процесс не показывает: итог он кладёт в файл, который читает родитель.
/// </summary>
internal static class DriverTransferService
{
    internal const string ExportArgument = "--export-drivers";
    internal const string ImportArgument = "--import-drivers";

    // Итог лежит в общем для всех учётных записей каталоге: запрос UAC может
    // быть подтверждён другим администратором, чей %LocalAppData% приложению
    // недоступен.
    private static readonly string ResultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "HonorPCHelper", "driver-transfer.json");

    private const int RebootRequiredExitCode = 3010;

    internal static Task<DriverTransferResult> ExportAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
        => RunElevatedAsync(ExportArgument, archivePath, cancellationToken);

    internal static Task<DriverTransferResult> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
        => RunElevatedAsync(ImportArgument, sourcePath, cancellationToken);

    private static async Task<DriverTransferResult> RunElevatedAsync(
        string argument,
        string path,
        CancellationToken cancellationToken)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException(L.T(
                "Не удалось определить путь к HonorPCHelper.exe.",
                "Could not determine the path to HonorPCHelper.exe.",
                "无法确定 HonorPCHelper.exe 的路径。"));

        TryDelete(ResultPath);
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Verb = "runas"
        };
        startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add(path);

        AppLog.Info($"Starting elevated driver transfer: {argument} {path}");
        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(L.T(
                    "Не удалось запустить перенос драйверов.",
                    "Could not start the driver transfer.",
                    "无法启动驱动程序传输。"));
            await process.WaitForExitAsync(cancellationToken);
            return ReadResult() ?? new DriverTransferResult(false, 0, false, L.T(
                $"Перенос драйверов завершился с кодом {process.ExitCode}.",
                $"The driver transfer exited with code {process.ExitCode}.",
                $"驱动程序传输以代码 {process.ExitCode} 结束。"));
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            // Пользователь отменил запрос UAC — молча выходим.
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private static DriverTransferResult? ReadResult()
    {
        try
        {
            if (!File.Exists(ResultPath))
                return null;
            using var stream = File.OpenRead(ResultPath);
            return JsonSerializer.Deserialize<DriverTransferResult>(stream);
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read the driver transfer result", exception);
            return null;
        }
        finally
        {
            TryDelete(ResultPath);
        }
    }

    /// <summary>Дочерний процесс: выгружает хранилище драйверов в архив.</summary>
    internal static int Export(string? archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
            return 2;

        var staging = CreateStagingDirectory("DriverExport");
        try
        {
            var (exitCode, output) = RunPnpUtil("/export-driver", "*", staging);
            var packages = Directory.GetFiles(staging, "*.inf", SearchOption.AllDirectories).Length;
            if (packages == 0)
                return Fail(L.T(
                    "Windows не вернула ни одного стороннего драйвера для выгрузки.",
                    "Windows returned no third-party drivers to export.",
                    "Windows 未返回可导出的第三方驱动程序。") + Detail(exitCode, output));

            var directory = Path.GetDirectoryName(Path.GetFullPath(archivePath));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            TryDelete(archivePath);
            ZipFile.CreateFromDirectory(staging, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
            AppLog.Info($"Exported {packages} driver package(s) to {archivePath}");
            return Succeed(packages, rebootRequired: false);
        }
        catch (Exception exception)
        {
            AppLog.Error("Driver export failed", exception);
            return Fail(exception.Message);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    /// <summary>Дочерний процесс: возвращает драйверы из архива, папки или одного inf.</summary>
    internal static int Import(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return 2;

        string? staging = null;
        try
        {
            string[] arguments;
            int packages;
            if (File.Exists(sourcePath)
                && Path.GetExtension(sourcePath).Equals(".inf", StringComparison.OrdinalIgnoreCase))
            {
                arguments = ["/add-driver", Path.GetFullPath(sourcePath), "/install"];
                packages = 1;
            }
            else
            {
                var directory = sourcePath;
                if (File.Exists(sourcePath))
                {
                    staging = CreateStagingDirectory("DriverImport");
                    ExtractSafely(sourcePath, staging);
                    directory = staging;
                }
                else if (!Directory.Exists(sourcePath))
                {
                    return Fail(L.T(
                        "Файл или папка с драйверами не найдены.",
                        "The driver file or folder was not found.",
                        "未找到驱动程序文件或文件夹。"));
                }

                packages = Directory.GetFiles(directory, "*.inf", SearchOption.AllDirectories).Length;
                if (packages == 0)
                    return Fail(L.T(
                        "В выбранном источнике нет файлов драйверов (*.inf).",
                        "The selected source contains no driver files (*.inf).",
                        "所选来源中没有驱动程序文件 (*.inf)。"));
                arguments = ["/add-driver", Path.Combine(Path.GetFullPath(directory), "*.inf"), "/subdirs", "/install"];
            }

            var (exitCode, output) = RunPnpUtil(arguments);
            var rebootRequired = exitCode == RebootRequiredExitCode;
            if (exitCode != 0 && !rebootRequired)
                return Fail(L.T(
                    "Windows отклонила установку драйверов.",
                    "Windows rejected the driver installation.",
                    "Windows 拒绝了驱动程序安装。") + Detail(exitCode, output));

            AppLog.Info($"Imported {packages} driver package(s) from {sourcePath}");
            return Succeed(packages, rebootRequired);
        }
        catch (Exception exception)
        {
            AppLog.Error("Driver import failed", exception);
            return Fail(exception.Message);
        }
        finally
        {
            if (staging is not null)
                TryDeleteDirectory(staging);
        }
    }

    private static (int ExitCode, string Output) RunPnpUtil(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "pnputil.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        AppLog.Info($"Running pnputil {string.Join(' ', arguments)}");
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(L.T(
                "Не удалось запустить pnputil.",
                "Could not start pnputil.",
                "无法启动 pnputil。"));
        // Оба потока читаются до ожидания выхода: полное хранилище драйверов
        // даёт вывод больше буфера канала, и процесс встал бы на записи.
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        AppLog.Info($"pnputil finished with code {process.ExitCode}");
        return (process.ExitCode, standardOutput.Result + standardError.Result);
    }

    // Хвост вывода pnputil объясняет отказ лучше, чем код возврата, но целиком
    // в сообщение не помещается.
    private static string Detail(int exitCode, string output)
    {
        var detail = string.Join(' ', output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(3));
        return string.IsNullOrWhiteSpace(detail)
            ? $" (pnputil: {exitCode})"
            : $" (pnputil: {exitCode}) {detail}";
    }

    private static int Succeed(int packages, bool rebootRequired)
        => WriteResult(new DriverTransferResult(true, packages, rebootRequired, null)) ? 0 : 1;

    private static int Fail(string error)
    {
        AppLog.Error($"Driver transfer failed: {error}");
        WriteResult(new DriverTransferResult(false, 0, false, error));
        return 1;
    }

    private static bool WriteResult(DriverTransferResult result)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath)!);
            using var stream = File.Create(ResultPath);
            JsonSerializer.Serialize(stream, result);
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not write the driver transfer result", exception);
            return false;
        }
    }

    private static string CreateStagingDirectory(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "HonorPCHelper", $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void ExtractSafely(string archivePath, string destinationDirectory)
    {
        var destinationRoot = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
            if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L.T(
                    "Архив драйверов содержит небезопасный путь.",
                    "The driver archive contains an unsafe path.",
                    "驱动程序存档包含不安全的路径。"));
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) { AppLog.Error($"Could not delete {path}", exception); }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (Exception exception) { AppLog.Error($"Could not delete {path}", exception); }
    }
}
