using PixiEditor.Models.Timelapse;

namespace PixiEditor.Tests;

/// <summary>
/// Covers how long a replay rests on its final frame, which is the one piece of the export that is
/// plain arithmetic rather than encoding.
/// </summary>
public class TimelapseHoldTests
{
    private const int Fps = 30;

    [Fact]
    public void ASecondAtThirtyFramesAddsTwentyNine()
    {
        // The last frame is already in the clip once, so a second of it needs twenty-nine more.
        Assert.Equal(29, TimelapseRecorder.HoldFrameCount(1, Fps, 100));
    }

    [Fact]
    public void TwoSecondsAddTwiceAsMany()
    {
        Assert.Equal(59, TimelapseRecorder.HoldFrameCount(2, Fps, 100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NoHoldAddsNothing(double seconds)
    {
        Assert.Equal(0, TimelapseRecorder.HoldFrameCount(seconds, Fps, 100));
    }

    [Fact]
    public void AHoldShorterThanOneFrameAddsNothing()
    {
        Assert.Equal(0, TimelapseRecorder.HoldFrameCount(0.01, Fps, 100));
    }

    [Fact]
    public void ASlowedDownClipHoldsForTheSameTimeNotTheSameFrames()
    {
        // A three frame session plays at a lower rate, so a second of it is fewer frames.
        Assert.Equal(1, TimelapseRecorder.HoldFrameCount(1, 2, 3));
    }

    [Fact]
    public void TheHoldCannotPushTheClipPastTheFrameCeiling()
    {
        int room = 5;
        int held = TimelapseRecorder.HoldFrameCount(10, Fps, TimelapseRecorder.MaxExportFrames - room);

        Assert.Equal(room, held);
    }

    [Fact]
    public void AFullClipTakesNoHoldAtAll()
    {
        Assert.Equal(0, TimelapseRecorder.HoldFrameCount(5, Fps, TimelapseRecorder.MaxExportFrames));
    }

    [Fact]
    public void AnImpossibleFrameRateIsRefusedRatherThanDividedBy()
    {
        Assert.Equal(0, TimelapseRecorder.HoldFrameCount(1, 0, 100));
    }
}
