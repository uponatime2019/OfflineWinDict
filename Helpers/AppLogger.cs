using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace OfflineWinDict.Helpers
{
    public static class AppLogger
    {
        private static readonly string LogFilePath;
        public static bool LoggingEnabled { get; set; } = true;

        static AppLogger()
        {
            try
            {
                var logsDir = Path.Combine(AppPaths.Root, "logs");
                Directory.CreateDirectory(logsDir);

                var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var fileName = $"app_session_{unixTimestamp}.txt";
                LogFilePath = Path.Combine(logsDir, fileName);

                var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] AppLogger initialized at {LogFilePath}";
                File.AppendAllText(LogFilePath, entry + Environment.NewLine);
                Debug.WriteLine(entry);
                Debug.WriteLine($"[Logger] Logs directory: {logsDir}");
            }
            catch (Exception ex)
            {
                LogFilePath = string.Empty;
                Debug.WriteLine($"[Logger Error] Failed to initialize logger: {ex.Message}");
            }
        }

        public static void LogAction(string actionName, Dictionary<string, object>? parameters = null)
        {
            if (!LoggingEnabled) return;
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var entry = $"[{timestamp}] {actionName}";
                if (parameters != null && parameters.Count > 0)
                {
                    var parts = new List<string>();
                    foreach (var kv in parameters)
                    {
                        parts.Add($"{kv.Key}={kv.Value}");
                    }
                    entry += " | " + string.Join(", ", parts);
                }
                if (!string.IsNullOrEmpty(LogFilePath))
                {
                    File.AppendAllText(LogFilePath, entry + Environment.NewLine);
                }
                Debug.WriteLine(entry);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Logger Error] {ex.Message}");
            }
        }

        public static void LogException(Exception ex, string context)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var entry = $"[{timestamp}] EXCEPTION [{context}]: {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}" +
                            $"Stack Trace: {ex.StackTrace}{Environment.NewLine}";
                if (ex.InnerException != null)
                {
                    entry += $"Inner Exception: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}{Environment.NewLine}" +
                             $"Inner Stack Trace: {ex.InnerException.StackTrace}{Environment.NewLine}";
                    if (ex.InnerException.InnerException != null)
                    {
                        entry += $"Inner2 Exception: {ex.InnerException.InnerException.GetType().FullName}: {ex.InnerException.InnerException.Message}{Environment.NewLine}" +
                                 $"Inner2 Stack Trace: {ex.InnerException.InnerException.StackTrace}{Environment.NewLine}";
                    }
                }
                if (!string.IsNullOrEmpty(LogFilePath))
                {
                    File.AppendAllText(LogFilePath, entry + Environment.NewLine);
                }
                Debug.WriteLine(entry);
            }
            catch (Exception logEx)
            {
                Debug.WriteLine("[Logger Error] " + logEx.Message);
            }
        }
    }
}
