using Microsoft.Extensions.Logging;
using TelaRec.Infrastructure.Reliability;

namespace TelaRec.Tests;

public sealed class ExceptionPolicyTests
{
    [Fact]
    public void Unobserved_task_exception_is_reported_and_marked_observed()
    {
        var logger = new RecordingLogger();
        var exception = new InvalidOperationException("falha em task");
        var args = new UnobservedTaskExceptionEventArgs(new AggregateException(exception));

        ExceptionPolicy.ReportUnobservedTask(logger, args);

        Assert.True(args.Observed);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(args.Exception, entry.Exception);
        Assert.Contains("falha em task", entry.Exception!.ToString());
        Assert.Contains("não observada", entry.Message);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException), true)]
    [InlineData(typeof(OutOfMemoryException), false)]
    public void Recoverability_matches_exception_type(Type type, bool expected)
    {
        var exception = (Exception)Activator.CreateInstance(type)!;
        Assert.Equal(expected, ExceptionPolicy.IsRecoverable(exception));
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
