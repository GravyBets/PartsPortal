namespace PartsPortal.Services;

public static class UpdateLog
{
    private static readonly object Gate = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PartsPortal",
        "update.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var line =
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}";

            if (exception is not null)
                line += $" {exception.GetType().Name}: {exception.Message}";

            lock (Gate)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never interfere with app startup.
        }
    }
}
