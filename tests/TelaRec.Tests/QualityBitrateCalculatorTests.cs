using TelaRec.Core.Recording;

namespace TelaRec.Tests;

public sealed class QualityBitrateCalculatorTests
{
    [Fact]
    public void Higher_quality_suggests_a_higher_bitrate()
    {
        var high = QualityBitrateCalculator.Suggest("Alta", 1920, 1080, 30);
        var medium = QualityBitrateCalculator.Suggest("Média", 1920, 1080, 30);
        var low = QualityBitrateCalculator.Suggest("Baixa", 1920, 1080, 30);

        Assert.True(high > medium);
        Assert.True(medium > low);
    }

    [Fact]
    public void Bitrate_scales_with_resolution_and_fps()
    {
        var reference = QualityBitrateCalculator.Suggest("Alta", 1920, 1080, 30);
        var faster = QualityBitrateCalculator.Suggest("Alta", 1920, 1080, 60);
        var smaller = QualityBitrateCalculator.Suggest("Alta", 1280, 720, 30);

        Assert.True(faster > reference);
        Assert.True(smaller < reference);
    }

    [Fact]
    public void Accent_insensitive_quality_names_are_accepted()
    {
        Assert.Equal(
            QualityBitrateCalculator.Suggest("Média", 1280, 720, 30),
            QualityBitrateCalculator.Suggest("media", 1280, 720, 30));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(320, 240, 15)]
    public void Sugestion_is_clamped_to_supported_range(int width, int height, int fps)
    {
        var value = QualityBitrateCalculator.Suggest("Alta", width, height, fps);
        Assert.InRange(value, QualityBitrateCalculator.MinimumBitrate, QualityBitrateCalculator.MaximumBitrate);
    }

    [Fact]
    public void Invalid_dimensions_fall_back_to_minimum()
    {
        Assert.Equal(QualityBitrateCalculator.MinimumBitrate, QualityBitrateCalculator.Suggest("Alta", 0, 0, 30));
    }
}
