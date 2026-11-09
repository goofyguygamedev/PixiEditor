using Avalonia;
using Drawie.Backend.Core.Surfaces;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using Drawie.Numerics;
using PixiEditor.Helpers.Converters;
using Colors = Drawie.Backend.Core.ColorsImpl.Colors;

namespace PixiEditor.Views.Overlays;

/// <summary>A guide of 2:1 diagonals, the slope isometric pixel art is drawn along.</summary>
public class IsometricGridOverlay : Overlay
{
    /// <summary>Used when the grid size is too small to mean an isometric tile.</summary>
    private const double DefaultTileWidth = 16;

    public static readonly StyledProperty<double> TileWidthProperty =
        AvaloniaProperty.Register<IsometricGridOverlay, double>(nameof(TileWidth));

    public double TileWidth
    {
        get => GetValue(TileWidthProperty);
        set => SetValue(TileWidthProperty, value);
    }

    private readonly Paint pen = new() { Color = Colors.White.WithAlpha(110), StrokeWidth = 1, IsAntiAliased = true, Style = PaintStyle.Stroke };

    static IsometricGridOverlay()
    {
        IsVisibleProperty.Changed.Subscribe(args => (args.Sender as IsometricGridOverlay)?.Refresh());
        TileWidthProperty.Changed.Subscribe(args => (args.Sender as IsometricGridOverlay)?.Refresh());
    }

    public IsometricGridOverlay()
    {
        IsHitTestVisible = false;
    }

    public override bool CanRender() => true;

    protected override void OnRenderOverlay(Canvas context, RectD canvasBounds)
    {
        double tileWidth = TileWidth >= 2 ? TileWidth : DefaultTileWidth;
        double tileHeight = tileWidth / 2;
        double width = canvasBounds.Width;
        double height = canvasBounds.Height;
        double rise = width / 2;

        pen.StrokeWidth = (float)ReciprocalConverter.Convert(ZoomScale);

        for (double start = -rise; start <= height; start += tileHeight)
        {
            context.DrawLine(new VecD(0, start), new VecD(width, start + rise), pen);
        }

        for (double start = 0; start <= height + rise; start += tileHeight)
        {
            context.DrawLine(new VecD(0, start), new VecD(width, start - rise), pen);
        }
    }
}
