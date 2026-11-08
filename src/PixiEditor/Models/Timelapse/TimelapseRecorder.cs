using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Drawie.Backend.Core;
using Drawie.Backend.Core.Bridge;
using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using PixiEditor.Extensions.CommonApi.UserPreferences.Settings.PixiEditor;
using PixiEditor.Views;
using Drawie.Backend.Core.Surfaces;
using Drawie.Backend.Core.Surfaces.ImageData;
using Drawie.Numerics;
using PixiEditor.AnimationRenderer.Core;
using PixiEditor.AnimationRenderer.FFmpeg;
using PixiEditor.ViewModels.Document;

namespace PixiEditor.Models.Timelapse;

/// <summary>
/// Records a Procreate-style timelapse of a document: snapshots the canvas every time a user action
/// completes and encodes the collected frames into a replay video.
/// </summary>
/// <remarks>
/// Capture is driven by <see cref="DocumentViewModel.UndoBoundaryPassed"/>, which fires once per
/// undo step, so the replay has exactly one frame per thing the artist actually did - no padding
/// from idle time, and no duplicate frames from the middle of a single stroke.
/// <para>
/// Every one of those actions is captured; none are dropped to keep the UI responsive. Cost is
/// bounded at the other end instead: frames are captured at no more than
/// <see cref="MaxCaptureLongEdge"/> on the long edge, and the PNG write is handed to a background
/// queue so only the render and encode happen on the UI thread.
/// </para>
/// <para>
/// A slow interval timer runs alongside as a safety net for frames the renderer was too busy to
/// produce. It is gated on the document's change id, so it writes nothing when nothing is
/// outstanding.
/// </para>
/// </remarks>
internal class TimelapseRecorder
{
    /// <summary>Absolute cap on frames fed to the encoder, whatever the requested duration.</summary>
    public const int MaxExportFrames = 3000;

    /// <summary>
    /// Frame rate of the exported replay. Fixed rather than derived from the frame count, because
    /// deriving it produced one-frame-per-second slideshows for short sessions.
    /// </summary>
    public const int OutputFps = 30;

    /// <summary>
    /// Long edge a replay is rendered at. Small pixel-art canvases are scaled up to roughly this by
    /// the encoder; the source frames stay at canvas resolution so nothing is interpolated twice.
    /// </summary>
    public const int TargetLongEdge = 1080;

    /// <summary>
    /// Long edge frames are captured at. Only ever downscales: a huge canvas does not need a
    /// full-resolution PNG per action, and capping it is what keeps per-action capture affordable.
    /// </summary>
    public const int MaxCaptureLongEdge = 1080;

    /// <summary>Floor on replay length, so a handful of actions doesn't produce an unwatchable blip.</summary>
    private const double MinReplaySeconds = 2;

    public static string RootFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PixiEditor", "Timelapse");

    private readonly Dictionary<Guid, TimelapseSession> sessions = new();
    private readonly Func<DocumentViewModel?> activeDocumentGetter;
    private readonly HashSet<DocumentViewModel> attachedDocuments = new();
    private readonly HashSet<Guid> pendingCaptures = new();

    private readonly object writeLock = new();
    private Task writeChain = Task.CompletedTask;

    private DispatcherTimer? sweepTimer;
    private bool reportedCaptureFailure;

    /// <summary>Colour of the dark squares when the replay is exported on a checkerboard.</summary>
    private static readonly Color CheckerColor = new(255, 205, 205, 205);

    /// <summary>Side of one checkerboard square, matching the editor's own transparency grid.</summary>
    private const int CheckerCell = 8;

    public bool IsRecording { get; private set; }

    /// <summary>
    /// What the running recording is doing. Set through <see cref="Start"/> or
    /// <see cref="ApplyOptions"/> so a change can take effect mid-session.
    /// </summary>
    public TimelapseOptions Options { get; private set; } = new(
        TimelapseCaptureTrigger.OnActionComplete,
        TimelapseCaptureSource.ArtworkOnly,
        TimelapseBackground.White,
        2d,
        0.25);

    /// <summary>Raised after a frame is captured, so UI can reflect the new frame count.</summary>
    public event Action? Captured;

    public TimelapseRecorder(Func<DocumentViewModel?> activeDocumentGetter)
    {
        this.activeDocumentGetter = activeDocumentGetter;
    }

    public TimelapseSession? TryGetSession(Guid documentId) =>
        sessions.TryGetValue(documentId, out TimelapseSession? session) ? session : null;

    public TimelapseSession GetOrCreateSession(DocumentViewModel document)
    {
        if (sessions.TryGetValue(document.Id, out TimelapseSession? existing))
            return existing;

        string folder = Path.Combine(RootFolder, document.Id.ToString("N"));
        Directory.CreateDirectory(folder);

        TimelapseSession session = new(document.Id, folder);
        sessions[document.Id] = session;
        return session;
    }

    /// <summary>
    /// Starts listening for completed actions on a document. Safe to call more than once for the
    /// same document.
    /// </summary>
    public void Attach(DocumentViewModel document)
    {
        if (!attachedDocuments.Add(document))
            return;

        document.UndoBoundaryPassed += OnUndoBoundaryPassed;
    }

    public void Detach(DocumentViewModel document)
    {
        if (!attachedDocuments.Remove(document))
            return;

        document.UndoBoundaryPassed -= OnUndoBoundaryPassed;
        pendingCaptures.Remove(document.Id);
    }

    public void Start(TimelapseOptions options)
    {
        Options = options;
        RestartTimer();
        IsRecording = true;
    }

    /// <summary>
    /// Applies changed settings to a recording already in progress, so the Settings window affects
    /// the session being recorded rather than only the next one.
    /// </summary>
    public void ApplyOptions(TimelapseOptions options)
    {
        bool timerChanged =
            Math.Abs(options.TimerIntervalSeconds - Options.TimerIntervalSeconds) > 0.0001;

        Options = options;

        if (IsRecording && timerChanged)
            RestartTimer();
    }

    private void RestartTimer()
    {
        sweepTimer?.Stop();
        sweepTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(Options.TimerIntervalSeconds), DispatcherPriority.Background, OnSweep);
        sweepTimer.Start();
    }

    public void Stop()
    {
        sweepTimer?.Stop();
        sweepTimer = null;
        pendingCaptures.Clear();
        IsRecording = false;
    }

    private void OnUndoBoundaryPassed(DocumentViewModel document)
    {
        if (!IsRecording)
            return;

        // The boundary fires from inside change processing, before the scene has finished
        // rendering. Posting at Background priority lets that settle first, so the frame written is
        // the canvas the artist is actually looking at. The coalescing guard only collapses
        // duplicate posts for one not-yet-serviced action; it never skips an action.
        if (!pendingCaptures.Add(document.Id))
            return;

        Dispatcher.UIThread.Post(() =>
        {
            pendingCaptures.Remove(document.Id);

            if (!IsRecording || document.IsDisposed)
                return;

            CaptureIfChanged(document);
        }, DispatcherPriority.Background);
    }

    /// <remarks>
    /// Two jobs in one timer. Under <see cref="TimelapseCaptureTrigger.OnActionComplete"/> it is
    /// the slow safety net for frames the renderer was too busy to produce when an action finished.
    /// Under <see cref="TimelapseCaptureTrigger.WhileDrawing"/> it is the capture itself, but only
    /// for as long as a stroke is actually in progress - otherwise a quarter-second timer would
    /// just be a fast idle poll.
    /// </remarks>
    private void OnSweep(object? sender, EventArgs e)
    {
        DocumentViewModel? document = activeDocumentGetter();
        if (document is null)
            return;

        bool midStroke = document.BlockingUpdateableChangeActive;

        if (Options.Trigger == TimelapseCaptureTrigger.WhileDrawing && !midStroke)
            return;

        // Mid-stroke frames have to bypass the change-id check. That id comes off the undo stack,
        // which does not move until a change is committed, so every frame taken during a stroke
        // carries the id of the change before it and would be discarded as a duplicate - which is
        // exactly why "repeatedly while drawing" recorded almost nothing.
        CaptureIfChanged(document, force: midStroke);
    }

    /// <summary>
    /// Captures a frame of the active document, unless nothing has changed since the last one.
    /// </summary>
    public bool CaptureIfChanged() => CaptureIfChanged(activeDocumentGetter());

    /// <summary>
    /// Captures a frame of <paramref name="document"/>, unless nothing has changed since the last
    /// one.
    /// </summary>
    /// <param name="force">
    /// Take the frame even though the document's change id has not moved. Used while a stroke is in
    /// progress, where pixels are visibly changing but no change has been committed yet, so the id
    /// is still the one from before the stroke started.
    /// </param>
    public bool CaptureIfChanged(DocumentViewModel? document, bool force = false)
    {
        if (document is null || document.IsDisposed)
            return false;

        Guid? changeGuid = document.LastChangeGuid;

        TimelapseSession session = GetOrCreateSession(document);

        if (!force)
        {
            if (changeGuid is null)
                return false; // Nothing has ever been changed in this document.

            if (session.LastCapturedChangeGuid == changeGuid)
                return false; // Already captured: skip rather than duplicate the previous frame.
        }

        if (!TryCaptureFrame(document, session))
            return false;

        session.LastCapturedChangeGuid = changeGuid;
        Captured?.Invoke();
        return true;
    }

    private bool TryCaptureFrame(DocumentViewModel document, TimelapseSession session)
    {
        try
        {
            byte[] encoded;
            VecI captureSize;

            if (Options.Source == TimelapseCaptureSource.WholeProgram)
            {
                if (!TryCaptureWindow(out encoded, out captureSize))
                    return false;
            }
            else if (!TryCaptureArtwork(document, out encoded, out captureSize))
            {
                return false;
            }

            // Encoding needs the rendered pixels, but the write does not, so a slow disk never
            // shows up as a stutter between strokes.
            QueueWrite(session.NextFramePath(), encoded);

            session.OnFrameWritten(captureSize);
            return true;
        }
        catch (Exception e)
        {
            ReportCaptureFailure(e);
            return false;
        }
    }

    private static bool TryCaptureArtwork(DocumentViewModel document, out byte[] encoded, out VecI size)
    {
        encoded = [];
        size = GetCaptureSize(document.SizeBindable);

        var maybeSurface = document.TryRenderWholeImage(
            document.AnimationDataViewModel.ActiveFrameTime, size);

        if (maybeSurface.IsT0)
            return false; // Renderer was busy; the sweep will pick this change up.

        using Surface surface = maybeSurface.AsT1;
        encoded = EncodePng(surface);
        return true;
    }

    /// <summary>
    /// Captures the editor window itself - panels, toolbars, the canvas at its current zoom.
    /// </summary>
    /// <remarks>
    /// Prefers a screen read, because that is the only way the canvas appears at all: the canvas is
    /// a GPU surface handed to the compositor, so Avalonia's software re-render of the visual tree
    /// produces the window with its panels and an empty hole where the artwork should be. The
    /// re-render is kept only as a fallback for platforms with no screen capture implemented.
    /// </remarks>
    private static bool TryCaptureWindow(out byte[] encoded, out VecI size)
    {
        encoded = [];
        size = default;

        MainWindow? window = MainWindow.Current;
        if (window is null)
            return false;

        if (System.OperatingSystem.IsWindows())
        {
            IntPtr handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (WindowScreenCapture.TryCapture(handle, MaxCaptureLongEdge, out encoded, out size))
                return true;
        }

        return TryRenderWindowVisual(window, out encoded, out size);
    }

    /// <summary>
    /// Fallback window capture through Avalonia's renderer. Draws the UI correctly but leaves the
    /// canvas empty, so it is only better than recording nothing at all.
    /// </summary>
    private static bool TryRenderWindowVisual(MainWindow window, out byte[] encoded, out VecI size)
    {
        encoded = [];
        size = default;

        PixelSize pixelSize = PixelSize.FromSize(window.ClientSize, window.RenderScaling);
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
            return false;

        // mp4's yuv420p rejects odd dimensions, the same constraint the canvas path has.
        pixelSize = new PixelSize(
            pixelSize.Width + (pixelSize.Width % 2), pixelSize.Height + (pixelSize.Height % 2));

        using RenderTargetBitmap bitmap = new(pixelSize, new Vector(96, 96));
        bitmap.Render(window);

        using MemoryStream stream = new();
        bitmap.Save(stream);

        encoded = stream.ToArray();
        size = new VecI(pixelSize.Width, pixelSize.Height);
        return true;
    }

    private static byte[] EncodePng(Surface surface)
    {
        using Image snapshot = surface.DrawingSurface.Snapshot();
        using MemoryStream stream = new();
        snapshot.Encode(EncodedImageFormat.Png).SaveTo(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Queues a frame to be written to disk. Writes are chained so they stay in capture order and
    /// never run concurrently against the same folder.
    /// </summary>
    private void QueueWrite(string path, byte[] data)
    {
        lock (writeLock)
        {
            writeChain = writeChain.ContinueWith(
                _ =>
                {
                    try
                    {
                        File.WriteAllBytes(path, data);
                    }
                    catch (Exception e)
                    {
                        ReportCaptureFailure(e);
                    }
                },
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    /// <summary>Waits for queued frame writes to reach disk.</summary>
    private Task FlushPendingWritesAsync()
    {
        lock (writeLock)
        {
            return writeChain;
        }
    }

    private void ReportCaptureFailure(Exception e)
    {
        // A dropped frame must never take the editor down mid-stroke, but swallowing the reason
        // silently makes a broken recorder indistinguishable from an idle one. Report the first
        // failure only, so a persistently failing capture can't spam the log on every action.
        if (reportedCaptureFailure)
            return;

        reportedCaptureFailure = true;
        Console.Error.WriteLine($"Timelapse frame capture failed; recording may be incomplete. {e}");
    }

    private static VecI ToEvenSize(VecI size) =>
        new(Math.Max(2, size.X + (size.X % 2)), Math.Max(2, size.Y + (size.Y % 2)));

    /// <summary>
    /// Size a frame is captured at: the canvas size, downscaled if it is bigger than
    /// <see cref="MaxCaptureLongEdge"/>. Never upscales - that is the encoder's job, and doing it
    /// here would mean storing interpolated pixels on disk for no benefit.
    /// </summary>
    private static VecI GetCaptureSize(VecI canvasSize)
    {
        int longest = Math.Max(canvasSize.X, canvasSize.Y);
        if (longest <= MaxCaptureLongEdge || longest <= 0)
            return ToEvenSize(canvasSize);

        double scale = MaxCaptureLongEdge / (double)longest;
        return ToEvenSize(new VecI(
            Math.Max(2, (int)Math.Round(canvasSize.X * scale)),
            Math.Max(2, (int)Math.Round(canvasSize.Y * scale))));
    }

    /// <summary>
    /// Resolution the replay is encoded at. Pixel-art canvases are scaled by a whole number so
    /// every source pixel becomes an identical block; anything already large is left alone.
    /// </summary>
    private static VecI GetExportSize(VecI frameSize)
    {
        if (frameSize.X <= 0 || frameSize.Y <= 0)
            return ToEvenSize(new VecI(TargetLongEdge, TargetLongEdge));

        int longest = Math.Max(frameSize.X, frameSize.Y);
        int scale = Math.Max(1, TargetLongEdge / longest);

        return ToEvenSize(new VecI(frameSize.X * scale, frameSize.Y * scale));
    }

    /// <summary>
    /// Encodes a session's frames to a replay video.
    /// </summary>
    /// <param name="targetDurationSeconds">
    /// Roughly how long the replay should run. Longer sessions are sampled down to fit, the way
    /// Procreate compresses hours of drawing into a short clip; shorter ones simply play out.
    /// </param>
    public async Task<string?> ExportAsync(
        TimelapseSession session,
        string outputFormat = "mp4",
        double? targetDurationSeconds = 30,
        Action<double>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        // Frames are written in the background, so the newest ones may still be in flight.
        await FlushPendingWritesAsync();

        int maxFrames = MaxExportFrames;
        if (targetDurationSeconds is > 0)
        {
            maxFrames = Math.Clamp(
                (int)Math.Round(targetDurationSeconds.Value * OutputFps), 1, MaxExportFrames);
        }

        List<string> framePaths = SelectFramePaths(session, maxFrames);
        if (framePaths.Count == 0)
            return null;

        List<Image> frames = new(framePaths.Count);
        foreach (string path in framePaths)
        {
            Image? image = Image.FromEncodedData(path);
            if (image is not null)
                frames.Add(image);
        }

        if (frames.Count == 0)
            return null;

        // Play at a fixed rate, but stretch a very short session out rather than emit a clip too
        // brief to watch.
        int fps = OutputFps;
        if (frames.Count / (double)OutputFps < MinReplaySeconds)
        {
            // Floor, not round: rounding up the frame rate makes the clip shorter than the minimum
            // it is supposed to guarantee - three frames came out as a 1.5 second blink.
            fps = Math.Max(1, (int)Math.Floor(frames.Count / MinReplaySeconds));
        }

        VecI sourceSize = ToEvenSize(session.FrameSize == default
            ? new VecI(frames[0].Size.X, frames[0].Size.Y)
            : session.FrameSize);

        List<Image> composited = CompositeOntoBackground(frames, sourceSize, Options.Background);
        foreach (Image frame in frames)
            frame.Dispose();

        if (composited.Count == 0)
            return null;

        string outputPath = Path.Combine(
            session.FolderPath, $"replay_{DateTime.Now:yyyyMMdd_HHmmss}.{outputFormat}");

        FFMpegRenderer renderer = new()
        {
            FrameRate = fps,
            OutputFormat = outputFormat,
            Size = GetExportSize(sourceSize),
            QualityPreset = QualityPreset.VeryHigh
        };

        // The renderer takes ownership of the images and disposes them itself.
        bool success = await renderer.RenderAsync(
            composited, outputPath, cancellationToken, progressCallback);

        return success ? outputPath : null;
    }

    /// <summary>
    /// Flattens each frame onto an opaque background at one common size.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not cosmetic. Video has no alpha channel - mp4 encodes as yuv420p - so every transparent
    /// pixel becomes black unless something is painted under it first. Black artwork on a
    /// transparent canvas therefore encoded to a replay that was black from end to end, with
    /// perfectly good PNGs sitting on disk. This is the step that was missing.
    /// </para>
    /// <para>
    /// Done at export rather than at capture so the frames on disk stay lossless and keep their
    /// alpha, which means the background can be changed and the replay re-exported without having
    /// to record the session again.
    /// </para>
    /// <para>
    /// Everything is fitted to one size because the encoder's pipe requires it, and frames can
    /// legitimately differ: the canvas can be resized mid-session, and a whole-program recording
    /// changes size whenever the window does.
    /// </para>
    /// </remarks>
    private static List<Image> CompositeOntoBackground(
        List<Image> frames, VecI size, TimelapseBackground background)
    {
        List<Image> result = new(frames.Count);

        DrawingBackendApi.Current.RenderingDispatcher.Invoke(() =>
        {
            using Paint checkerPaint = new() { Color = CheckerColor };

            foreach (Image frame in frames)
            {
                using Surface surface = Surface.ForDisplay(size);
                DrawBackground(surface, size, background, checkerPaint);

                var canvas = surface.DrawingSurface.Canvas;
                canvas.Save();

                if (frame.Size.X != size.X || frame.Size.Y != size.Y)
                {
                    canvas.Scale(size.X / (float)frame.Size.X, size.Y / (float)frame.Size.Y);
                }

                canvas.DrawImage(frame, 0, 0);
                canvas.Restore();

                result.Add(surface.DrawingSurface.Snapshot());
            }
        });

        return result;
    }

    private static void DrawBackground(
        Surface surface, VecI size, TimelapseBackground background, Paint checkerPaint)
    {
        var canvas = surface.DrawingSurface.Canvas;

        if (background == TimelapseBackground.Black)
        {
            canvas.Clear(Colors.Black);
            return;
        }

        canvas.Clear(Colors.White);

        if (background != TimelapseBackground.Checkerboard)
            return;

        for (int y = 0; y < size.Y; y += CheckerCell)
        {
            for (int x = 0; x < size.X; x += CheckerCell)
            {
                if (((x / CheckerCell) + (y / CheckerCell)) % 2 == 1)
                    canvas.DrawRect(x, y, CheckerCell, CheckerCell, checkerPaint);
            }
        }
    }

    /// <summary>
    /// Evenly samples frames when a session has more than <paramref name="maxFrames"/>, so a long
    /// session is time-compressed instead of producing an endless video.
    /// </summary>
    private static List<string> SelectFramePaths(TimelapseSession session, int maxFrames)
    {
        List<string> all = session.EnumerateFramePaths().ToList();
        if (all.Count <= maxFrames)
            return all;

        List<string> sampled = new(maxFrames);
        double step = all.Count / (double)maxFrames;
        for (int i = 0; i < maxFrames; i++)
            sampled.Add(all[Math.Min(all.Count - 1, (int)(i * step))]);

        // However aggressive the sampling, the replay should still end on the finished artwork.
        sampled[^1] = all[^1];
        return sampled;
    }
}
