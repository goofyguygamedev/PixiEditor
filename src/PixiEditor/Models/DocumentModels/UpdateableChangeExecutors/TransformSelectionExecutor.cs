using Drawie.Backend.Core.Numerics;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Actions.Generated;
using PixiEditor.Models.DocumentModels.UpdateableChangeExecutors.Features;
using PixiEditor.Helpers;
using PixiEditor.Models.Tools;

namespace PixiEditor.Models.DocumentModels.UpdateableChangeExecutors;

#nullable enable
/// <summary>
/// Puts the transform box around the selection while a select tool is active, so the selection can
/// be resized by its handles the same way a pasted image is.
/// </summary>
/// <remarks>
/// <para>
/// This is the same box, overlay and size readout the paste transform uses; it only drives a
/// different change. Scale only: rotating or shearing a selection would resample its edges off the
/// pixel grid, and a selection is read back per pixel.
/// </para>
/// <para>
/// Each drag is finished on release rather than waiting for an apply, so one drag is one undo step
/// and the box stays up for the next one. It does not block other actions, so pressing the canvas
/// outside the box still starts a new selection.
/// </para>
/// </remarks>
internal class TransformSelectionExecutor : UpdateableChangeExecutor, ITransformableExecutor,
    ITransformStoppedEvent
{
    private bool boxShown;
    private bool changeInProgress;
    private bool showingBox;

    public override ExecutorType Type => ExecutorType.ToolLinked;

    public override bool BlocksOtherActions => false;

    /// <summary>
    /// Puts the box back after a select gesture, which force-stopped this executor to run.
    /// </summary>
    /// <remarks>
    /// Safe to call before the new selection has been applied: the box is fitted again when the
    /// selection change arrives.
    /// </remarks>
    internal static void ShowFor(DocumentInternalParts internals) =>
        internals.ChangeController.TryStartExecutor(new TransformSelectionExecutor());

    public override ExecutionState Start()
    {
        // Succeeds even with nothing selected: a selection made after this point arrives through
        // OnSelectionPathChanged, and the executor has to still be listening when it does.
        FitBoxToSelection();
        return ExecutionState.Success;
    }

    public override void OnSelectionPathChanged()
    {
        if (changeInProgress)
        {
            // Our own drag is what is rewriting the path. Re-fitting now would fight the handles.
            return;
        }

        FitBoxToSelection();
    }

    private void FitBoxToSelection()
    {
        RectD? bounds = SelectionBounds();

        if (bounds is null)
        {
            HideBox();
            return;
        }

        if (boxShown && AlreadyFits(bounds.Value))
        {
            return;
        }

        // Setting the box's corners raises the same event a dragged handle does, and showing it
        // must not count as the artist resizing anything: that wrote a selection change every time
        // the box appeared, so an undo could be followed by a selection snapping to whatever the
        // last transform left behind - the bounds of a pasted image, say.
        ShapeCorners corners = new(bounds.Value);
        showingBox = true;

        try
        {
            document!.TransformHandler.ShowTransform(
                DocumentTransformMode.Scale_NoRotate_NoShear_NoPerspective, false, corners, false);
            document.TransformHandler.CanAlignToPixels = true;
        }
        finally
        {
            showingBox = false;
        }

        boxShown = true;
    }

    private RectD? SelectionBounds()
    {
        if (document?.SelectionPathBindable is not { IsEmpty: false } path)
        {
            return null;
        }

        RectD bounds = path.TightBounds;
        return bounds is { Width: > 0, Height: > 0 } ? bounds : null;
    }

    /// <summary>
    /// Whether the box is already where the selection is. Re-showing it needlessly would throw
    /// away the transform's own undo stack on every unrelated selection change.
    /// </summary>
    private bool AlreadyFits(RectD bounds)
    {
        RectD current = document!.TransformHandler.Corners.AABBBounds;
        const double tolerance = 0.01;

        return Math.Abs(current.Left - bounds.Left) < tolerance &&
               Math.Abs(current.Top - bounds.Top) < tolerance &&
               Math.Abs(current.Right - bounds.Right) < tolerance &&
               Math.Abs(current.Bottom - bounds.Bottom) < tolerance;
    }

    private void HideBox()
    {
        if (!boxShown)
        {
            return;
        }

        document!.TransformHandler.HideTransform();
        boxShown = false;
    }

    public bool IsTransforming => boxShown;

    public void OnTransformStarted()
    {
    }

    public void OnTransformChanged(ShapeCorners corners)
    {
        if (!SelectionTransform.IsRealChange(SelectionBounds(), corners, showingBox))
        {
            return;
        }

        internals!.ActionAccumulator.AddActions(new TransformSelectionPath_Action(corners));
        changeInProgress = true;
    }

    public void OnTransformStopped() => FinishChange();

    public void OnTransformApplied()
    {
        FinishChange();
        HideBox();
        onEnded!(this);
    }

    public void OnLineOverlayMoved(VecD start, VecD end)
    {
    }

    public void OnSelectedObjectNudged(VecI distance) => document!.TransformHandler.Nudge(distance);

    public bool IsTransformingMember(Guid id) => false;

    /// <summary>
    /// Claims the transform feature only once a handle has actually moved something.
    /// </summary>
    /// <remarks>
    /// The box is up for as long as a select tool is active, and the commands that ask this
    /// question would otherwise change meaning the whole time: Escape would commit the box instead
    /// of clearing the selection, and the arrow keys would nudge the selection outline instead of
    /// the pixels.
    /// </remarks>
    public bool IsFeatureEnabled<T>()
    {
        Type feature = typeof(T);
        return feature == typeof(ITransformableExecutor) && changeInProgress ||
               feature == typeof(ITransformStoppedEvent) && changeInProgress;
    }

    private void FinishChange()
    {
        if (!changeInProgress)
        {
            return;
        }

        internals!.ActionAccumulator.AddFinishedActions(new EndTransformSelectionPath_Action());
        changeInProgress = false;
    }

    public override void ForceStop()
    {
        FinishChange();
        HideBox();
    }
}
