using Avalonia.Headless.XUnit;
using Drawie.Numerics;
using PixiEditor.Models.Layers;

namespace PixiEditor.Tests;

/// <summary>
/// Covers the atlas layout: equal cells sized to the biggest sprite, a grid as square as it can
/// be, and the gap landing between cells rather than around the outside.
/// </summary>
public class SpriteAtlasTests : FullPixiEditorTest
{
    [AvaloniaFact]
    public void CellsAreSizedToTheBiggestSpriteAndSpacedBetween()
    {
        SpriteAtlas.Sprite[] sprites =
        [
            new(Guid.NewGuid(), new RectI(0, 0, 8, 4)),
            new(Guid.NewGuid(), new RectI(20, 20, 6, 10)),
        ];

        (VecI size, var placements) = SpriteAtlas.Plan(sprites, spacing: 2);

        // Two sprites make a 2x1 grid of 8x10 cells with one 2px gap between them.
        Assert.Equal(new VecI(18, 10), size);
        Assert.Equal(new VecI(0, 0), placements[0].Destination);
        Assert.Equal(new VecI(10, 0), placements[1].Destination);
    }

    [AvaloniaFact]
    public void TheGridIsAsSquareAsItCanBe()
    {
        SpriteAtlas.Sprite[] sprites = Enumerable.Range(0, 9)
            .Select(_ => new SpriteAtlas.Sprite(Guid.NewGuid(), new RectI(0, 0, 10, 10)))
            .ToArray();

        (VecI size, var placements) = SpriteAtlas.Plan(sprites, spacing: 0);

        Assert.Equal(new VecI(30, 30), size);
        Assert.Equal(new VecI(0, 0), placements[0].Destination);
        Assert.Equal(new VecI(20, 20), placements[8].Destination);
    }

    [AvaloniaFact]
    public void TheGapNeverRunsRoundTheOutside()
    {
        SpriteAtlas.Sprite[] sprites =
        [
            new(Guid.NewGuid(), new RectI(0, 0, 5, 5)),
        ];

        (VecI size, var placements) = SpriteAtlas.Plan(sprites, spacing: 7);

        // One sprite, no neighbours, so no gap anywhere.
        Assert.Equal(new VecI(5, 5), size);
        Assert.Equal(new VecI(0, 0), placements[0].Destination);
    }

    [AvaloniaFact]
    public void EachSpriteKeepsTheRegionItWasTrimmedTo()
    {
        RectI awkward = new(13, 27, 6, 9);
        SpriteAtlas.Sprite[] sprites = [new(Guid.NewGuid(), awkward)];

        (_, var placements) = SpriteAtlas.Plan(sprites, spacing: 3);

        Assert.Equal(awkward, placements[0].Source);
    }

    [AvaloniaFact]
    public void NothingToPackGivesNothingBack()
    {
        (VecI size, var placements) = SpriteAtlas.Plan([], spacing: 4);

        Assert.Equal(VecI.Zero, size);
        Assert.Empty(placements);
    }
}
