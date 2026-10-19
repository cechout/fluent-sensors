using System;
using System.IO;
using Windows.Storage;


namespace FluentSensors.WidgetProvider
{
    // lifecycle and failures in LocalState\widget-provider.log; the board gives a provider no other place to report to
    // (cut back to the newest half once it passes MaxBytes)
    public static class Log
    {
        private const long MaxBytes = 256 * 1024;

        private static readonly object _gate = new();
        private static readonly string? _path = ResolvePath();

        public static void Write(string line)
        {
            if (_path == null) return;

            lock (_gate)
            {
                try
                {
                    var file = new FileInfo(_path);
                    if (file.Exists && file.Length > MaxBytes)
                    {
                        string text = File.ReadAllText(_path);
                        File.WriteAllText(_path, text[(text.Length / 2)..]);
                    }

                    File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
                }
                catch { /* a log that cannot be written is no reason to stop the provider */ }
            }
        }

        private static string? ResolvePath()
        {
            try
            {
                return Path.Combine(ApplicationData.Current.LocalFolder.Path, "widget-provider.log");
            }
            catch
            {
                return null; // no package identity
            }
        }
    }
}
