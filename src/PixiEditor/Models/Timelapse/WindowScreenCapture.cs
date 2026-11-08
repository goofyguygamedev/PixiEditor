using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Drawie.Numerics;

namespace PixiEditor.Models.Timelapse;

/// <summary>
/// Reads back the pixels a window actually occupies on screen.
/// </summary>
/// <remarks>
/// <para>
/// Avalonia's own <c>RenderTargetBitmap.Render</c> cannot do this job. It re-renders the visual
/// tree in software, and the canvas is not something that tree can redraw - it is a GPU surface
/// Drawie hands to the compositor - so a window captured that way comes back with every panel and
/// toolbar present and the artwork missing. Reading back the composited desktop is the only way to
/// get what the artist is actually looking at.
/// </para>
/// <para>
/// The trade-off is that this is a screen read, not a window read: anything overlapping the window
/// lands in the frame, and a minimized window cannot be captured at all.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WindowScreenCapture
{
    private const int Srccopy = 0x00CC0020;

    /// <summary>Also pick up layered windows, which is how some overlays and tooltips are drawn.</summary>
    private const int Captureblt = 0x40000000;

    private const int DibRgbColors = 0;
    private const int BiRgb = 0;

    /// <summary>Best-quality stretch mode, which matters because this only ever downscales.</summary>
    private const int Halftone = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader bmiHeader;
        public int bmiColors;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr handle);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr src,
        int srcX, int srcY, int rop);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr dest, int xDest, int yDest, int widthDest, int heightDest,
        IntPtr src, int xSrc, int ySrc, int widthSrc, int heightSrc, int rop);

    [DllImport("gdi32.dll")]
    private static extern bool SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr bitmap, uint startScan, uint scanLines,
        byte[] bits, ref BitmapInfo info, int usage);

    /// <summary>
    /// Captures <paramref name="windowHandle"/> as an encoded PNG, or returns false if there is
    /// nothing capturable (no handle, minimized, zero-sized, or GDI refused).
    /// </summary>
    /// <param name="maxLongEdge">
    /// Longest edge the captured frame may have; anything larger is scaled down. This is a storage
    /// limit, not a quality preference: a frame every quarter second at full desktop resolution is
    /// several megabytes each, which runs into gigabytes over a drawing session.
    /// </param>
    public static bool TryCapture(IntPtr windowHandle, int maxLongEdge, out byte[] png, out VecI size)
    {
        png = [];
        size = default;

        if (windowHandle == IntPtr.Zero || IsIconic(windowHandle))
            return false;

        if (!GetWindowRect(windowHandle, out NativeRect rect))
            return false;

        int sourceWidth = rect.Right - rect.Left;
        int sourceHeight = rect.Bottom - rect.Top;

        if (sourceWidth <= 0 || sourceHeight <= 0)
            return false;

        int width = sourceWidth;
        int height = sourceHeight;

        int longest = Math.Max(sourceWidth, sourceHeight);
        if (maxLongEdge > 0 && longest > maxLongEdge)
        {
            double scale = maxLongEdge / (double)longest;
            width = (int)Math.Round(sourceWidth * scale);
            height = (int)Math.Round(sourceHeight * scale);
        }

        // mp4's yuv420p rejects odd dimensions. Trimming a row beats padding one, which would be a
        // stripe of whatever happened to be in the buffer.
        width -= width % 2;
        height -= height % 2;

        if (width <= 0 || height <= 0)
            return false;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            return false;

        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;

        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, width, height);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
                return false;

            previous = SelectObject(memoryDc, bitmap);

            if (width == sourceWidth && height == sourceHeight)
            {
                if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, rect.Left, rect.Top,
                        Srccopy | Captureblt))
                {
                    return false;
                }
            }
            else
            {
                SetStretchBltMode(memoryDc, Halftone);

                if (!StretchBlt(memoryDc, 0, 0, width, height,
                        screenDc, rect.Left, rect.Top, sourceWidth, sourceHeight,
                        Srccopy | Captureblt))
                {
                    return false;
                }
            }

            byte[] pixels = new byte[width * height * 4];
            BitmapInfo info = new()
            {
                bmiHeader = new BitmapInfoHeader
                {
                    biSize = Marshal.SizeOf<BitmapInfoHeader>(),
                    biWidth = width,

                    // Negative height asks for top-down rows. Bottom-up is the GDI default and
                    // would hand back an upside-down frame.
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BiRgb
                }
            };

            if (GetDIBits(memoryDc, bitmap, 0, (uint)height, pixels, ref info, DibRgbColors) == 0)
                return false;

            // BitBlt leaves the alpha byte at zero, so without this the frame saves as a fully
            // transparent PNG - the same way the replay came out black the first time around.
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;

            png = EncodePng(pixels, width, height);
            size = new VecI(width, height);
            return true;
        }
        finally
        {
            if (previous != IntPtr.Zero)
                SelectObject(memoryDc, previous);

            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);

            if (memoryDc != IntPtr.Zero)
                DeleteDC(memoryDc);

            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static byte[] EncodePng(byte[] bgra, int width, int height)
    {
        using WriteableBitmap bitmap = new(
            new PixelSize(width, height), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Opaque);

        using (ILockedFramebuffer buffer = bitmap.Lock())
        {
            // Copied a row at a time: the framebuffer is free to pad its rows, and assuming a
            // tightly packed stride would shear the image when it does.
            int stride = width * 4;
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(bgra, y * stride, IntPtr.Add(buffer.Address, y * buffer.RowBytes), stride);
            }
        }

        using MemoryStream stream = new();
        bitmap.Save(stream);
        return stream.ToArray();
    }
}
