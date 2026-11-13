using Drawie.Numerics;

namespace PixiEditor.Models.Layers;

/// <summary>
/// Works out where each layer goes in a sprite atlas. Kept apart from the drawing so the awkward
/// part - the arithmetic - can be checked without a document.
/// </summary>
internal static class SpriteAtlas
{
    /// <summary>One layer and the part of the canvas its pixels actually occupy.</summary>
    public readonly record struct Sprite(Guid Layer, RectI Bounds);

    /// <summary>Where one sprite's pixels are copied from, and where they land in the atlas.</summary>
    public readonly record struct Placement(Guid Layer, RectI Source, VecI Destination);

    /// <summary>
    /// Lays the sprites out in a grid of equal cells, each cell as big as the largest sprite, with
    /// <paramref name="spacing"/> pixels of gap between them.
    /// </summary>
    /// <remarks>
    /// Equal cells rather than a tight pack: a game reading this back wants a predictable stride,
    /// and a packer's cleverness is wasted if every frame has to be looked up by hand. The grid is
    /// as square as it can be, so an atlas of nine sprites is three by three rather than a strip.
    /// </remarks>
    public static (VecI Size, List<Placement> Placements) Plan(IReadOnlyList<Sprite> sprites, int spacing)
    {
        if (sprites.Count == 0)
        {
            return (VecI.Zero, []);
        }

        spacing = Math.Max(0, spacing);

        int cellWidth = sprites.Max(sprite => sprite.Bounds.Width);
        int cellHeight = sprites.Max(sprite => sprite.Bounds.Height);

        int columns = (int)Math.Ceiling(Math.Sqrt(sprites.Count));
        int rows = (int)Math.Ceiling(sprites.Count / (double)columns);

        VecI size = new(
            (columns * cellWidth) + ((columns - 1) * spacing),
            (rows * cellHeight) + ((rows - 1) * spacing));

        List<Placement> placements = new(sprites.Count);
        for (int i = 0; i < sprites.Count; i++)
        {
            int column = i % columns;
            int row = i / columns;

            placements.Add(new Placement(
                sprites[i].Layer,
                sprites[i].Bounds,
                new VecI(column * (cellWidth + spacing), row * (cellHeight + spacing))));
        }

        return (size, placements);
    }
}
