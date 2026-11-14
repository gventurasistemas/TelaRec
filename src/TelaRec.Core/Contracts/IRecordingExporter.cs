using TelaRec.Core.Recording;
namespace TelaRec.Core.Contracts;
public interface IRecordingExporter
{
    bool CanExport(RecordingProfile profile);
    Task ExportAsync(string sourcePath, string destinationPath, RecordingProfile profile, CancellationToken cancellationToken = default);
}
