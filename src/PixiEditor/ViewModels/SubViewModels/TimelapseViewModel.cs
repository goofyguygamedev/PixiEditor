using System.Collections.Specialized;
using System.IO;
using System.Linq;
using PixiEditor.Extensions.CommonApi.UserPreferences.Settings.PixiEditor;
using PixiEditor.Models.Commands;
using PixiEditor.Models.Commands.Attributes.Commands;
using PixiEditor.Models.Commands.Attributes.Evaluators;
using PixiEditor.Models.Dialogs;
using PixiEditor.Models.Timelapse;
using PixiEditor.OperatingSystem;
using PixiEditor.Helpers;
using PixiEditor.ViewModels.Document;

using Drawie.Backend.Core.Surfaces.ImageData;

namespace PixiEditor.ViewModels.SubViewModels;

/// <summary>
/// Commands and state for the timelapse replay feature: records a document's canvas every time an
/// action completes and encodes the result into a sped-up replay video.
/// </summary>
internal class TimelapseViewModel : SubViewModel<ViewModelMain>
{
    private readonly DocumentManagerViewModel documentManager;
    private readonly TimelapseRecorder recorder;

    private bool isExporting;

    /// <summary>
    /// Settable so the File menu's checkbox can bind two-way to it and actually show whether
    /// recording is on - with recording enabled by default, a toggle that gave no feedback looked
    /// like it did nothing while in fact turning recording off.
    /// </summary>
    public bool IsRecording
    {
        get => recorder.IsRecording;
        set
        {
            if (value == recorder.IsRecording)
                return;

            if (value)
                recorder.Start(CurrentOptions);
            else
                recorder.Stop();

            PixiEditorSettings.Timelapse.Enabled.Value = recorder.IsRecording;
            OnPropertyChanged(nameof(IsRecording));
        }
    }

    public int CurrentFrameCount
    {
        get
        {
            DocumentViewModel? document = documentManager.ActiveDocument;
            if (document is null)
                return 0;

            return recorder.GetOrCreateSession(document).FrameCount;
        }
    }

    /// <summary>
    /// The recorder's settings as they stand right now. Read fresh each time rather than cached,
    /// so whatever the Settings window last wrote is what a restart of the recording picks up.
    /// </summary>
    private static TimelapseOptions CurrentOptions => new(
        PixiEditorSettings.Timelapse.CaptureTrigger.Value,
        PixiEditorSettings.Timelapse.CaptureSource.Value,
        PixiEditorSettings.Timelapse.Background.Value,
        PixiEditorSettings.Timelapse.SweepIntervalSeconds.Value,
        PixiEditorSettings.Timelapse.DrawingIntervalSeconds.Value);

    public static double TargetDurationSeconds =>
        PixiEditorSettings.Timelapse.TargetDurationSeconds.Value;

    public static double LastFrameDurationSeconds =>
        PixiEditorSettings.Timelapse.LastFrameDurationSeconds.Value;

    public TimelapseViewModel(ViewModelMain owner, DocumentManagerViewModel documentManager)
        : base(owner)
    {
        this.documentManager = documentManager;
        recorder = new TimelapseRecorder(() => this.documentManager.ActiveDocument);
        recorder.Captured += OnFrameCaptured;

        // Every document gets hooked, not just the active one, so switching tabs mid-session
        // doesn't silently stop recording the one you left.
        foreach (DocumentViewModel document in documentManager.Documents)
            recorder.Attach(document);

        documentManager.Documents.CollectionChanged += OnDocumentsChanged;

        if (PixiEditorSettings.Timelapse.Enabled.Value)
        {
            recorder.Start(CurrentOptions);
        }

        WatchSettings();
    }

    /// <summary>
    /// Pushes Settings-window changes into the running recorder.
    /// </summary>
    /// <remarks>
    /// Without this the options would only be read when recording starts, so changing the capture
    /// rate or the source would appear to do nothing until the next launch - and the obvious way to
    /// check a new setting is to draw something immediately after changing it.
    /// </remarks>
    private void WatchSettings()
    {
        PixiEditorSettings.Timelapse.CaptureTrigger.ValueChanged += (_, _) => ApplyOptions();
        PixiEditorSettings.Timelapse.CaptureSource.ValueChanged += (_, _) => ApplyOptions();
        PixiEditorSettings.Timelapse.Background.ValueChanged += (_, _) => ApplyOptions();
        PixiEditorSettings.Timelapse.DrawingIntervalSeconds.ValueChanged += (_, _) => ApplyOptions();
        PixiEditorSettings.Timelapse.SweepIntervalSeconds.ValueChanged += (_, _) => ApplyOptions();

        // The same switch exists in the File menu and in Settings, so each has to follow the other.
        PixiEditorSettings.Timelapse.Enabled.ValueChanged += (_, _) =>
        {
            bool wanted = PixiEditorSettings.Timelapse.Enabled.Value;
            if (wanted == recorder.IsRecording)
                return;

            if (wanted)
                recorder.Start(CurrentOptions);
            else
                recorder.Stop();

            OnPropertyChanged(nameof(IsRecording));
        };
    }

    private void ApplyOptions() => recorder.ApplyOptions(CurrentOptions);

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (DocumentViewModel document in e.OldItems?.OfType<DocumentViewModel>() ?? [])
            recorder.Detach(document);

        foreach (DocumentViewModel document in e.NewItems?.OfType<DocumentViewModel>() ?? [])
            recorder.Attach(document);
    }

    private void OnFrameCaptured()
    {
        OnPropertyChanged(nameof(CurrentFrameCount));
        RefreshCommandStates();
    }

    /// <summary>
    /// Forces the timelapse commands to re-run their CanExecute evaluators.
    /// </summary>
    /// <remarks>
    /// A <see cref="Evaluator.CanExecuteAttribute"/> declared without explicit dependencies only
    /// re-evaluates when the active document changes. Capturing a frame is not such a change, so
    /// without this the export command is evaluated once while the session is still empty and then
    /// stays greyed out forever, no matter how many frames pile up.
    /// </remarks>
    private static void RefreshCommandStates()
    {
        if (CommandController.Current is null)
            return;

        CommandController.CanExecuteChanged("PixiEditor.Timelapse.");
    }

    // No MenuItemPath: this lives in the File menu as a checkbox built by
    // ToggleTimelapseRecordingMenuBuilder, so that the menu shows the current state.
    [Command.Basic("PixiEditor.Timelapse.ToggleRecording", "TIMELAPSE_TOGGLE",
        "TIMELAPSE_TOGGLE_DESCRIPTIVE")]
    public void ToggleRecording() => IsRecording = !IsRecording;

    [Command.Basic("PixiEditor.Timelapse.ExportReplay", "TIMELAPSE_EXPORT_REPLAY",
        "TIMELAPSE_EXPORT_REPLAY_DESCRIPTIVE", CanExecute = "PixiEditor.Timelapse.HasFrames",
        MenuItemPath = "FILE/TIMELAPSE_EXPORT_REPLAY", MenuItemOrder = 61)]
    public async Task ExportReplay()
    {
        DocumentViewModel? document = documentManager.ActiveDocument;
        if (document is null || isExporting)
            return;

        TimelapseSession session = recorder.GetOrCreateSession(document);
        if (session.FrameCount == 0)
        {
            NoticeDialog.Show("TIMELAPSE_NO_FRAMES", "TIMELAPSE_EXPORT_REPLAY");
            return;
        }

        isExporting = true;
        try
        {
            // Capture the current state first so the replay ends on what is actually on screen.
            recorder.CaptureIfChanged();

            // A whole window recording ends on a shot of the editor, which is not what anyone
            // wants to look at last. This renders the picture on its own for the closing frame.
            using Image? artOnly = PixiEditorSettings.Timelapse.ArtOnlyLastFrame.Value
                ? TimelapseRecorder.TryRenderArtworkFrame(document)
                : null;

            string? outputPath = await recorder.ExportAsync(
                session, targetDurationSeconds: TargetDurationSeconds,
                lastFrameSeconds: LastFrameDurationSeconds, artOnlyFinalFrame: artOnly);

            if (outputPath is null)
            {
                NoticeDialog.Show("TIMELAPSE_EXPORT_FAILED", "TIMELAPSE_EXPORT_REPLAY");
                return;
            }

            IOperatingSystem.Current.OpenFolder(outputPath);
        }
        catch (Exception e)
        {
            NoticeDialog.Show("TIMELAPSE_EXPORT_FAILED", "TIMELAPSE_EXPORT_REPLAY");
            CrashHelper.SendExceptionInfo(e);
        }
        finally
        {
            isExporting = false;
        }
    }

    [Command.Basic("PixiEditor.Timelapse.OpenFolder", "TIMELAPSE_OPEN_FOLDER",
        "TIMELAPSE_OPEN_FOLDER_DESCRIPTIVE")]
    public void OpenTimelapseFolder()
    {
        Directory.CreateDirectory(TimelapseRecorder.RootFolder);
        IOperatingSystem.Current.OpenFolder(TimelapseRecorder.RootFolder);
    }

    [Command.Basic("PixiEditor.Timelapse.ClearSession", "TIMELAPSE_CLEAR",
        "TIMELAPSE_CLEAR_DESCRIPTIVE", CanExecute = "PixiEditor.Timelapse.HasFrames")]
    public void ClearSession()
    {
        DocumentViewModel? document = documentManager.ActiveDocument;
        if (document is null)
            return;

        recorder.GetOrCreateSession(document).DeleteFrames();
        OnPropertyChanged(nameof(CurrentFrameCount));
        RefreshCommandStates();
    }

    [Evaluator.CanExecute("PixiEditor.Timelapse.HasFrames")]
    public bool HasFrames() => CurrentFrameCount > 0;
}
