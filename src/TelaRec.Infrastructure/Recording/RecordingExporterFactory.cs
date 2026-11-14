using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;

namespace TelaRec.Infrastructure.Recording;

public sealed class RecordingExporterFactory(IEnumerable<IRecordingExporter> exporters) : IRecordingExporterFactory
{
    public IRecordingExporter GetExporter(RecordingProfile profile) =>
        exporters.FirstOrDefault(exporter => exporter.CanExport(profile))
        ?? throw new NotSupportedException($"Não há backend para {profile.Container}.");
}
