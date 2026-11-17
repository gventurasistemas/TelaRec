using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelaRec.App.Wpf.Infrastructure;
using TelaRec.App.Wpf.Resources;
using TelaRec.App.Wpf.ViewModels;
using TelaRec.Core.Contracts;
using TelaRec.Core.Reliability;
using TelaRec.Core.Recording;
using TelaRec.Core.Scheduling;
using TelaRec.Infrastructure.Persistence;
using TelaRec.Infrastructure.Recording;
using TelaRec.Infrastructure.Reliability;

namespace TelaRec.App.Wpf;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private FileLoggerProvider? _fileLoggerProvider;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        RegisterGlobalExceptionHandlers();
        base.OnStartup(e);
        try
        {
            var services = new ServiceCollection();
            _fileLoggerProvider = new FileLoggerProvider();
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddDebug();
                builder.AddProvider(_fileLoggerProvider);
            });
            services.AddSingleton<ISettingsService, JsonSettingsService>();
            services.AddSingleton<IVideoCaptureService, WindowsGraphicsCaptureService>();
            services.AddSingleton<IVideoEncoderCapabilitiesService, MediaFoundationVideoEncoderCapabilitiesService>();
            services.AddSingleton<IClickCaptureService, WindowsClickCaptureService>();
            services.AddSingleton<IVideoEncoderService, MediaFoundationVideoEncoderService>();
            services.AddSingleton<IFrameScaler, BgraFrameScaler>();
            services.AddSingleton<IVideoFrameComposer, VideoFrameComposer>();
            services.AddSingleton<IWebcamCaptureService, OpenCvWebcamCaptureService>();
            services.AddSingleton<IRecordingExporter, FfmpegRecordingExporter>();
            services.AddSingleton<IRecordingExporterFactory, RecordingExporterFactory>();
            services.AddSingleton<IAudioCaptureService, WasapiLoopbackAudioCaptureService>();
            services.AddSingleton<IMicrophoneCaptureService, WasapiMicrophoneCaptureService>();
            services.AddSingleton<IScreenshotService, ScreenshotService>();
            services.AddSingleton<RecordingService>();
            services.AddSingleton<IRecordingService>(provider => provider.GetRequiredService<RecordingService>());
            services.AddSingleton<IRecordingTelemetrySource>(provider => provider.GetRequiredService<RecordingService>());
            services.AddSingleton<IRecordingSessionJournal, JsonRecordingSessionJournal>();
            services.AddSingleton<IRecordingDiagnostics, RecordingDiagnosticsService>();
            services.AddSingleton<IRecordingAvailabilityProbe, WindowsRecordingAvailabilityProbe>();
            services.AddSingleton<IRecordingScheduler, RecordingScheduler>();
            services.AddSingleton<IScheduledRecordingStore, JsonScheduledRecordingStore>();
            services.AddSingleton(ReleaseIdentity.FromAssembly(typeof(App).Assembly));
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MainWindow>();
            _serviceProvider = services.BuildServiceProvider();
            _logger = _serviceProvider.GetRequiredService<ILogger<App>>();
            var release = _serviceProvider.GetRequiredService<ReleaseIdentity>();
            _logger.LogInformation("TelaRec iniciado. Versão {Version}; commit {Commit}", release.InformationalVersion, release.Commit);
            _serviceProvider.GetRequiredService<IRecordingDiagnostics>().Report(DiagnosticSeverity.Information, "release.identity", $"{release.Product} {release.DisplayVersion}");

            var window = _serviceProvider.GetRequiredService<MainWindow>();
            window.Show();
            // Recuperação fora da thread de inicialização para não bloquear a abertura da janela.
            _ = Dispatcher.InvokeAsync(RecoverSessionsAsync, DispatcherPriority.ApplicationIdle);
        }
        catch (Exception exception)
        {
            ReportFatal(exception, "Falha ao iniciar o TelaRec.");
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception, "Exceção não tratada na interface");
        e.Handled = ExceptionPolicy.IsRecoverable(e.Exception);
        try
        {
            MessageBox.Show(
                Strings.Get("UnexpectedErrorMessage"),
                Strings.Get("UnexpectedErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // A MessageBox pode falhar durante o encerramento; o log já foi gravado.
        }
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception
            ?? new InvalidOperationException("Erro desconhecido sem exceção associada.");
        LogError(exception, $"Exceção fatal não tratada (encerrando: {e.IsTerminating})");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (_logger is not null)
        {
            ExceptionPolicy.ReportUnobservedTask(_logger, e);
            return;
        }

        e.SetObserved();
    }

    private async Task RecoverSessionsAsync()
    {
        var provider = _serviceProvider;
        if (provider is null) return;
        try
        {
            var journal = provider.GetRequiredService<IRecordingSessionJournal>();
            var diagnostics = provider.GetRequiredService<IRecordingDiagnostics>();
            var recovery = await journal.RecoverAsync().ConfigureAwait(true);
            foreach (var message in recovery.Diagnostics)
                diagnostics.Report(DiagnosticSeverity.Warning, "recovery.session", message);
            _logger?.LogInformation(
                "Recuperação de sessões: {SessionsFound} encontradas, {TemporaryFilesRemoved} temporários removidos.",
                recovery.SessionsFound,
                recovery.TemporaryFilesRemoved);
        }
        catch (Exception exception)
        {
            LogError(exception, "Falha ao recuperar sessões anteriores");
        }
    }

    private void LogError(Exception exception, string message)
    {
        if (_logger is not null) _logger.LogError(exception, message);
        else Trace.WriteLine($"{message}{Environment.NewLine}{exception}");
    }

    private void ReportFatal(Exception exception, string message)
    {
        LogError(exception, message);
        try
        {
            MessageBox.Show(
                $"{Strings.Get("UnexpectedErrorMessage")}{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                Strings.Get("UnexpectedErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // Sem UI disponível; o log é a única saída.
        }
        Shutdown(-1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _logger?.LogInformation("TelaRec encerrando.");
            var provider = Interlocked.Exchange(ref _serviceProvider, null);
            if (provider is not null)
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            LogError(exception, "Falha ao liberar recursos na saída");
        }
        finally
        {
            _fileLoggerProvider?.Dispose();
            _fileLoggerProvider = null;
            base.OnExit(e);
        }
    }
}
