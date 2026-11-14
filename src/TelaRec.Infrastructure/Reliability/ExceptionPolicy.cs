using Microsoft.Extensions.Logging;

namespace TelaRec.Infrastructure.Reliability;

/// <summary>Políticas de tratamento de exceções compartilhadas pelo aplicativo.</summary>
public static class ExceptionPolicy
{
    /// <summary>Erros considerados recuperáveis na thread de interface.</summary>
    public static bool IsRecoverable(Exception exception) =>
        exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException);

    /// <summary>Registra uma exceção de tarefa não observada e impede que ela derrube o processo.</summary>
    public static void ReportUnobservedTask(ILogger logger, UnobservedTaskExceptionEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(args);
        logger.LogError(args.Exception, "Exceção não observada em uma tarefa.");
        args.SetObserved();
    }
}
