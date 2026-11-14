using Microsoft.Extensions.Logging.Abstractions;
using TelaRec.Infrastructure.Recording;

namespace TelaRec.Tests;

public sealed class WindowsClickCaptureServiceTests
{
    [Fact]
    public void Start_and_stop_are_safe_and_idempotent()
    {
        using var service = new WindowsClickCaptureService(NullLogger<WindowsClickCaptureService>.Instance);

        service.Start();
        service.Stop();
        service.Stop();

        Assert.False(service.IsCapturing);
    }
}
