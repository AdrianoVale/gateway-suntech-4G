using System.Collections.Concurrent;
using System.Globalization;
using GatewaySunteh4G_NET8.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GatewaySunteh4G_NET8.Services.Logging;

public sealed class DailyFileLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, DailyFileLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly DailyFileLogWriter _writer;

    public DailyFileLoggerProvider(IOptions<GatewayOptions> options)
    {
        _writer = new DailyFileLogWriter(options.Value.FileLogging);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new DailyFileLogger(name, _writer));
    }

    public void Dispose()
    {
        _writer.Dispose();
        _loggers.Clear();
    }

    private sealed class DailyFileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly DailyFileLogWriter _writer;

        public DailyFileLogger(string categoryName, DailyFileLogWriter writer)
        {
            _categoryName = categoryName;
            _writer = writer;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            _writer.Write(logLevel, _categoryName, eventId, message, exception);
        }
    }

    private sealed class DailyFileLogWriter : IDisposable
    {
        private readonly object _lock = new();
        private readonly string _baseDirectory;
        private readonly string _currentFilePath;
        private readonly string _archivedDirectory;
        private readonly string _archiveFilePrefix;
        private readonly int _retentionDays;
        private DateTime _activeDate;
        private StreamWriter? _writer;

        public DailyFileLogWriter(FileLoggingOptions options)
        {
            _baseDirectory = Path.GetFullPath(options.Directory, AppContext.BaseDirectory);
            _currentFilePath = Path.Combine(_baseDirectory, options.CurrentFileName);
            _archivedDirectory = Path.Combine(_baseDirectory, options.ArchivedDirectoryName);
            _archiveFilePrefix = options.ArchiveFilePrefix;
            _retentionDays = options.RetentionDays;

            Directory.CreateDirectory(_baseDirectory);
            Directory.CreateDirectory(_archivedDirectory);

            _activeDate = DateTime.Today;
            RotateStartupIfNeeded();
            OpenWriter(append: true);
            CleanupArchives(DateTime.Today);
        }

        public void Write(LogLevel level, string category, EventId eventId, string message, Exception? exception)
        {
            lock (_lock)
            {
                RotateIfNeeded(DateTime.Today);

                var eventToken = eventId.Id == 0 ? string.Empty : $" ({eventId.Id})";
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {category}{eventToken}: {message}";
                _writer!.WriteLine(line);
                if (exception is not null)
                {
                    _writer.WriteLine(exception);
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }

        private void RotateStartupIfNeeded()
        {
            if (!File.Exists(_currentFilePath))
            {
                return;
            }

            var lastWriteDate = File.GetLastWriteTime(_currentFilePath).Date;
            if (lastWriteDate < DateTime.Today)
            {
                MoveCurrentToArchive(lastWriteDate);
            }
        }

        private void RotateIfNeeded(DateTime today)
        {
            if (_activeDate >= today)
            {
                return;
            }

            _writer?.Dispose();
            _writer = null;
            MoveCurrentToArchive(_activeDate);
            _activeDate = today;
            OpenWriter(append: false);
            CleanupArchives(today);
        }

        private void MoveCurrentToArchive(DateTime date)
        {
            if (!File.Exists(_currentFilePath))
            {
                return;
            }

            var archivedFileName = $"{_archiveFilePrefix}-{date:yyyyMMdd}.log";
            var archivedPath = Path.Combine(_archivedDirectory, archivedFileName);
            if (File.Exists(archivedPath))
            {
                using var source = new StreamReader(_currentFilePath);
                using var destination = new StreamWriter(archivedPath, append: true);
                destination.Write(source.ReadToEnd());
                File.Delete(_currentFilePath);
                return;
            }

            File.Move(_currentFilePath, archivedPath);
        }

        private void CleanupArchives(DateTime today)
        {
            var threshold = today.AddDays(-_retentionDays);
            var pattern = $"{_archiveFilePrefix}-*.log";
            foreach (var file in Directory.EnumerateFiles(_archivedDirectory, pattern, SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var dateToken = fileName[(fileName.LastIndexOf('-') + 1)..];
                if (!DateTime.TryParseExact(dateToken, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var logDate))
                {
                    continue;
                }

                if (logDate < threshold)
                {
                    File.Delete(file);
                }
            }
        }

        private void OpenWriter(bool append)
        {
            var stream = new FileStream(_currentFilePath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(stream)
            {
                AutoFlush = true
            };
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
