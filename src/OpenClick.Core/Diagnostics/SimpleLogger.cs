namespace OpenClick.Core.Diagnostics;

/// <summary>Minimal thread-safe file logger. No I/O ever happens on the mouse-hook path.</summary>
public sealed class SimpleLogger : IDisposable
{
    private readonly object _lock = new();
    private readonly string _path;
    private bool _disposed;

    public SimpleLogger(string path)
    {
        _path = path;
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); } catch { }
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    private void Write(string level, string message)
    {
        lock (_lock)
        {
            if (_disposed) return;
            try
            {
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(_path, line);
                // Bound log size (~1 MB, keep tail).
                var fi = new FileInfo(_path);
                if (fi.Exists && fi.Length > 1024 * 1024)
                {
                    var lines = File.ReadAllLines(_path);
                    File.WriteAllLines(_path, lines[^2000..]);
                }
            }
            catch { /* logging must never throw */ }
        }
    }

    public void Dispose()
    {
        lock (_lock) _disposed = true;
    }
}
