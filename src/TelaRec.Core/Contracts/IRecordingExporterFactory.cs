using TelaRec.Core.Recording;

namespace TelaRec.Core.Contracts;

public interface IRecordingExporterFactory
{
    IRecordingExporter GetExporter(RecordingProfile profile);
}
