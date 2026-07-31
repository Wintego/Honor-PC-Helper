namespace HonorPCHelper;

internal static class AppLog
{
    private const long MaxFileSize = 1024 * 1024;
    private static readonly object SyncRoot = new();

    internal static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HonorPCHelper",
        "HonorPCHelper.log");

    internal static void Info(string message) => Write("INFO", message, null);

    internal static void Error(string message, Exception? exception = null)
        => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxFileSize)
                    file.Delete();

                var details = exception is null ? string.Empty : $" | {exception}";
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{details}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
