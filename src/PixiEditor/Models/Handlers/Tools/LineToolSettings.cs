namespace PixiEditor.Models.Handlers.Tools;

/// <summary>
/// Names of the line tools' curve settings. The toolbar derives a setting's name from the property
/// it is declared on, and the executor has to recognise those names when a setting changes, so both
/// sides reference these instead of repeating the strings.
/// </summary>
internal static class LineToolSettings
{
    public const string CurvePoints = nameof(CurvePoints);
    public const string CurveSharpness = nameof(CurveSharpness);
    public const string IsometricLock = nameof(IsometricLock);
}
