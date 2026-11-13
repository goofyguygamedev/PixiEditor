using ChunkyImageLib;using ChunkyImageLib.DataHolders;
using Drawie.Backend.Core;
using Drawie.Backend.Core.Bridge;
using Drawie.Backend.Core.Numerics;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Changeables.Graph.Interfaces;
using PixiEditor.Models.Handlers;
using PixiEditor.UI.Common.Localization;
using PixiEditor.ViewModels.Document;

namespace PixiEditor.Models.Layers;

/// <summary>Draws the atlas <see cref="SpriteAtlas"/> planned and wraps it in a new document.</summary>
internal static class SpriteAtlasRenderer
{
    /// <summary>Returns null when no layer has any pixels, so there is nothing to pack.</summary>
    public static DocumentViewModel? Render(DocumentViewModel document, int spacing)
    {
        int frame = document.AnimationDataViewModel.ActiveFrameBindable;
        var readOnly = document.AccessInternalReadOnlyDocument();

        List<(SpriteAtlas.Sprite Sprite, IReadOnlyChunkyImage Image)> sprites = new();

        foreach (IStructureMemberHandler member in document.StructureHelper.GetAllMembers())
        {
            if (readOnly.FindMember(member.Id) is not IReadOnlyImageNode node)
                continue;

            IReadOnlyChunkyImage image = node.GetLayerImageAtFrame(frame);
            RectI? bounds = image.FindTightCommittedBounds();

            // An empty layer has nothing to contribute and would only widen every cell.
            if (bounds is not { Width: > 0, Height: > 0 })
                continue;

            sprites.Add((new SpriteAtlas.Sprite(member.Id, bounds.Value), image));
        }

        if (sprites.Count == 0)
            return null;

        (VecI size, List<SpriteAtlas.Placement> placements) =
            SpriteAtlas.Plan(sprites.Select(entry => entry.Sprite).ToList(), spacing);

        using Surface atlas = new(size);

        DrawingBackendApi.Current.RenderingDispatcher.Invoke(() =>
        {
            for (int i = 0; i < placements.Count; i++)
            {
                DrawSprite(atlas, sprites[i].Image, placements[i]);
            }
        });

        VecI atlasSize = size;
        return DocumentViewModel.Build(builder => builder
            .WithSize(atlasSize)
            .WithGraph(graph => graph
                .WithImageLayerNode(
                    new LocalizedString("SPRITE_ATLAS_DOCUMENT_NAME"), atlas,
                    document.ProcessingColorSpace, out int id)
                .WithOutputNode(id, "Output")));
    }

    /// <summary>
    /// Copies one layer's trimmed content into its cell. The image is stored in chunks, so this
    /// walks the chunks the sprite covers and lets the clip take care of the overhang.
    /// </summary>
    private static void DrawSprite(Surface atlas, IReadOnlyChunkyImage image, SpriteAtlas.Placement placement)
    {
        RectI source = placement.Source;
        var canvas = atlas.DrawingSurface.Canvas;

        canvas.Save();
        canvas.ClipRect(new RectD(
            placement.Destination.X, placement.Destination.Y, source.Width, source.Height));
        canvas.Translate(placement.Destination.X - source.X, placement.Destination.Y - source.Y);

        int firstX = (int)Math.Floor(source.X / (double)ChunkyImage.FullChunkSize);
        int firstY = (int)Math.Floor(source.Y / (double)ChunkyImage.FullChunkSize);
        int lastX = (int)Math.Floor((source.Right - 1) / (double)ChunkyImage.FullChunkSize);
        int lastY = (int)Math.Floor((source.Bottom - 1) / (double)ChunkyImage.FullChunkSize);

        for (int chunkY = firstY; chunkY <= lastY; chunkY++)
        {
            for (int chunkX = firstX; chunkX <= lastX; chunkX++)
            {
                VecI chunkPos = new(chunkX, chunkY);
                image.DrawCommittedChunkOn(
                    chunkPos, ChunkResolution.Full, canvas, chunkPos * ChunkyImage.FullChunkSize);
            }
        }

        canvas.Restore();
    }
}
