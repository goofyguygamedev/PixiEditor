using PixiEditor.Extensions.CommonApi.UserPreferences.Settings.PixiEditor;

namespace PixiEditor.ViewModels.UserPreferences.Settings;

/// <summary>
/// The Settings-window face of the timelapse recorder's options.
/// </summary>
/// <remarks>
/// These only write preferences. <see cref="SubViewModels.TimelapseViewModel"/> watches the same
/// settings and pushes them into the running recorder, so a change takes effect on the recording in
/// progress rather than at the next launch.
/// </remarks>
internal class TimelapseSettings : SettingsGroup
{
    public bool RecordingEnabled
    {
        get => PixiEditorSettings.Timelapse.Enabled.Value;
        set => RaiseAndUpdatePreference(PixiEditorSettings.Timelapse.Enabled, value);
    }

    public TimelapseCaptureTrigger CaptureTrigger
    {
        get => PixiEditorSettings.Timelapse.CaptureTrigger.Value;
        set
        {
            RaiseAndUpdatePreference(PixiEditorSettings.Timelapse.CaptureTrigger, value);
            OnPropertyChanged(nameof(IsDrawingIntervalRelevant));
        }
    }

    /// <summary>
    /// Whether the drawing interval box is worth showing. It does nothing in
    /// <see cref="TimelapseCaptureTrigger.OnActionComplete"/>, and an enabled-looking field that is
    /// ignored is worse than no field.
    /// </summary>
    public bool IsDrawingIntervalRelevant =>
        CaptureTrigger == TimelapseCaptureTrigger.WhileDrawing;

    public double DrawingIntervalSeconds
    {
        get => PixiEditorSettings.Timelapse.DrawingIntervalSeconds.Value;
        set => RaiseAndUpdatePreference(
            PixiEditorSettings.Timelapse.DrawingIntervalSeconds,
            Math.Clamp(value, TimelapseOptionsRange.MinDrawingInterval, TimelapseOptionsRange.MaxDrawingInterval));
    }

    public TimelapseCaptureSource CaptureSource
    {
        get => PixiEditorSettings.Timelapse.CaptureSource.Value;
        set => RaiseAndUpdatePreference(PixiEditorSettings.Timelapse.CaptureSource, value);
    }

    public TimelapseBackground Background
    {
        get => PixiEditorSettings.Timelapse.Background.Value;
        set => RaiseAndUpdatePreference(PixiEditorSettings.Timelapse.Background, value);
    }

    public double TargetDurationSeconds
    {
        get => PixiEditorSettings.Timelapse.TargetDurationSeconds.Value;
        set => RaiseAndUpdatePreference(
            PixiEditorSettings.Timelapse.TargetDurationSeconds, Math.Clamp(value, 1, 600));
    }
}

/// <summary>
/// The clamping bounds, mirrored here so the settings page does not have to reach into the
/// recorder's own types.
/// </summary>
internal static class TimelapseOptionsRange
{
    public const double MinDrawingInterval = 0.05;
    public const double MaxDrawingInterval = 10;
}
