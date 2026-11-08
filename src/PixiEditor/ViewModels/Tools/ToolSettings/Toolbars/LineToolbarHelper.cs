using PixiEditor.Models.Handlers.Toolbars;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.ViewModels.Tools.ToolSettings.Settings;

namespace PixiEditor.ViewModels.Tools.ToolSettings.Toolbars;

/// <summary>
/// Tidies up the line tools' curve-point setting after the toolbar factory has built it. The factory
/// only knows the generic size-setting shape, which defaults to a "px" unit and a minimum of one -
/// neither of which fits a count of control points that is allowed to be zero.
/// </summary>
internal static class LineToolbarHelper
{
    /// <summary>Upper bound on curve points; past this the handles are unusably crowded.</summary>
    public const int MaxCurvePoints = 16;

    public static void ConfigureCurveSettings(IToolbar toolbar)
    {
        if (toolbar.GetSetting(LineToolSettings.CurvePoints) is not SizeSettingViewModel curvePoints)
            return;

        curvePoints.Min = 0;
        curvePoints.Max = MaxCurvePoints;
        curvePoints.DecimalPlaces = 0;
        curvePoints.Unit = string.Empty;
    }
}
