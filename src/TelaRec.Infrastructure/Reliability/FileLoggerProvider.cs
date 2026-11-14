using System.Text;
using Microsoft.Extensions.Logging;

namespace TelaRec.Infrastructure.Reliability;

/// <summary>
/// Provider de log diário em arquivo, sem dependências externas.
/// Falhas de escrita são ignoradas para nunca interromper o aplicativo.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly object _sync = new();
    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private readonly int _retentionDays;
    private bool _disposed;

    public FileLoggerProvider(string? directory = null, LogLevel minimumLevel = LogLevel.Information, int retentionDays = 14)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TelaRec",
            "logs");
        _minimumLevel = minimumLevel;
        _retentionDays = Math.Max(1, retentionDays);
        TryPrepareDirectory();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    private void TryPrepareDirectory()
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);
            foreach (var path in Directory.EnumerateFiles(_directory, "telarec-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception)
        {
            // Logging nunca deve impedir a inicialização do aplicativo.
        }
    }

    internal void Write(string category, LogLevel level, EventId eventId, string message, Exception? exception)
    {
        if (level < _minimumLevel) return;

        var builder = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture))
            .Append(" [").Append(LevelText(level)).Append("] ")
            .Append(category).Append(": ").Append(message);
        if (eventId.Id != 0) builder.Append(" (evento ").Append(eventId.Id).Append(')');
        if (exception is not null) builder.AppendLine().Append(exception);

        var path = Path.Combine(_directory, $"telarec-{DateTime.Now:yyyyMMdd}.log");
        lock (_sync)
        {
            if (_disposed) return;
            try
            {
                File.AppendAllText(path, builder.ToString() + Environment.NewLine, Utf8WithoutBom);
            }
            catch (Exception)
            {
                // Falha de I/O em log não pode afetar a gravação nem a UI.
            }
        }
    }

    private static string LevelText(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERRO",
        LogLevel.Critical => "CRIT",
        _ => "LOG"
    };

    public void Dispose()
    {
        lock (_sync) _disposed = true;
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= provider._minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            provider.Write(category, logLevel, eventId, formatter(state, exception), exception);
        }
    }
}
