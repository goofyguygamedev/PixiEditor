using Drawie.Backend.Core;
using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.Numerics;
using Drawie.Backend.Core.Surfaces;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Changeables.Graph.Interfaces;
using PixiEditor.ChangeableDocument.Rendering;

namespace PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.Effects;

[NodeInfo("Outline")]
public class OutlineNode : RenderNode, IRenderInput
{
    public RenderInputProperty Background { get; }
    public InputProperty<OutlineType> Type { get; }
    public InputProperty<double> Thickness { get; }
    public InputProperty<Color> Color { get; }

    /// <summary>
    /// Blurs the outline edge. 0 keeps the hard convolution edge; higher values feather it.
    /// </summary>
    public InputProperty<double> Softness { get; }

    /// <summary>
    /// Biases which sides the outline grows towards. (0,0) grows evenly in all directions and
    /// leaves the original behaviour untouched.
    /// </summary>
    public InputProperty<VecD> Direction { get; }

    private Kernel simpleKernel = new Kernel(3, 3, [1, 1, 1, 1, 1, 1, 1, 1, 1]);
    private Kernel pixelPerfectKernel = new Kernel(3, 3, [0, 1, 0, 1, -4, 1, 0, 1, 0]);

    private Kernel gaussianKernel = new Kernel(5, 5, [
        1, 4, 6, 4, 1,
        4, 16, 24, 16, 4,
        6, 24, 36, 24, 6,
        4, 16, 24, 16, 4,
        1, 4, 6, 4, 1
    ]);

    private Paint paint;
    private ImageFilter filter;

    // CreateCompose does not take ownership, so the parts have to outlive the composed filter and
    // be disposed together with it.
    private readonly List<ImageFilter> ownedFilters = new();

    private OutlineType? lastType = null;

    protected override bool ExecuteOnlyOnCacheChange => true;

    public OutlineNode()
    {
        Background = CreateRenderInput("Background", "BACKGROUND");
        Type = CreateInput("Type", "TYPE", OutlineType.Simple);
        Thickness = CreateInput("Thickness", "THICKNESS", 1.0)
            .WithRules(validator => validator.Min(0.0));
        Color = CreateInput("Color", "COLOR", Colors.Black);
        Softness = CreateInput("Softness", "SOFTNESS", 0.0)
            .WithRules(validator => validator.Min(0.0).Max(1.0));
        Direction = CreateInput("Direction", "DIRECTION", new VecD(0, 0));

        paint = new Paint();

        Output.FirstInChain = null;
    }

    protected override void OnExecute(RenderContext context)
    {
        base.OnExecute(context);

        Kernel finalKernel = Type.Value switch
        {
            OutlineType.Simple => simpleKernel,
            OutlineType.Gaussian => gaussianKernel,
            OutlineType.PixelPerfect => pixelPerfectKernel,
            _ => simpleKernel
        };

        // PixelPerfect is an edge-detect kernel rather than a dilation, so masking it by a
        // direction would not mean anything.
        VecD direction = Direction.Value;
        if (direction.LengthSquared > 0 && Type.Value != OutlineType.PixelPerfect)
        {
            finalKernel = ApplyDirection(finalKernel, direction);
        }

        VecI offset = new VecI(finalKernel.RadiusX, finalKernel.RadiusY);
        float sum = finalKernel.Sum;
        double gain = sum != 0 ? 1.0 / sum : 1.0;

        DisposeFilters();

        ImageFilter? convolution =
            ImageFilter.CreateMatrixConvolution(finalKernel, (float)gain, 0, offset, TileMode.Clamp, true);
        filter = convolution;

        double softness = Math.Clamp(Softness.Value, 0, 1);
        if (softness > 0 && convolution is not null)
        {
            // Scale the blur by thickness so softness stays proportional to the outline's weight.
            float sigma = (float)(softness * Math.Max(1.0, Thickness.Value));
            ImageFilter? blur = ImageFilter.CreateBlur(sigma, sigma);
            if (blur is not null)
            {
                ImageFilter? softened = ImageFilter.CreateCompose(blur, convolution);
                if (softened is not null)
                {
                    ownedFilters.Add(convolution);
                    ownedFilters.Add(blur);
                    filter = softened;
                }
                else
                {
                    blur.Dispose();
                }
            }
        }

        lastType = Type.Value;
    }

    protected override void OnPaint(RenderContext context, Canvas surface)
    {
        if (Background.Value == null)
        {
            return;
        }

        if (Thickness.Value > 0)
        {
            paint.ImageFilter = filter;
            paint.ColorFilter = ColorFilter.CreateBlendMode(Color.Value, BlendMode.SrcIn);

            using Texture temp = Texture.ForProcessing(surface, context.ProcessingColorSpace);
            int saved = temp.DrawingSurface.Canvas.SaveLayer(paint);

            var ctx = context.Clone();
            bool isAdjusted = context.DocumentSize == context.RenderOutputSize;
            ctx.RenderOutputSize = isAdjusted
                ? context.RenderOutputSize
                : (VecI)(context.RenderOutputSize * context.ChunkResolution.InvertedMultiplier());

            Background.Value.Paint(ctx, temp.DrawingSurface.Canvas);

            temp.DrawingSurface.Canvas.RestoreToCount(saved);

            temp.DrawingSurface.Canvas.SetMatrix(Matrix3X3.Identity);

            int thickness = (int)(Thickness.Value * context.ChunkResolution.Multiplier());

            for (int i = 1; i < (int)thickness; i++)
            {
                saved = temp.DrawingSurface.Canvas.SaveLayer(paint);

                temp.DrawingSurface.Canvas.DrawSurface(temp.DrawingSurface, 0, 0);

                temp.DrawingSurface.Canvas.RestoreToCount(saved);
            }

            saved = surface.Save();
            surface.SetMatrix(Matrix3X3.Identity);
            surface.DrawSurface(temp.DrawingSurface, 0, 0);

            surface.RestoreToCount(saved);
        }

        Background?.Value?.Paint(context, surface);
    }

    /// <summary>
    /// Zeroes the kernel weights pointing away from <paramref name="direction"/>, so the outline
    /// only grows on the requested sides. The centre weight is kept so the shape itself survives.
    /// </summary>
    private static Kernel ApplyDirection(Kernel source, VecD direction)
    {
        Kernel masked = (Kernel)source.Clone();
        VecD unit = direction.Normalize();

        for (int oy = -masked.RadiusY; oy <= masked.RadiusY; oy++)
        {
            for (int ox = -masked.RadiusX; ox <= masked.RadiusX; ox++)
            {
                if (ox == 0 && oy == 0)
                    continue;

                if (ox * unit.X + oy * unit.Y < 0)
                {
                    masked[ox, oy] = 0;
                }
            }
        }

        return masked;
    }

    private void DisposeFilters()
    {
        filter?.Dispose();
        filter = null;

        foreach (ImageFilter owned in ownedFilters)
        {
            owned.Dispose();
        }

        ownedFilters.Clear();
    }

    public override void Dispose()
    {
        base.Dispose();
        DisposeFilters();
        paint?.Dispose();
    }

    public override RectD? GetPreviewBounds(RenderContext ctx, string elementToRenderName = "")
    {
        RectD? backgroundBounds = TryGetInputBounds(ctx, elementToRenderName);
        if (backgroundBounds != null)
        {
            return backgroundBounds.Value.Inflate(Thickness.Value * 2);
        }

        return new RectD(0, 0, ctx.DocumentSize.X, ctx.DocumentSize.Y);
    }

    public override void RenderPreview(DrawingSurface renderOn, RenderContext context, string elementToRenderName)
    {
        int saved = renderOn.Canvas.Save();
        //renderOn.Canvas.Scale((float)context.ChunkResolution.Multiplier());
        OnPaint(context, renderOn.Canvas);
        renderOn.Canvas.RestoreToCount(saved);
    }

    public override Node CreateCopy()
    {
        return new OutlineNode();
    }
}

public enum OutlineType
{
    Simple,
    Gaussian,
    PixelPerfect,
}
