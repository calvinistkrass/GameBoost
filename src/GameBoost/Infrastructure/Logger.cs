using System.Text;

namespace GameBoost.Infrastructure;

/// <summary>
/// Einfacher, threadsicherer Datei-Logger. Schreibt nach %AppData%\GameBoost\logs\gameboost-JJJJ-MM-TT.log.
/// Wirft selbst niemals Ausnahmen.
/// </summary>
public static class Logger
{
    private static readonly object Gate = new();

    public static string CurrentLogFile =>
        Path.Combine(AppPaths.LogDirectory, $"gameboost-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string message) => Write("INFO ", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WARN ", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
              .Append(" [").Append(level).Append("] [T")
              .Append(Environment.CurrentManagedThreadId).Append("] ")
              .Append(message);
            if (ex != null) sb.AppendLine().Append(ex);
            sb.AppendLine();

            var line = sb.ToString();
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);
                File.AppendAllText(CurrentLogFile, line, Encoding.UTF8);
            }
            System.Diagnostics.Debug.Write(line);
        }
        catch
        {
            // Logging darf die App unter keinen Umständen zum Absturz bringen.
        }
    }

    /// <summary>Löscht Logdateien, die älter als <paramref name="keepDays"/> Tage sind.</summary>
    public static void CleanupOldLogs(int keepDays = 14)
    {
        try
        {
            if (!Directory.Exists(AppPaths.LogDirectory)) return;
            var limit = DateTime.Now.AddDays(-keepDays);
            foreach (var file in Directory.GetFiles(AppPaths.LogDirectory, "gameboost-*.log"))
            {
                if (File.GetLastWriteTime(file) < limit) File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            Warn("Alte Logdateien konnten nicht gelöscht werden", ex);
        }
    }
}
