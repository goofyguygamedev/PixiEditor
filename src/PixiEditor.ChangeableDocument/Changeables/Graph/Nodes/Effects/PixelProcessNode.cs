using System.Runtime.InteropServices;
using Drawie.Backend.Core;
using Drawie.Backend.Core.Numerics;
using Drawie.Backend.Core.Surfaces;
using Drawie.Backend.Core.Surfaces.ImageData;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Changeables.Graph.Interfaces;
using PixiEditor.ChangeableDocument.Rendering;

namespace PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.Effects;

/// <summary>
/// Base for effects that work on the actual pixels of what is connected to them, in 8-bit sRGB
/// straight alpha, rather than as a shader.
/// </summary>
/// <remarks>
/// Pixel-art effects tend to need things a shader cannot do: every pixel looking at its neighbours in
/// order, connected regions, exact palette colours. The result is cached against a hash of the input
/// pixels and settings, because a redraw of the canvas for some unrelated reason is far more common
/// than the artwork actually changing.
/// </remarks>
public abstract class PixelProcessNode : RenderNode, IRenderInput
{
    private static readonly ImageInfo ResultInfo =
        new(0, 0, ColorType.RgbaF16, AlphaType.Premul, ColorSpace.CreateSrgb());

    public RenderInputProperty Background { get; }

    private readonly Paint resultPaint = new();

    private Surface? cachedResult;
    private int cachedKey;
    private VecI cachedSize;

    protected PixelProcessNode()
    {
        Background = CreateRenderInput("Background", "BACKGROUND");
        Output.FirstInChain = null;
    }

    /// <summary>Whether the input is drawn underneath the result, for effects that only add to it.</summary>
    protected virtual bool DrawsBackground => false;

    /// <summary>Blend used to put the result on the canvas.</summary>
    protected virtual BlendMode ResultBlendMode => BlendMode.SrcOver;

    /// <summary>Adds every setting that affects the result, so a change to one invalidates the cache.</summary>
    protected abstract void AddSettingsTo(ref HashCode hash, RenderContext context);

    /// <summary>The processed image as straight-alpha RGBA, or null to draw nothing.</summary>
    /// <param name="rgba">The input as straight-alpha 8-bit sRGB RGBA.</param>
    protected abstract byte[]? Process(byte[] rgba, VecI size, RenderContext context);

    protected override void OnPaint(RenderContext context, Canvas surface)
    {
        if (Background.Value == null)
        {
            return;
        }

        if (DrawsBackground)
        {
            Background.Value.Paint(context, surface);
        }

        using Texture artwork = Texture.ForProcessing(surface, context.ProcessingColorSpace);

        RenderContext artworkContext = context.Clone();
        bool isAdjusted = context.DocumentSize == context.RenderOutputSize;
        artworkContext.RenderOutputSize = isAdjusted
            ? context.RenderOutputSize
            : (VecI)(context.RenderOutputSize * context.ChunkResolution.InvertedMultiplier());
        Background.Value.Paint(artworkContext, artwork.DrawingSurface.Canvas);

        Surface? result = GetResult(artwork, context);
        if (result is null)
        {
            return;
        }

        int saved = surface.Save();
        surface.SetMatrix(Matrix3X3.Identity);
        resultPaint.BlendMode = ResultBlendMode;
        surface.DrawSurface(result.DrawingSurface, 0, 0, resultPaint);
        surface.RestoreToCount(saved);
    }

    private Surface? GetResult(Texture artwork, RenderContext context)
    {
        VecI size = artwork.Size;
        using Pixmap pixmap = artwork.PeekPixels();

        // A Skia pixel span has to be taken a whole pixel at a time.
        ReadOnlySpan<Half> pixels = MemoryMarshal.Cast<ulong, Half>(pixmap.GetPixelSpan<ulong>());

        HashCode hash = new();
        hash.AddBytes(MemoryMarshal.AsBytes(pixels));
        hash.Add(context.ChunkResolution);
        AddSettingsTo(ref hash, context);
        int key = hash.ToHashCode();

        if (key == cachedKey && size == cachedSize && cachedResult is not null)
        {
            return cachedResult;
        }

        cachedResult?.Dispose();
        cachedResult = null;
        cachedKey = key;
        cachedSize = size;

        byte[] rgba = ToStraightSrgb(pixels, size, !context.ProcessingColorSpace.IsSrgb);
        byte[]? processed = Process(rgba, size, context);
        if (processed is null)
        {
            return null;
        }

        cachedResult = new Surface(ResultInfo.WithSize(size));
        cachedResult.DrawBytes(size, processed, ColorType.Rgba8888, AlphaType.Unpremul);
        return cachedResult;
    }

    private static byte[] ToStraightSrgb(ReadOnlySpan<Half> pixels, VecI size, bool isLinear)
    {
        byte[] rgba = new byte[size.X * size.Y * 4];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            float alpha = (float)pixels[i + 3];
            if (alpha <= 0)
            {
                continue;
            }

            rgba[i] = Encode((float)pixels[i] / alpha);
            rgba[i + 1] = Encode((float)pixels[i + 1] / alpha);
            rgba[i + 2] = Encode((float)pixels[i + 2] / alpha);
            rgba[i + 3] = (byte)Math.Clamp((alpha * 255f) + 0.5f, 0f, 255f);
        }

        return rgba;

        byte Encode(float value)
        {
            if (isLinear)
            {
                value = value <= 0.0031308f
                    ? value * 12.92f
                    : (1.055f * MathF.Pow(value, 1f / 2.4f)) - 0.055f;
            }

            return (byte)Math.Clamp((value * 255f) + 0.5f, 0f, 255f);
        }
    }

    public override RectD? GetPreviewBounds(RenderContext ctx, string elementToRenderName = "")
    {
        return TryGetInputBounds(ctx, elementToRenderName) ?? new RectD(0, 0, ctx.DocumentSize.X, ctx.DocumentSize.Y);
    }

    public override void RenderPreview(DrawingSurface renderOn, RenderContext context, string elementToRenderName)
    {
        int saved = renderOn.Canvas.Save();
        OnPaint(context, renderOn.Canvas);
        renderOn.Canvas.RestoreToCount(saved);
    }

    public override void Dispose()
    {
        base.Dispose();
        resultPaint.Dispose();
        cachedResult?.Dispose();
    }
}
