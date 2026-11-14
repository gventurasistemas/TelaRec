using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelaRec.App.Wpf.ViewModels;
using TelaRec.Core.Contracts;
using TelaRec.Infrastructure.Persistence;
using TelaRec.Infrastructure.Recording;
using TelaRec.Infrastructure.Reliability;
using TelaRec.Core.Reliability;
using TelaRec.Core.Scheduling;

namespace TelaRec.App.Wpf;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IVideoCaptureService, WindowsGraphicsCaptureService>();
        services.AddSingleton<IVideoEncoderService, MediaFoundationVideoEncoderService>();
        services.AddSingleton<IFrameScaler, BgraFrameScaler>();
        services.AddSingleton<IVideoFrameComposer, VideoFrameComposer>();
        services.AddSingleton<IWebcamCaptureService, OpenCvWebcamCaptureService>();
        services.AddSingleton<IRecordingExporter, FfmpegRecordingExporter>();
        services.AddSingleton<IRecordingExporterFactory, RecordingExporterFactory>();
        services.AddSingleton<IAudioCaptureService, WasapiLoopbackAudioCaptureService>();
        services.AddSingleton<IScreenshotService, ScreenshotService>();
        services.AddSingleton<IRecordingService, RecordingService>();
        services.AddSingleton<IRecordingSessionJournal, JsonRecordingSessionJournal>();
        services.AddSingleton<IRecordingDiagnostics, RecordingDiagnosticsService>();
        services.AddSingleton<IRecordingAvailabilityProbe, WindowsRecordingAvailabilityProbe>();
        services.AddSingleton<IRecordingScheduler, RecordingScheduler>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        _serviceProvider = services.BuildServiceProvider();
        var recovery = _serviceProvider.GetRequiredService<IRecordingSessionJournal>().RecoverAsync().GetAwaiter().GetResult();
        var diagnostics = _serviceProvider.GetRequiredService<IRecordingDiagnostics>();
        foreach (var message in recovery.Diagnostics) diagnostics.Report(DiagnosticSeverity.Warning, "recovery.session", message);
        _serviceProvider.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
