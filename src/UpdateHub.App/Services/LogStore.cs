using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using UpdateHub.Core.Services;

namespace UpdateHub.App.Services;

public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message)
{
    public string Time => Timestamp.ToString("HH:mm:ss");

    public string LevelLabel => Level switch
    {
        LogLevel.Trace or LogLevel.Debug => "AYRINTI",
        LogLevel.Information => "BİLGİ",
        LogLevel.Warning => "UYARI",
        LogLevel.Error => "HATA",
        LogLevel.Critical => "KRİTİK",
        _ => string.Empty,
    };

    public string ShortCategory => Category.Contains('.') ? Category[(Category.LastIndexOf('.') + 1)..] : Category;
}

/// <summary>
/// Keeps the most recent log lines for the Log page and mirrors everything to a daily file under
/// %LOCALAPPDATA%\UpdateHub\logs so users can attach it to a bug report.
/// </summary>
public sealed class LogStore : IDisposable
{
    private readonly object _fileLock = new();
    private readonly StreamWriter? _writer;
    private int _maxEntries = 2000;

    public LogStore()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            FilePath = Path.Combine(AppPaths.LogDirectory, $"updatehub-{DateTime.Now:yyyyMMdd}.log");
            _writer = new StreamWriter(FilePath, append: true, Encoding.UTF8) { AutoFlush = true };
        }
        catch (Exception)
        {
            _writer = null;
        }
    }

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public string? FilePath { get; }

    public int MaxEntries
    {
        get => _maxEntries;
        set => _maxEntries = Math.Max(100, value);
    }

    public void Add(LogEntry entry)
    {
        lock (_fileLock)
        {
            _writer?.WriteLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] {entry.Category}: {entry.Message}");
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            Entries.Add(entry);
            while (Entries.Count > _maxEntries)
            {
                Entries.RemoveAt(0);
            }
        });
    }

    public void Clear() => Entries.Clear();

    public void Dispose() => _writer?.Dispose();
}

public sealed class LogStoreLoggerProvider : ILoggerProvider
{
    private readonly LogStore _store;

    public LogStoreLoggerProvider(LogStore store)
    {
        _store = store;
    }

    public ILogger CreateLogger(string categoryName) => new StoreLogger(_store, categoryName);

    public void Dispose()
    {
    }

    private sealed class StoreLogger : ILogger
    {
        private readonly LogStore _store;
        private readonly string _category;

        public StoreLogger(LogStore store, string category)
        {
            _store = store;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (exception is not null)
            {
                message += $" — {exception.GetType().Name}: {exception.Message}";
            }

            _store.Add(new LogEntry(DateTimeOffset.Now, logLevel, _category, message));
        }
    }
}
