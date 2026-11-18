using TelaRec.App.Wpf.Infrastructure;

namespace TelaRec.Tests;

public sealed class WindowSelectionTests
{
    [Fact]
    public void Selection_policy_rejects_hidden_minimized_empty_and_own_windows()
    {
        const uint ownProcess = 42;

        Assert.False(WindowSelectionPolicy.CanSelect(new DesktopWindow((nint)1, "", true, false, 7), ownProcess));
        Assert.False(WindowSelectionPolicy.CanSelect(new DesktopWindow((nint)1, "Editor", false, false, 7), ownProcess));
        Assert.False(WindowSelectionPolicy.CanSelect(new DesktopWindow((nint)1, "Editor", true, true, 7), ownProcess));
        Assert.False(WindowSelectionPolicy.CanSelect(new DesktopWindow((nint)1, "TelaRec", true, false, ownProcess), ownProcess));
        Assert.True(WindowSelectionPolicy.CanSelect(new DesktopWindow((nint)1, "Editor", true, false, 7), ownProcess));
    }

    [Fact]
    public void Zero_handle_cannot_be_selected_or_revalidated()
    {
        Assert.False(WindowSelectionPolicy.CanSelect(new DesktopWindow(IntPtr.Zero, "Editor"), 42));
        Assert.False(WindowEnumerator.TryGetSelectableWindow(IntPtr.Zero, out _));
    }

    [Fact]
    public void Thumbnail_capture_dimensions_cap_memory_before_bitmap_allocation()
    {
        var size = WindowThumbnailProvider.GetCaptureDimensions(8192, 8192);

        Assert.InRange(size.Width, 1, WindowThumbnailProvider.MaximumCaptureDimension);
        Assert.InRange(size.Height, 1, WindowThumbnailProvider.MaximumCaptureDimension);
        Assert.True(size.Width * (long)size.Height <= WindowThumbnailProvider.MaximumCapturePixels);
    }

    [Fact]
    public void Thumbnail_capture_dimensions_preserve_small_windows()
    {
        Assert.Equal((640, 480), WindowThumbnailProvider.GetCaptureDimensions(640, 480));
    }

    [Fact]
    public async Task Thumbnail_loader_returns_placeholder_when_capture_is_cancelled()
    {
        var started = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var loader = new WindowThumbnailLoader((_, _, _) =>
        {
            started.Set();
            release.Wait();
            return null;
        }, TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();

        var result = loader.CaptureAsync((nint)1, 180, 100, cancellation.Token);
        Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
        cancellation.Cancel();

        Assert.Null(await result);
        release.Set();
    }

    [Fact]
    public async Task Thumbnail_loader_returns_placeholder_when_capture_times_out()
    {
        var release = new ManualResetEventSlim();
        var loader = new WindowThumbnailLoader((_, _, _) =>
        {
            release.Wait();
            return null;
        }, TimeSpan.FromMilliseconds(25));

        var result = await loader.CaptureAsync((nint)1, 180, 100, CancellationToken.None);

        Assert.Null(result);
        release.Set();
    }

    [Fact]
    public async Task Timed_out_captures_keep_their_slots_until_the_native_work_finishes()
    {
        var started = 0;
        var threeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new ManualResetEventSlim();
        var loader = new WindowThumbnailLoader((_, _, _) =>
        {
            if (Interlocked.Increment(ref started) == WindowThumbnailLoader.MaximumConcurrentCaptures)
                threeStarted.TrySetResult();
            release.Wait();
            return null;
        }, TimeSpan.FromMilliseconds(25));

        var firstBatch = Enumerable.Range(0, WindowThumbnailLoader.MaximumConcurrentCaptures)
            .Select(index => loader.CaptureAsync((nint)(index + 1), 180, 100, CancellationToken.None))
            .ToArray();
        await threeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.WhenAll(firstBatch);

        var fourth = loader.CaptureAsync((nint)4, 180, 100, CancellationToken.None);
        await Task.Delay(100);
        Assert.Equal(WindowThumbnailLoader.MaximumConcurrentCaptures, Volatile.Read(ref started));

        release.Set();
        await fourth;
        Assert.Equal(WindowThumbnailLoader.MaximumConcurrentCaptures + 1, Volatile.Read(ref started));
    }
}
