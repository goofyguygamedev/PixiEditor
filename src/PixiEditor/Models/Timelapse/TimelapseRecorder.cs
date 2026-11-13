using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>
    /// The session for <paramref name="document"/>: its existing frames if it has any on disk, and
    /// a move to a new folder if the document has been saved elsewhere since the last frame.
    /// </summary>
    /// <remarks>
    /// Creates no directory, so the UI can ask a document how many frames it has without leaving an
    /// empty folder behind for every document that is merely opened. The folder is created by the
    /// write itself, at the point there is actually a frame to put in it.
    /// </remarks>
    public TimelapseSession GetOrCreateSession(DocumentViewModel document)
    {
        string key = GetSessionKey(document);

        if (!sessions.TryGetValue(document.Id, out TimelapseSession? existing))
        {
            TimelapseSession created = new(key, Path.Combine(RootFolder, key));
            sessions[document.Id] = created;
            return created;
        }

        if (existing.Key == key)
            return existing;

        TimelapseSession rekeyed = Rekey(existing, key);
        sessions[document.Id] = rekeyed;
        return rekeyed;
    }

    /// <summary>
    /// Folder name a document's frames live under, derived from where the file is saved so that
    /// closing it and opening it again tomorrow continues the same replay.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately not the document id. That id is a fresh <c>Guid.NewGuid()</c> on every load and
    /// is never written into the .pixi file, so keying on it meant each reopen quietly started a new
    /// replay in a new folder and orphaned the previous one on disk: a drawing worked on across
    /// several evenings gave one replay per evening and no way to get the whole thing.
    /// </para>
    /// <para>
    /// The file's own name goes in the folder name alongside the hash, so the timelapse directory
    /// can be made sense of by hand. The hash is of the full path, so two files that share a name in
    /// different folders do not share a replay.
    /// </para>
    /// <para>
    /// A document that has never been saved has no stable identity to key on and falls back to its
    /// id, which means a fresh folder per load. Saving it moves those frames onto the real key, so
    /// nothing drawn before the first save is lost.
    /// </para>
    /// </remarks>
    private static string GetSessionKey(DocumentViewModel document)
    {
        string? path = document.FullFilePath;
        if (string.IsNullOrWhiteSpace(path))
            return $"unsaved_{document.Id:N}";

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            full = path; // Unparseable path: hash it as given rather than lose the session.
        }

        string normalized = full
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        // Windows and macOS treat paths differing only in case as the same file; Linux does not.
        if (!System.OperatingSystem.IsLinux())
            normalized = normalized.ToLowerInvariant();

        string hash = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16]
            .ToLowerInvariant();

        string name = SanitizeName(Path.GetFileNameWithoutExtension(full));
        return name.Length == 0 ? hash : $"{name}_{hash}";
    }

    /// <summary>Makes a file's name usable as a folder name, and short enough not to dominate it.</summary>
    private static string SanitizeName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder builder = new(name.Length);

        foreach (char c in name)
        {
            if (builder.Length >= 40)
                break;

            builder.Append(c == ' ' || Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        return builder.ToString().Trim('_', '.');
    }

    /// <summary>
    /// Moves a session's frames to the folder its new key names, which is what happens when a
    /// document is saved for the first time or saved somewhere else.
    /// </summary>
    /// <remarks>
    /// If the destination already holds a replay - that file has been recorded before - the frames
    /// are appended to it and the old folder is left where it is, so neither recording is destroyed.
    /// </remarks>
    private TimelapseSession Rekey(TimelapseSession existing, string key)
    {
        string target = Path.Combine(RootFolder, key);

        if (Directory.Exists(existing.FolderPath) && !Directory.Exists(target))
        {
            // A frame from the previous capture may still be queued for the old folder. Letting the
            // queue drain first keeps it from being written into a folder that is about to move, or
            // from recreating that folder afterwards with a single stray frame in it.
            bool drained;
            try
            {
                drained = FlushPendingWritesAsync().Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
                drained = true; // A failed write has already been reported; it must not block this.
            }

            // Wedged queue: stay where we are and try again on the next capture rather than stall.
            if (!drained)
                return existing;

            try
            {
                Directory.CreateDirectory(RootFolder);
                Directory.Move(existing.FolderPath, target);
            }
            catch (Exception e)
            {
                // Recording into the new folder from here still beats dropping frames entirely.
                ReportCaptureFailure(e);
            }
        }

        return new TimelapseSession(key, target)
        {
            LastCapturedChangeGuid = existing.LastCapturedChangeGuid
        };
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

    /// <summary>
    /// Renders the artwork on its own, for ending a whole window recording on the picture.
    /// Returns null when the renderer is busy, in which case the clip just ends as it always did.
    /// </summary>
    public static Image? TryRenderArtworkFrame(DocumentViewModel document)
    {
        var maybeSurface = document.TryRenderWholeImage(
            document.AnimationDataViewModel.ActiveFrameTime, GetCaptureSize(document.SizeBindable));

        if (maybeSurface.IsT0)
            return null;

        using Surface surface = maybeSurface.AsT1;
        return surface.DrawingSurface.Snapshot();
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
    /// <para>
    /// Prefers a screen read, because that is the only way the canvas appears at all: the canvas is
    /// a GPU surface handed to the compositor, so Avalonia's software re-render of the visual tree
    /// produces the window with its panels and an empty hole where the artwork should be. The
    /// re-render is kept only as a fallback for platforms with no screen capture implemented.
    /// </para>
    /// <para>
    /// A screen read would also pick up whatever is in front of the window, so no frame is taken at
    /// all while another program is the active one. Nothing is lost by waiting: the artwork cannot
    /// change while the artist is somewhere else, and the change that is pending stays pending, so
    /// the next sweep records it as soon as they come back.
    /// </para>
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
            // Before the fallback, not after: re-rendering the visual tree would quietly write a
            // frame of empty canvas rather than skipping one.
            if (!WindowScreenCapture.IsOwnAppInFront())
                return false;

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
                        // Created here rather than when the session is resolved, so merely opening a
                        // document does not litter the timelapse directory with empty folders.
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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
        double lastFrameSeconds = 1,
        Image? artOnlyFinalFrame = null,
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

        VecI sourceSize = ToEvenSize(PickCommonFrameSize(frames, session.FrameSize));

        List<Image> composited = CompositeOntoBackground(frames, sourceSize, Options.Background,
            HoldFrameCount(lastFrameSeconds, fps, frames.Count), artOnlyFinalFrame);
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
    /// How many extra copies of the last frame to encode so that the replay rests on the finished
    /// picture for <paramref name="seconds"/> instead of cutting away the moment the last stroke
    /// lands.
    /// </summary>
    /// <remarks>
    /// One less than the seconds are worth, because the frame is already there once. Never enough
    /// to push the clip past the frame ceiling the rest of the export works to.
    /// </remarks>
    public static int HoldFrameCount(double seconds, int fps, int frameCount)
    {
        if (seconds <= 0 || fps <= 0)
        {
            return 0;
        }

        int wanted = (int)Math.Round(seconds * fps) - 1;
        return Math.Clamp(wanted, 0, Math.Max(0, MaxExportFrames - frameCount));
    }

    /// <summary>
    /// The size a replay is encoded at when its frames are not all the same size.
    /// </summary>
    /// <remarks>
    /// The most common size wins, with the last frame breaking ties, because that is both the shape
    /// the session mostly looked like and the shape it ended on. Taking the last frame's size
    /// outright - which is what this used to do - meant that switching capture source halfway
    /// through squashed every frame from the first half into the second half's aspect ratio.
    /// </remarks>
    private static VecI PickCommonFrameSize(List<Image> frames, VecI fallback)
    {
        if (frames.Count == 0)
            return fallback;

        Dictionary<VecI, int> counts = new();
        foreach (Image frame in frames)
        {
            VecI size = new(frame.Size.X, frame.Size.Y);
            counts[size] = counts.GetValueOrDefault(size) + 1;
        }

        VecI chosen = new(frames[^1].Size.X, frames[^1].Size.Y);
        int best = counts.GetValueOrDefault(chosen);

        foreach ((VecI size, int count) in counts)
        {
            if (count <= best)
                continue;

            best = count;
            chosen = size;
        }

        return chosen;
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
    /// legitimately differ: the canvas can be resized mid-session, the capture source can be
    /// switched from the artwork to the whole program, and a whole-program recording changes size
    /// whenever the window does. An odd-sized frame is scaled evenly on both axes and centred on
    /// the background, so it is letterboxed rather than stretched into a shape it never had.
    /// </para>
    /// </remarks>
    /// <param name="extraLastFrames">
    /// Copies of the final frame to add on the end, which hold the finished picture on screen.
    /// Composited again rather than repeating one image, because the renderer disposes what it is
    /// given and the same image handed to it twice would be disposed twice.
    /// </param>
    /// <param name="finalFrame">
    /// Shown in place of the last recorded frame and every held copy of it. Used to end a whole
    /// window recording on the artwork by itself, so the clip finishes on the picture rather than
    /// on a shot of the editor around it. It is scaled and centred like any other frame, so it
    /// does not have to match their size.
    /// </param>
    private static List<Image> CompositeOntoBackground(
        List<Image> frames, VecI size, TimelapseBackground background, int extraLastFrames = 0,
        Image? finalFrame = null)
    {
        int total = frames.Count + Math.Max(0, extraLastFrames);
        List<Image> result = new(total);

        DrawingBackendApi.Current.RenderingDispatcher.Invoke(() =>
        {
            using Paint checkerPaint = new() { Color = CheckerColor };

            for (int i = 0; i < total; i++)
            {
                Image frame = finalFrame is not null && i >= frames.Count - 1
                    ? finalFrame
                    : frames[Math.Min(i, frames.Count - 1)];

                using Surface surface = Surface.ForDisplay(size);
                DrawBackground(surface, size, background, checkerPaint);

                float scale = Math.Min(
                    size.X / (float)frame.Size.X, size.Y / (float)frame.Size.Y);

                var canvas = surface.DrawingSurface.Canvas;
                canvas.Save();

                // Both of these are no-ops for a frame that is already the chosen size, which is
                // every frame of an ordinary single-source recording.
                canvas.Translate(
                    (size.X - (frame.Size.X * scale)) / 2f, (size.Y - (frame.Size.Y * scale)) / 2f);
                canvas.Scale(scale);

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
