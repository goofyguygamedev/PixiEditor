using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.FilterNodes;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.Effects;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Drawie.Backend.Core;
using PixiEditor.ChangeableDocument;
using PixiEditor.ChangeableDocument.Enums;
using PixiEditor.Extensions.Exceptions;
using PixiEditor.Models.Commands.Attributes.Commands;
using PixiEditor.Models.Commands.Attributes.Evaluators;
using PixiEditor.Models.Dialogs;
using PixiEditor.Models.Handlers;
using PixiEditor.Extensions.CommonApi.UserPreferences.Settings.PixiEditor;
using PixiEditor.Views;
using System.Runtime.CompilerServices;
using PixiEditor.Views.Dialogs;
using PixiEditor.Models.IO;
using PixiEditor.Models.Layers;
using Drawie.Numerics;
using PixiEditor.Helpers;
using PixiEditor.UI.Common.Fonts;
using PixiEditor.UI.Common.Localization;
using PixiEditor.ViewModels.Dock;
using PixiEditor.ViewModels.Document;
using PixiEditor.ViewModels.Document.Nodes;
using PixiEditor.Views.Overlays.TextOverlay;

namespace PixiEditor.ViewModels.SubViewModels;
#nullable enable
[Command.Group("PixiEditor.Layer", "LAYER")]
internal class LayersViewModel : SubViewModel<ViewModelMain>
{
    public LayersViewModel(ViewModelMain owner)
        : base(owner)
    {
    }

    [Evaluator.CanExecute("PixiEditor.Layer.CanDeleteSelected",
        nameof(DocumentManagerViewModel.ActiveDocument),
        nameof(DocumentManagerViewModel.ActiveDocument.SelectedStructureMember))]
    public bool CanDeleteSelected()
    {
        var member = Owner.DocumentManagerSubViewModel.ActiveDocument?.SelectedStructureMember;
        if (member is null)
            return false;
        return true;
    }

    [Evaluator.CanExecute("PixiEditor.Layer.HasSelectedMembers",
        nameof(DocumentManagerViewModel.ActiveDocument),
        nameof(DocumentManagerViewModel.ActiveDocument.SelectedStructureMember),
        nameof(DocumentManagerViewModel.ActiveDocument.SoftSelectedStructureMembers))]
    public bool HasSelectedMembers()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return false;
        return doc.SelectedStructureMember is not null || doc.SoftSelectedStructureMembers.Count > 0;
    }

    [Evaluator.CanExecute("PixiEditor.Layer.HasTransformableMembers",
        nameof(DocumentManagerViewModel.ActiveDocument),
        nameof(DocumentManagerViewModel.ActiveDocument.SelectedStructureMember),
        nameof(DocumentManagerViewModel.ActiveDocument.SoftSelectedStructureMembers))]
    public bool HasTransformableMembers()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return false;
        bool hasMembers = doc.SelectedStructureMember is not null || doc.SoftSelectedStructureMembers.Count > 0;
        if (!hasMembers)
            return false;

        var selectedMembers = doc.ExtractSelectedLayers();
        foreach (var member in selectedMembers)
        {
            var handler = doc.StructureHelper.Find(member);
            if (handler is ITransformableMemberHandler)
            {
                return true;
            }
        }

        return false;
    }

    [Evaluator.CanExecute("PixiEditor.Layer.IsMemberTransformable",
        nameof(DocumentManagerViewModel.ActiveDocument),
        nameof(DocumentManagerViewModel.ActiveDocument.SelectedStructureMember),
        nameof(DocumentManagerViewModel.ActiveDocument.SoftSelectedStructureMembers))]
    public bool IsMemberTransformable(object property)
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return false;

        if (property is Guid memberGuid)
        {
            var handler = doc.StructureHelper.Find(memberGuid);
            return handler is ITransformableMemberHandler;
        }

        return false;
    }


    [Evaluator.CanExecute("PixiEditor.Layer.HasMultipleSelectedMembers",
        nameof(DocumentManagerViewModel.ActiveDocument),
        nameof(DocumentManagerViewModel.ActiveDocument.SelectedStructureMember),
        nameof(DocumentManagerViewModel.ActiveDocument.SoftSelectedStructureMembers))]
    public bool HasMultipleSelectedMembers()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return false;
        int count = doc.SoftSelectedStructureMembers.Count;
        if (doc.SelectedStructureMember is not null)
            count++;
        return count > 1;
    }

    [Evaluator.CanExecute("PixiEditor.Layer.HasMultipleFlattenedSelectedMembers",
        nameof(DocumentManagerViewModel.ActiveDocument),
        nameof(DocumentManagerViewModel.ActiveDocument.SelectedStructureMember),
        nameof(DocumentManagerViewModel.ActiveDocument.SoftSelectedStructureMembers))]
    public bool HasMultipleFlattenedSelectedMembers()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return false;
        int count = doc.ExtractSelectedLayers().Count;
        return count > 1;
    }

    private List<Guid> GetSelected()
    {
        List<Guid> members = new();
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return members;
        if (doc.SelectedStructureMember is not null)
            members.Add(doc.SelectedStructureMember.Id);
        members.AddRange(doc.SoftSelectedStructureMembers.Select(static member => member.Id));
        return members;
    }

    [Command.Basic("PixiEditor.Layer.DeleteAllSelected", "LAYER_DELETE_ALL_SELECTED",
        "LAYER_DELETE_ALL_SELECTED_DESCRIPTIVE", CanExecute = "PixiEditor.Layer.HasSelectedMembers",
        Icon = PixiPerfectIcons.Trash, AnalyticsTrack = true, Key = Key.Delete,
        ShortcutContexts = [typeof(LayersDockViewModel)])]
    public void DeleteAllSelected()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return;
        var selected = doc.ExtractSelectedLayers(true).Concat(doc.SelectedMembers).Distinct().ToList();
        if (selected.Count > 0)
        {
            doc.Operations.DeleteStructureMembers(selected);
        }
    }

    [Command.Basic("PixiEditor.Layer.NewFolder", "NEW_FOLDER", "CREATE_NEW_FOLDER",
        CanExecute = "PixiEditor.Layer.CanCreateNewMember",
        Icon = PixiPerfectIcons.FolderPlus, AnalyticsTrack = true)]
    public void NewFolder()
    {
        if (Owner.DocumentManagerSubViewModel.ActiveDocument is not { } doc)
            return;

        using var block = doc.Operations.StartChangeBlock();
        Guid? guid = doc.Operations.CreateStructureMember(StructureMemberType.Folder);
        if (doc.SoftSelectedStructureMembers.Count == 0)
            return;
        var selectedInOrder = doc.GetSelectedMembersInOrder();
        selectedInOrder.Reverse();
        block.ExecuteQueuedActions();

        if (guid is null)
            return;

        if (selectedInOrder.Count > 0)
        {
            Guid lastMovedMember = guid.Value;
            StructureMemberPlacement placement = StructureMemberPlacement.Inside;

            foreach (Guid memberGuid in selectedInOrder)
            {
                doc.Operations.MoveStructureMember(memberGuid, lastMovedMember, placement);
                lastMovedMember = memberGuid;
                if (placement == StructureMemberPlacement.Inside)
                {
                    placement = StructureMemberPlacement.Below;
                }

                block.ExecuteQueuedActions();
            }

            doc.Operations.ClearSoftSelectedMembers();
        }

        doc.Operations.SetSelectedMember(guid.Value);
    }

    [Command.Basic("PixiEditor.Layer.NewLayer", "NEW_LAYER", "CREATE_NEW_LAYER",
        CanExecute = "PixiEditor.Layer.CanCreateNewMember", Key = Key.N,
        Modifiers = KeyModifiers.Control | KeyModifiers.Shift,
        Icon = PixiPerfectIcons.FilePlus, AnalyticsTrack = true)]
    public void NewLayer()
    {
        if (Owner.DocumentManagerSubViewModel.ActiveDocument is not { } doc)
            return;

        doc.Operations.CreateStructureMember(StructureMemberType.ImageLayer);
    }

    [Command.Basic("PixiEditor.Layer.ToggleLayerLock", "TOGGLE_ACTIVE_LAYER_LOCK",
        "TOGGLE_ACTIVE_LAYER_LOCK_DESCRIPTIVE",
        CanExecute = "PixiEditor.Layer.HasSelectedMembers", Icon = PixiPerfectIcons.Lock, AnalyticsTrack = true)]
    public void ToggleLayerLock()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return;

        var selectedMembers = doc.SelectedMembers;
        if (selectedMembers.Count == 0)
            return;

        var memberVms = selectedMembers.Select(member => doc.StructureHelper.Find(member)).ToList();
        bool allLocked = memberVms.All(member => member is { IsLockedBindable: true });
        bool newLockState = !allLocked;

        using var block = doc.Operations.StartChangeBlock();
        foreach (var member in memberVms)
        {
            member.IsLockedBindable = newLockState;
        }
    }

    public Guid? NewLayer(Type layerType, ActionSource source, string? name = null)
    {
        if (Owner.DocumentManagerSubViewModel.ActiveDocument is not { } doc)
            return null;

        return doc.Operations.CreateStructureMember(layerType, source, name);
    }

    public Guid? ForceNewLayer(Type layerType, ActionSource source, string? name = null)
    {
        if (Owner.DocumentManagerSubViewModel.ActiveDocument is not { } doc)
            return null;

        return doc.Operations.ForceCreateStructureMember(layerType, source, name);
    }

    [Evaluator.CanExecute("PixiEditor.Layer.CanCreateNewMember")]
    public bool CanCreateNewMember()
    {
        return Owner.DocumentManagerSubViewModel.ActiveDocument is { BlockingUpdateableChangeActive: false };
    }

    [Command.Internal("PixiEditor.Layer.ToggleLockTransparency",
        CanExecute = "PixiEditor.Layer.SelectedMemberIsTransparencyLockable",
        AnalyticsTrack = true)]
    public void ToggleLockTransparency()
    {
        var member = Owner.DocumentManagerSubViewModel.ActiveDocument?.SelectedStructureMember;
        if (member is not ImageLayerNodeViewModel layerVm)
            return;
        layerVm.LockTransparencyBindable = !layerVm.LockTransparencyBindable;
    }

    [Command.Internal("PixiEditor.Layer.SelectActiveMember", AnalyticsTrack = true)]
    public void SelectActiveMember(Guid memberGuid)
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return;

        var member = doc.StructureHelper.Find(memberGuid);
        if (member is null)
            return;

        doc.Operations.SetSelectedMember(member.Id);
    }

    [Command.Internal("PixiEditor.Layer.OpacitySliderDragStarted")]
    public void OpacitySliderDragStarted()
    {
        Owner.DocumentManagerSubViewModel.ActiveDocument?.Tools.UseOpacitySlider();
        Owner.DocumentManagerSubViewModel.ActiveDocument?.EventInlet.OnOpacitySliderDragStarted();
    }

    [Command.Internal("PixiEditor.Layer.OpacitySliderDragged")]
    public void OpacitySliderDragged(double value)
    {
        Owner.DocumentManagerSubViewModel.ActiveDocument?.EventInlet.OnOpacitySliderDragged((float)value);
    }

    [Command.Internal("PixiEditor.Layer.OpacitySliderDragEnded", AnalyticsTrack = true)]
    public void OpacitySliderDragEnded()
    {
        Owner.DocumentManagerSubViewModel.ActiveDocument?.EventInlet.OnOpacitySliderDragEnded();
    }

    [Command.Internal("PixiEditor.Layer.OpacitySliderSet", AnalyticsTrack = true)]
    public void OpacitySliderSet(double value)
    {
        var document = Owner.DocumentManagerSubViewModel.ActiveDocument;

        if (document?.SelectedStructureMember != null)
        {
            document.Operations.SetMemberOpacity(document.SelectedStructureMember.Id, (float)value);
        }
    }

    [Command.Basic("PixiEditor.Layer.DuplicateSelectedMember", "DUPLICATE_SELECTED_LAYER", "DUPLICATE_SELECTED_LAYER",
        Icon = PixiPerfectIcons.DuplicateFile, MenuItemPath = "EDIT/DUPLICATE", MenuItemOrder = 5,
        AnalyticsTrack = true)]
    public void DuplicateMember()
    {
        if (Owner.DocumentManagerSubViewModel.ActiveDocument?.SelectedStructureMember == null)
            return;

        var member = Owner.DocumentManagerSubViewModel.ActiveDocument?.SelectedStructureMember;

        member.Document.Operations.DuplicateMember(member.Id);
    }

    [Command.Basic("PixiEditor.Layer.UnlinkNestedDocument", "UNLINK", "UNLINK_DESCRIPTIVE",
        CanExecute = "PixiEditor.Layer.SelectedMemberIsNestedDocument",
        Icon = PixiPerfectIcons.ChainBreak, AnalyticsTrack = true)]
    public void UnlinkNestedDocument()
    {
        var member = Owner.DocumentManagerSubViewModel.ActiveDocument?.SelectedStructureMember;
        if (member is not NestedDocumentNodeViewModel nestedDocVm)
            return;

        nestedDocVm.Document.Operations.UnlinkNestedDocument(nestedDocVm.Id);
    }

    [Command.Internal("PixiEditor.Layer.CreateNestedFromLayer")]
    public void CreateNestedFromLayer(Guid layerGuid)
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return;

        doc.Operations.CreateNestedDocumentFromMember(layerGuid);
    }

    [Evaluator.CanExecute("PixiEditor.Layer.SelectedMemberIsLayer",
        nameof(DocumentManagerViewModel.ActiveDocument), nameof(DocumentViewModel.SelectedStructureMember))]
    public bool SelectedMemberIsLayer(object property)
    {
        var member = Owner.DocumentManagerSubViewModel.ActiveDocument?.SelectedStructureMember;
        return member is ILayerHandler;
    }


    [Evaluator.CanExecute("PixiEditor.Layer.SelectedMemberIsTransparencyLockable",
        nameof(DocumentManagerViewModel.ActiveDocument), nameof(DocumentViewModel.SelectedStructureMember))]
    public bool SelectedMemberIsTransparencyLockable(object property)
    {
        var member = Owner.DocumentManagerSubViewModel.ActiveDocument?.SelectedStructureMember;
        return member is ITransparencyLockableMember;
    }

    [Evaluator.CanExecute("PixiEditor.Layer.SelectedLayerIsRasterizable",
        nameof(DocumentManagerViewModel.ActiveDocument), nameof(DocumentViewModel.SelectedStructureMember))]
    public bool SelectedLayerIsRasterizable(object property)
    {
        var member = Owner.DocumentManagerSubViewModel.ActiveDocument?.SelectedStructureMember;
        return member is ILayerHandler && member is not IRasterLayerHandler;
    }

    [Evaluator.CanExecute("PixiEditor.Layer.SelectedMemberIsVectorLayer",
        nameof(DocumentManagerViewModel.ActiveDocument), nameof(DocumentViewModel.SelectedStructureMember))]
    public bool SelectedMemberIsVectorLayer(object property)
    {
        var member = Owner.DocumentManagerSubViewModel.ActiveDocument?.SelectedStructureMember;
        return member is IVectorLayerHandler;
    }

    [Evaluator.CanExecute("PixiEditor.Layer.AnySelectedMemberIsVectorLayer",
        nameof(DocumentManagerViewModel.ActiveDocument), nameof(DocumentViewModel.SelectedStructureMember))]
    public bool AnySelectedMemberIsVectorLayer(object property)
    {
        var members = Owner.DocumentManagerSubViewModel.ActiveDocument?.ExtractSelectedLayers();

        if (members is null || members.Count == 0)
            return false;

        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return false;

        foreach (var member in members)
        {
            var handler = doc.StructureHelper.Find(member);
            if (handler is IVectorLayerHandler)
            {
                return true;
            }
        }

        return false;
    }

    [Evaluator.CanExecute("PixiEditor.Layer.AnySelectedLayerIsRasterizable",
        nameof(DocumentManagerViewModel.ActiveDocument), nameof(DocumentViewModel.SelectedStructureMember))]
    public bool AnySelectedMemberIsRasterizable(object property)
    {
        var members = Owner.DocumentManagerSubViewModel.ActiveDocument?.ExtractSelectedLayers();

        if (members is null || members.Count == 0)
            return false;

        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return false;

        foreach (var member in members)
        {
            var handler = doc.StructureHelper.Find(member);
            if (handler is ILayerHandler && handler is not IRasterLayerHandler)
            {
                return true;
            }
        }

        return false;
    }

    [Evaluator.CanExecute("PixiEditor.Layer.SelectedMemberIsNestedDocument",
        nameof(DocumentManagerViewModel.ActiveDocument), nameof(DocumentViewModel.SelectedStructureMember))]
    public bool SelectedMemberIsNestedDocument(object property)
    {
        var member = Owner.DocumentManagerSubViewModel.ActiveDocument?.SelectedStructureMember;
        return member is NestedDocumentNodeViewModel;
    }

    [Evaluator.CanExecute("PixiEditor.Layer.SelectedMemberIsSelectedText",
        nameof(DocumentManagerViewModel.ActiveDocument), nameof(DocumentViewModel.SelectedStructureMember))]
    public bool SelectedMemberIsSelectedText(object property)
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return false;

        var member = doc?.SelectedStructureMember;
        return member is IVectorLayerHandler && doc.TextOverlayViewModel.IsActive &&
               doc.TextOverlayViewModel.CursorPosition != doc.TextOverlayViewModel.SelectionEnd;
    }

    private bool HasSelectedMember(bool above)
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        var member = doc?.SelectedStructureMember;
        if (member is null)
            return false;
        if (above)
        {
            return doc.StructureHelper.GetAboveMember(member.Id, false) is not null;
        }

        return doc.StructureHelper.GetBelowMember(member.Id, false) is not null;
    }

    private void MoveSelectedMember(bool upwards)
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        var member = doc?.SelectedStructureMember;
        if (member is null)
            return;
        var path = doc!.StructureHelper.FindPath(member.Id);
        if (path.Count < 2 || path[1] is not FolderNodeViewModel folderVm)
            return;
        var parent = folderVm;
        if (parent.Children.Count == 0)
            return;
        int curIndex = parent.Children.IndexOf(path[0]);
        if (curIndex < 0)
            return;

        if (upwards)
        {
            if (curIndex == parent.Children.Count - 1)
                return;
            doc.Operations.MoveStructureMember(member.Id, parent.Children[curIndex + 1].Id,
                StructureMemberPlacement.Above);
        }
        else
        {
            if (curIndex == 0)
                return;
            doc.Operations.MoveStructureMember(member.Id, parent.Children[curIndex - 1].Id,
                StructureMemberPlacement.Below);
        }
    }

    [Evaluator.CanExecute("PixiEditor.Layer.ActiveLayerHasMask",
        nameof(ViewModelMain.DocumentManagerSubViewModel.ActiveDocument),
        nameof(ViewModelMain.DocumentManagerSubViewModel.ActiveDocument.SelectedStructureMember),
        nameof(ViewModelMain.DocumentManagerSubViewModel.ActiveDocument.SelectedStructureMember.HasMaskBindable))]
    public bool ActiveMemberHasMask() =>
        Owner.DocumentManagerSubViewModel.ActiveDocument?.SelectedStructureMember?.HasMaskBindable ?? false;

    [Evaluator.CanExecute("PixiEditor.Layer.ActiveLayerHasApplyableMask",
        nameof(ViewModelMain.DocumentManagerSubViewModel.ActiveDocument),
        nameof(ViewModelMain.DocumentManagerSubViewModel.ActiveDocument.SelectedStructureMember),
        nameof(ViewModelMain.DocumentManagerSubViewModel.ActiveDocument.SelectedStructureMember.HasMaskBindable))]
    public bool ActiveMemberHasApplyableMask() =>
        (Owner.DocumentManagerSubViewModel.ActiveDocument?.SelectedStructureMember?.HasMaskBindable ?? false)
        && Owner.DocumentManagerSubViewModel.ActiveDocument?.SelectedStructureMember is IRasterLayerHandler;

    [Evaluator.CanExecute("PixiEditor.Layer.ActiveLayerHasNoMask",
        nameof(ViewModelMain.DocumentManagerSubViewModel.ActiveDocument),
        nameof(ViewModelMain.DocumentManagerSubViewModel.ActiveDocument.SelectedStructureMember),
        nameof(ViewModelMain.DocumentManagerSubViewModel.ActiveDocument.SelectedStructureMember.HasMaskBindable))]
    public bool ActiveLayerHasNoMask() =>
        !Owner.DocumentManagerSubViewModel.ActiveDocument?.SelectedStructureMember?.HasMaskBindable ?? false;

    [Command.Basic("PixiEditor.Layer.CreateMask", "CREATE_MASK", "CREATE_MASK",
        CanExecute = "PixiEditor.Layer.ActiveLayerHasNoMask",
        Icon = PixiPerfectIcons.CreateMask, AnalyticsTrack = true)]
    public void CreateMask()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        var member = doc?.SelectedStructureMember;
        if (member is null || member.HasMaskBindable)
            return;
        doc!.Operations.CreateMask(member);
    }

    [Command.Basic("PixiEditor.Layer.DeleteMask", "DELETE_MASK", "DELETE_MASK",
        CanExecute = "PixiEditor.Layer.ActiveLayerHasMask", Icon = PixiPerfectIcons.Trash, AnalyticsTrack = true)]
    public void DeleteMask()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        var member = doc?.SelectedStructureMember;
        if (member is null || !member.HasMaskBindable)
            return;
        doc!.Operations.DeleteMask(member);
    }

    [Command.Basic("PixiEditor.Layer.ToggleMask", "TOGGLE_MASK", "TOGGLE_MASK",
        CanExecute = "PixiEditor.Layer.ActiveLayerHasMask",
        Icon = PixiPerfectIcons.MaskGhost, AnalyticsTrack = true)]
    public void ToggleMask()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        var member = doc?.SelectedStructureMember;
        if (member is null || !member.HasMaskBindable)
            return;

        member.MaskIsVisibleBindable = !member.MaskIsVisibleBindable;
    }

    [Command.Basic("PixiEditor.Layer.ApplyMask", "APPLY_MASK", "APPLY_MASK",
        CanExecute = "PixiEditor.Layer.ActiveLayerHasApplyableMask", AnalyticsTrack = true)]
    public void ApplyMask()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        var member = doc?.SelectedStructureMember;
        if (member is null || !member.HasMaskBindable)
            return;

        doc!.Operations.ApplyMask(member, doc.AnimationDataViewModel.ActiveFrameBindable);
    }

    [Command.Basic("PixiEditor.Layer.ToggleVisible", "TOGGLE_VISIBILITY", "TOGGLE_VISIBILITY",
        CanExecute = "PixiEditor.HasDocument",
        Icon = PixiPerfectIcons.FileGhost, AnalyticsTrack = true)]
    public void ToggleVisible()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        var member = doc?.SelectedStructureMember;
        if (member is null)
            return;

        member.IsVisibleBindable = !member.IsVisibleBindable;
    }

    [Evaluator.CanExecute("PixiEditor.Layer.HasMemberAbove",
        nameof(DocumentManagerViewModel.ActiveDocument),
        nameof(DocumentViewModel.SelectedStructureMember), nameof(DocumentViewModel.AllChangesSaved))]
    public bool HasMemberAbove(object property) => HasSelectedMember(true);

    [Evaluator.CanExecute("PixiEditor.Layer.HasMemberBelow",
        nameof(DocumentManagerViewModel.ActiveDocument),
        nameof(DocumentViewModel.SelectedStructureMember), nameof(DocumentViewModel.AllChangesSaved))]
    public bool HasMemberBelow(object property) => HasSelectedMember(false);

    [Command.Basic("PixiEditor.Layer.MoveSelectedMemberUpwards", "MOVE_MEMBER_UP", "MOVE_MEMBER_UP_DESCRIPTIVE",
        CanExecute = "PixiEditor.Layer.HasMemberAbove", AnalyticsTrack = true)]
    public void MoveSelectedMemberUpwards() => MoveSelectedMember(true);

    [Command.Basic("PixiEditor.Layer.MoveSelectedMemberDownwards", "MOVE_MEMBER_DOWN", "MOVE_MEMBER_DOWN_DESCRIPTIVE",
        CanExecute = "PixiEditor.Layer.HasMemberBelow", AnalyticsTrack = true)]
    public void MoveSelectedMemberDownwards() => MoveSelectedMember(false);

    /// <summary>
    /// Opens the quick colour correction for one layer, as the button on its row in the layer panel
    /// does. Without a layer id it corrects the selected layer, which is what the menu item and the
    /// shortcut do.
    /// </summary>
    /// <remarks>
    /// Unlike the adjustment menu below, this edits the layer's pixels there and then, and only
    /// inside the selection when there is one. That is the point of it: a filter node covers the
    /// whole layer and lives in the graph, which is more than a quick tweak to one corner needs.
    /// </remarks>
    /// <summary>
    /// Lays every layer that has pixels out in a grid in a new document, each trimmed to its own
    /// content, for use as a sprite sheet. Asks how much gap to leave and remembers the answer.
    /// </summary>
    /// <remarks>
    /// Trimmed, so the cells are as small as the largest sprite needs rather than the size of the
    /// canvas. That loses each layer's position on the canvas, which is the right trade for sprites
    /// but the wrong one for animation frames that have to stay in register.
    /// </remarks>
    [Command.Basic("PixiEditor.Layer.LayersToSpriteAtlas", "LAYERS_TO_SPRITE_ATLAS",
        "LAYERS_TO_SPRITE_ATLAS_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "IMAGE/LAYERS_TO_SPRITE_ATLAS", MenuItemOrder = 80,
        Icon = PixiPerfectIcons.Grid, AnalyticsTrack = true)]
    public async Task LayersToSpriteAtlas()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return;

        InputPopup spacingPrompt = new()
        {
            Title = new LocalizedString("LAYERS_TO_SPRITE_ATLAS"),
            Label = new LocalizedString("SPRITE_ATLAS_SPACING_LABEL"),
            InputText = PixiEditorSettings.Timelapse.SpriteAtlasSpacing.Value.ToString()
        };

        if (await spacingPrompt.ShowDialog<bool>(MainWindow.Current) != true)
            return;

        if (!int.TryParse(spacingPrompt.InputText, out int spacing) || spacing < 0)
            spacing = 0;

        PixiEditorSettings.Timelapse.SpriteAtlasSpacing.Value = spacing;

        DocumentViewModel? atlas = SpriteAtlasRenderer.Render(doc, spacing);
        if (atlas is null)
        {
            NoticeDialog.Show("SPRITE_ATLAS_NOTHING_TO_PACK", "LAYERS_TO_SPRITE_ATLAS");
            return;
        }

        Owner.FileSubViewModel.OpenGeneratedDocument(atlas);
    }

    private readonly ConditionalWeakTable<IDocument, SoloView> soloViews = new();

    /// <summary>
    /// Shows one layer on its own, hiding every other layer, and puts them all back when switched
    /// off. Without a layer id it solos the selected layer, which is what the menu item does.
    /// </summary>
    /// <remarks>
    /// This really does change each layer's visibility rather than filtering what is rendered, so
    /// it lands in the undo history - as one step, not one per layer. The upside is that nothing
    /// about the document is in a state the rest of the editor does not already understand.
    /// </remarks>
    [Command.Basic("PixiEditor.Layer.SoloView", "SOLO_VIEW_LAYER", "SOLO_VIEW_LAYER_DESCRIPTIVE",
        CanExecute = "PixiEditor.HasDocument", Icon = PixiPerfectIcons.Eye, AnalyticsTrack = true)]
    public void SoloViewLayer(Guid memberGuid)
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return;

        IStructureMemberHandler? member = memberGuid == Guid.Empty
            ? doc.SelectedStructureMember
            : doc.StructureHelper.Find(memberGuid);

        if (member is null)
            return;

        // A live tool change would otherwise be left half finished underneath the visibility edits.
        doc.Operations.TryStopToolLinkedExecutor();

        SoloView solo = soloViews.GetValue(doc, _ => new SoloView());

        using var block = doc.Operations.StartChangeBlock();
        solo.Toggle(doc, member.Id);
    }

    [Command.Basic("PixiEditor.Layer.QuickAdjustColors", "QUICK_ADJUST_TITLE", "QUICK_ADJUST_DESCRIPTIVE",
        CanExecute = "PixiEditor.HasDocument", Icon = PixiPerfectIcons.Sun, AnalyticsTrack = true)]
    public async Task QuickAdjustColors(Guid memberGuid)
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return;

        IStructureMemberHandler? member = memberGuid == Guid.Empty
            ? doc.SelectedStructureMember
            : doc.StructureHelper.Find(memberGuid);

        if (member is null || member.IsLockedBindable)
            return;

        // A live tool change would otherwise be left half finished underneath the correction.
        doc.Operations.TryStopToolLinkedExecutor();

        await new QuickAdjustPopup(doc, member).ShowDialog();
    }

    [Command.Basic("PixiEditor.Layer.AdjustColorAdjustments", typeof(ColorAdjustmentsFilterNode), "ADJ_COLOR", "ADJUSTMENT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "LAYER/ADJUSTMENTS/ADJ_COLOR", MenuItemOrder = 40, Icon = PixiPerfectIcons.Sun)]
    [Command.Basic("PixiEditor.Layer.AdjustGrayscale", typeof(GrayscaleNode), "ADJ_GRAYSCALE", "ADJUSTMENT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "LAYER/ADJUSTMENTS/ADJ_GRAYSCALE", MenuItemOrder = 41, Icon = PixiPerfectIcons.Sun)]
    [Command.Basic("PixiEditor.Layer.AdjustInvert", typeof(InvertFilterNode), "ADJ_INVERT", "ADJUSTMENT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "LAYER/ADJUSTMENTS/ADJ_INVERT", MenuItemOrder = 42, Icon = PixiPerfectIcons.Sun)]
    [Command.Basic("PixiEditor.Layer.AdjustSepia", typeof(SepiaFilterNode), "ADJ_SEPIA", "ADJUSTMENT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "LAYER/ADJUSTMENTS/ADJ_SEPIA", MenuItemOrder = 43, Icon = PixiPerfectIcons.Sun)]
    [Command.Basic("PixiEditor.Layer.AdjustBlur", typeof(BlurNode), "ADJ_BLUR", "ADJUSTMENT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "LAYER/ADJUSTMENTS/ADJ_BLUR", MenuItemOrder = 44, Icon = PixiPerfectIcons.Sun)]
    [Command.Basic("PixiEditor.Layer.AdjustShadow", typeof(ShadowNode), "ADJ_SHADOW", "ADJUSTMENT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "LAYER/ADJUSTMENTS/ADJ_SHADOW", MenuItemOrder = 45, Icon = PixiPerfectIcons.Sun)]
    [Command.Basic("PixiEditor.Layer.AdjustGlow", typeof(GlowNode), "ADJ_GLOW", "ADJUSTMENT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "LAYER/ADJUSTMENTS/ADJ_GLOW", MenuItemOrder = 46, Icon = PixiPerfectIcons.Sun)]
    public void AddLayerAdjustment(Type filterNodeType)
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc?.SelectedStructureMember is not { } member)
            return;

        doc.Operations.AddLayerFilter(filterNodeType, member);
        Owner.WindowSubViewModel.ShowDockWindow(NodeGraphDockViewModel.TabId);
    }

    [Command.Basic("PixiEditor.Layer.EffectShadeEdges", typeof(ShadeEdgesNode), "FX_SHADE_EDGES", "IMAGE_EFFECT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "IMAGE/IMAGE_EFFECTS/FX_SHADE_EDGES", MenuItemOrder = 40, Icon = PixiPerfectIcons.Blur)]
    [Command.Basic("PixiEditor.Layer.EffectDither", typeof(DitherNode), "FX_DITHER", "IMAGE_EFFECT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "IMAGE/IMAGE_EFFECTS/FX_DITHER", MenuItemOrder = 41, Icon = PixiPerfectIcons.Blur)]
    [Command.Basic("PixiEditor.Layer.EffectLevels", typeof(LevelsNode), "FX_LEVELS", "IMAGE_EFFECT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "IMAGE/IMAGE_EFFECTS/FX_LEVELS", MenuItemOrder = 42, Icon = PixiPerfectIcons.Blur)]
    [Command.Basic("PixiEditor.Layer.EffectCurves", typeof(CurvesNode), "FX_CURVES", "IMAGE_EFFECT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "IMAGE/IMAGE_EFFECTS/FX_CURVES", MenuItemOrder = 43, Icon = PixiPerfectIcons.Blur)]
    [Command.Basic("PixiEditor.Layer.EffectPosterize", typeof(PosterizationNode), "FX_POSTERIZE", "IMAGE_EFFECT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "IMAGE/IMAGE_EFFECTS/FX_POSTERIZE", MenuItemOrder = 44, Icon = PixiPerfectIcons.Blur)]
    [Command.Basic("PixiEditor.Layer.EffectPixelate", typeof(PixelateNode), "FX_PIXELATE", "IMAGE_EFFECT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "IMAGE/IMAGE_EFFECTS/FX_PIXELATE", MenuItemOrder = 45, Icon = PixiPerfectIcons.Blur)]
    [Command.Basic("PixiEditor.Layer.EffectMotionBlur", typeof(MotionBlurNode), "FX_MOTION_BLUR", "IMAGE_EFFECT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "IMAGE/IMAGE_EFFECTS/FX_MOTION_BLUR", MenuItemOrder = 46, Icon = PixiPerfectIcons.Blur)]
    [Command.Basic("PixiEditor.Layer.EffectZoomBlur", typeof(ZoomBlurNode), "FX_ZOOM_BLUR", "IMAGE_EFFECT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "IMAGE/IMAGE_EFFECTS/FX_ZOOM_BLUR", MenuItemOrder = 47, Icon = PixiPerfectIcons.Blur)]
    [Command.Basic("PixiEditor.Layer.EffectSpinBlur", typeof(SpinBlurNode), "FX_SPIN_BLUR", "IMAGE_EFFECT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "IMAGE/IMAGE_EFFECTS/FX_SPIN_BLUR", MenuItemOrder = 48, Icon = PixiPerfectIcons.Blur)]
    [Command.Basic("PixiEditor.Layer.EffectVignette", typeof(VignetteNode), "FX_VIGNETTE", "IMAGE_EFFECT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "IMAGE/IMAGE_EFFECTS/FX_VIGNETTE", MenuItemOrder = 49, Icon = PixiPerfectIcons.Blur)]
    [Command.Basic("PixiEditor.Layer.EffectChromatic", typeof(ChromaticAberrationNode), "FX_CHROMATIC", "IMAGE_EFFECT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "IMAGE/IMAGE_EFFECTS/FX_CHROMATIC", MenuItemOrder = 50, Icon = PixiPerfectIcons.Blur)]
    [Command.Basic("PixiEditor.Layer.EffectReduceNoise", typeof(ReduceNoiseNode), "FX_REDUCE_NOISE", "IMAGE_EFFECT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "IMAGE/IMAGE_EFFECTS/FX_REDUCE_NOISE", MenuItemOrder = 51, Icon = PixiPerfectIcons.Blur)]
    [Command.Basic("PixiEditor.Layer.EffectPixelGlow", typeof(PixelGlowNode), "FX_PIXEL_GLOW", "IMAGE_EFFECT_DESCRIPTIVE", CanExecute = "PixiEditor.HasDocument",
        MenuItemPath = "IMAGE/IMAGE_EFFECTS/FX_PIXEL_GLOW", MenuItemOrder = 52, Icon = PixiPerfectIcons.Sun)]
    public void AddImageEffect(Type effectNodeType)
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return;

        doc.Operations.AddImageEffect(effectNodeType);
        Owner.WindowSubViewModel.ShowDockWindow(NodeGraphDockViewModel.TabId);
    }

    [Command.Basic("PixiEditor.Layer.MergeSelected", "MERGE_ALL_SELECTED_LAYERS", "MERGE_ALL_SELECTED_LAYERS",
        CanExecute = "PixiEditor.Layer.HasMultipleSelectedMembers", AnalyticsTrack = true)]
    public void MergeSelected()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return;
        var selected = GetSelected();
        if (selected.Count == 0)
            return;
        doc.Operations.MergeStructureMembers(selected);
    }

    [Command.Basic("PixiEditor.Layer.CenterSelectedHorizontally", "CENTER_SELECTED_LAYERS_HORIZONTALLY",
        "CENTER_SELECTED_LAYERS_HORIZONTALLY_DESCRIPTIVE",
        MenuItemPath = "LAYER/ALIGN_SELECTED/CENTER_HORIZONTALLY", MenuItemOrder = 1,
        CanExecute = "PixiEditor.Layer.HasMultipleFlattenedSelectedMembers", Icon = PixiPerfectIcons.AlignHorizontalJustifyCenter,
        AnalyticsTrack = true)]
    public void CenterSelectedLayersHorizontally()
    {
        Align(HorizontalAlignment.Center, VerticalAlignment.Unaligned);
    }

    [Command.Basic("PixiEditor.Layer.CenterSelectedVertically", "CENTER_SELECTED_LAYERS_VERTICALLY",
        "CENTER_SELECTED_LAYERS_VERTICALLY_DESCRIPTIVE",
        MenuItemPath = "LAYER/ALIGN_SELECTED/CENTER_VERTICALLY", MenuItemOrder = 2,
        CanExecute = "PixiEditor.Layer.HasMultipleFlattenedSelectedMembers", Icon = PixiPerfectIcons.AlignVerticalJustifyCenter,
        AnalyticsTrack = true)]
    public void CenterSelectedLayersVertically()
    {
        Align(HorizontalAlignment.Unaligned, VerticalAlignment.Center);
    }

    [Command.Basic("PixiEditor.Layer.AlignRightSelectedLayers", "ALIGN_RIGHT_SELECTED_LAYERS",
        "ALIGN_RIGHT_SELECTED_LAYERS_DESCRIPTIVE",
        MenuItemPath = "LAYER/ALIGN_SELECTED/RIGHT", MenuItemOrder = 3,
        CanExecute = "PixiEditor.Layer.HasMultipleFlattenedSelectedMembers", Icon = PixiPerfectIcons.AlignHorizontalJustifyEnd,
        AnalyticsTrack = true)]
    public void AlignRightSelectedLayers()
    {
        Align(HorizontalAlignment.Right, VerticalAlignment.Unaligned);
    }

    [Command.Basic("PixiEditor.Layer.AlignLeftSelectedLayers", "ALIGN_LEFT_SELECTED_LAYERS",
        "ALIGN_LEFT_SELECTED_LAYERS_DESCRIPTIVE",
        MenuItemPath = "LAYER/ALIGN_SELECTED/LEFT", MenuItemOrder = 4,
        CanExecute = "PixiEditor.Layer.HasMultipleFlattenedSelectedMembers", Icon = PixiPerfectIcons.AlignHorizontalJustifyStart,
        AnalyticsTrack = true)]
    public void AlignLeftSelectedLayers()
    {
        Align(HorizontalAlignment.Left, VerticalAlignment.Unaligned);
    }

    [Command.Basic("PixiEditor.Layer.AlignTopSelectedLayers", "ALIGN_TOP_SELECTED_LAYERS",
        "ALIGN_TOP_SELECTED_LAYERS_DESCRIPTIVE",
        MenuItemPath = "LAYER/ALIGN_SELECTED/TOP", MenuItemOrder = 5,
        CanExecute = "PixiEditor.Layer.HasMultipleFlattenedSelectedMembers", Icon = PixiPerfectIcons.AlignVerticalJustifyStart,
        AnalyticsTrack = true)]
    public void AlignTopSelectedLayers()
    {
        Align(HorizontalAlignment.Unaligned, VerticalAlignment.Top);
    }


    [Command.Basic("PixiEditor.Layer.AlignBottomSelectedLayers", "ALIGN_BOTTOM_SELECTED_LAYERS",
        "ALIGN_BOTTOM_SELECTED_LAYERS_DESCRIPTIVE",
        MenuItemPath = "LAYER/ALIGN_SELECTED/BOTTOM", MenuItemOrder = 6,
        CanExecute = "PixiEditor.Layer.HasMultipleFlattenedSelectedMembers", Icon = PixiPerfectIcons.AlignVerticalJustifyEnd,
        AnalyticsTrack = true)]
    public void AlignBottomSelectedLayers()
    {
        Align(HorizontalAlignment.Unaligned, VerticalAlignment.Bottom);
    }

    [Command.Basic("PixiEditor.Layer.AlignSpreadHorizontalSelectedLayer", "ALIGN_SPREAD_HORIZONTAL_SELECTED_LAYERS",
        "ALIGN_SPREAD_HORIZONTAL_SELECTED_LAYERS_DESCRIPTIVE",
        MenuItemPath = "LAYER/ALIGN_SELECTED/SPREAD_HORIZONTALLY", MenuItemOrder = 7,
        CanExecute = "PixiEditor.Layer.HasMultipleFlattenedSelectedMembers", Icon = PixiPerfectIcons.AlignHorizontalSpaceBetween,
        AnalyticsTrack = true)]
    public void AlignSpreadHorizontalSelectedLayer()
    {
        Align(HorizontalAlignment.Spread, VerticalAlignment.Unaligned);
    }

    [Command.Basic("PixiEditor.Layer.AlignSpreadVerticalSelectedLayer", "ALIGN_SPREAD_VERTICAL_SELECTED_LAYERS",
        "ALIGN_SPREAD_VERTICAL_SELECTED_LAYERS_DESCRIPTIVE",
        MenuItemPath = "LAYER/ALIGN_SELECTED/SPREAD_VERTICALLY", MenuItemOrder = 8,
        CanExecute = "PixiEditor.Layer.HasMultipleFlattenedSelectedMembers", Icon = PixiPerfectIcons.AlignVerticalSpaceBetween,
        AnalyticsTrack = true)]
    public void AlignSpreadVerticalSelectedLayer()
    {
        Align(HorizontalAlignment.Unaligned, VerticalAlignment.Spread);
    }

    public void Align(HorizontalAlignment horizontal, VerticalAlignment vertical)
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return;
        var selected = GetSelected();

        if (selected.Count == 0)
            return;

        doc.Operations.AlignSelectedLayers(selected, horizontal, vertical);
    }

    public void MergeSelectedWith(bool above)
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        var member = doc?.SelectedStructureMember;
        if (doc is null || member is null)
            return;

        IStructureMemberHandler? nextMergeableMember = doc.StructureHelper.GetAboveMember(member.Id, true);
        IStructureMemberHandler? previousMergeableMember = doc.StructureHelper.GetBelowMember(member.Id, true);

        if (!above && previousMergeableMember is null)
            return;
        if (above && nextMergeableMember is null)
            return;

        IStructureMemberHandler mergeInto = above ? nextMergeableMember! : previousMergeableMember!;

        // The layer being merged into keeps its name: merging down into "Terrain" leaves "Terrain".
        doc.Operations.MergeStructureMembers([member.Id, mergeInto.Id], mergeInto.Id);
    }

    [Command.Basic("PixiEditor.Layer.MergeWithAbove", "MERGE_WITH_ABOVE", "MERGE_WITH_ABOVE_DESCRIPTIVE",
        CanExecute = "PixiEditor.Layer.HasMemberAbove", AnalyticsTrack = true)]
    public void MergeWithAbove() => MergeSelectedWith(true);

    [Command.Basic("PixiEditor.Layer.MergeWithBelow", "MERGE_WITH_BELOW", "MERGE_WITH_BELOW_DESCRIPTIVE",
        CanExecute = "PixiEditor.Layer.HasMemberBelow",
        Icon = PixiPerfectIcons.Merge, AnalyticsTrack = true)]
    public void MergeWithBelow() => MergeSelectedWith(false);

    [Evaluator.CanExecute("PixiEditor.Layer.ReferenceLayerExists",
        nameof(ViewModelMain.DocumentManagerSubViewModel),
        nameof(ViewModelMain.DocumentManagerSubViewModel.ActiveDocument),
        nameof(ViewModelMain.DocumentManagerSubViewModel.ActiveDocument.ReferenceLayerViewModel),
        nameof(ViewModelMain.DocumentManagerSubViewModel.ActiveDocument.ReferenceLayerViewModel.ReferenceTexture))]
    public bool ReferenceLayerExists() =>
        Owner.DocumentManagerSubViewModel.ActiveDocument?.ReferenceLayerViewModel.ReferenceTexture is not null;

    [Evaluator.CanExecute("PixiEditor.Layer.ReferenceLayerDoesntExist",
        nameof(ViewModelMain.DocumentManagerSubViewModel),
        nameof(ViewModelMain.DocumentManagerSubViewModel.ActiveDocument),
        nameof(ViewModelMain.DocumentManagerSubViewModel.ActiveDocument.ReferenceLayerViewModel.ReferenceTexture))]
    public bool ReferenceLayerDoesntExist() =>
        Owner.DocumentManagerSubViewModel.ActiveDocument is not null &&
        Owner.DocumentManagerSubViewModel.ActiveDocument.ReferenceLayerViewModel.ReferenceTexture is null;

    [Command.Basic("PixiEditor.Layer.ImportReferenceLayer", "ADD_REFERENCE_LAYER", "ADD_REFERENCE_LAYER",
        CanExecute = "PixiEditor.Layer.ReferenceLayerDoesntExist",
        Icon = PixiPerfectIcons.AddReference, AnalyticsTrack = true)]
    public async Task ImportReferenceLayer()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return;

        string path = await OpenReferenceLayerFilePicker();
        if (path is null)
            return;

        Surface bitmap;
        try
        {
            bitmap = Surface.Load(path);
        }
        catch (RecoverableException e)
        {
            NoticeDialog.Show(title: "ERROR", message: e.DisplayMessage);
            return;
        }
        catch (ArgumentException e)
        {
            NoticeDialog.Show(title: "ERROR", message: e.Message);
            return;
        }
        catch (Exception e)
        {
            CrashHelper.SendExceptionInfo(e);
            NoticeDialog.Show(title: "ERROR", message: e.Message);
            return;
        }

        byte[] bytes = bitmap.ToByteArray();

        bitmap.Dispose();

        VecI size = new VecI(bitmap.Size.X, bitmap.Size.Y);

        doc.Operations.ImportReferenceLayer(
            [.. bytes],
            size);
    }

    private async Task<string> OpenReferenceLayerFilePicker()
    {
        var imagesFilter = new FileTypeDialogDataSet(FileTypeDialogDataSet.SetKind.Image).GetFormattedTypes(true);
        if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                var filePicker = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions()
                    {
                        Title = new LocalizedString("REFERENCE_LAYER_PATH"), FileTypeFilter = imagesFilter,
                    });

                if (filePicker is null || filePicker.Count == 0)
                    return null;

                return filePicker[0].Path.LocalPath;
            }
            catch (COMException e)
            {
                NoticeDialog.Show(title: "ERROR", message: new LocalizedString("COM_EXCEPTION_ERROR", e.Message));
            }
        }

        return null;
    }

    [Command.Basic("PixiEditor.Layer.ResetTransformForSelected", "RESET_TRANSFORM", "RESET_TRANSFORM_DESCRIPTIVE",
        CanExecute = "PixiEditor.Layer.HasTransformableMembers", Icon = PixiPerfectIcons.Reset, AnalyticsTrack = true)]
    public void ResetTransform()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return;

        var selectedMembers = doc.ExtractSelectedLayers();
        if (selectedMembers.Count == 0)
            return;

        var block = doc.Operations.StartChangeBlock();

        foreach (var member in selectedMembers)
        {
            doc.Operations.ResetTransform(member);
        }

        block.Dispose();
    }

    [Command.Internal("PixiEditor.Layer.ResetTransformForMember",
        CanExecute = "PixiEditor.Layer.IsMemberTransformable", AnalyticsTrack = true)]
    public void ResetTransform(Guid memberGuid)
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return;

        doc.Operations.ResetTransform(memberGuid);
    }

    [Command.Basic("PixiEditor.Layer.DeleteReferenceLayer", "DELETE_REFERENCE_LAYER", "DELETE_REFERENCE_LAYER",
        CanExecute = "PixiEditor.Layer.ReferenceLayerExists", Icon = PixiPerfectIcons.Trash, AnalyticsTrack = true)]
    public void DeleteReferenceLayer()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return;

        doc.Operations.DeleteReferenceLayer();
    }

    [Command.Basic("PixiEditor.Layer.TransformReferenceLayer", "TRANSFORM_REFERENCE_LAYER", "TRANSFORM_REFERENCE_LAYER",
        CanExecute = "PixiEditor.Layer.ReferenceLayerExists",
        Icon = PixiPerfectIcons.Crop, AnalyticsTrack = true)]
    public void TransformReferenceLayer()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return;

        doc.Operations.TransformReferenceLayer();
    }

    [Command.Basic("PixiEditor.Layer.ToggleReferenceLayerTopMost", "TOGGLE_REFERENCE_LAYER_POS",
        "TOGGLE_REFERENCE_LAYER_POS_DESCRIPTIVE", CanExecute = "PixiEditor.Layer.ReferenceLayerExists",
        IconEvaluator = "PixiEditor.Layer.ToggleReferenceLayerTopMostIcon", AnalyticsTrack = true)]
    public void ToggleReferenceLayerTopMost()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return;

        doc.ReferenceLayerViewModel.IsTopMost = !doc.ReferenceLayerViewModel.IsTopMost;
    }

    [Command.Basic("PixiEditor.Layer.ResetReferenceLayerPosition", "RESET_REFERENCE_LAYER_POS",
        "RESET_REFERENCE_LAYER_POS", CanExecute = "PixiEditor.Layer.ReferenceLayerExists",
        Icon = PixiPerfectIcons.Reset, AnalyticsTrack = true)]
    public void ResetReferenceLayerPosition()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null)
            return;

        doc.Operations.ResetReferenceLayerPosition();
    }

    [Command.Basic("PixiEditor.Layer.Rasterize", "RASTERIZE_ACTIVE_LAYER", "RASTERIZE_ACTIVE_LAYER_DESCRIPTIVE",
        CanExecute = "PixiEditor.Layer.AnySelectedLayerIsRasterizable",
        Icon = PixiPerfectIcons.LowresCircle, MenuItemPath = "LAYER/RASTERIZE_ACTIVE_LAYER",
        AnalyticsTrack = true)]
    public void RasterizeActiveLayer()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        var selectedMembers = doc.ExtractSelectedLayers();
        if (selectedMembers.Count == 0)
            return;

        var block = doc.Operations.StartChangeBlock();

        foreach (var member in selectedMembers)
        {
            doc!.Operations.Rasterize(member);
        }

        block.Dispose();
    }


    [Command.Basic("PixiEditor.Layer.ConvertToCurve", "CONVERT_TO_CURVE", "CONVERT_TO_CURVE_DESCRIPTIVE",
        CanExecute = "PixiEditor.Layer.AnySelectedMemberIsVectorLayer",
        MenuItemPath = "LAYER/CONVERT_TO_CURVE", AnalyticsTrack = true)]
    public void ConvertActiveLayersToCurve()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        var selectedMembers = doc.ExtractSelectedLayers();
        if (selectedMembers.Count == 0)
            return;

        var block = doc.Operations.StartChangeBlock();

        foreach (var member in selectedMembers)
        {
            doc!.Operations.ConvertToCurve(member);
        }

        block.Dispose();
    }

    [Command.Basic("PixiEditor.Layer.SeparateShapes", "SEPARATE_SHAPES", "SEPARATE_SHAPES_DESCRIPTIVE",
        CanExecute = "PixiEditor.Layer.AnySelectedMemberIsVectorLayer",
        MenuItemPath = "LAYER/SEPARATE_SHAPES", AnalyticsTrack = true)]
    public void SeparateShapes()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        var selectedMembers = doc.ExtractSelectedLayers();
        if (selectedMembers.Count == 0)
            return;

        var block = doc.Operations.StartChangeBlock();

        foreach (var member in selectedMembers)
        {
            doc!.Operations.SeparateShapes(member);
        }

        block.Dispose();
    }

    [Command.Basic("PixiEditor.Layer.ExtractSelectedText", "EXTRACT_SELECTED_TEXT", "EXTRACT_SELECTED_TEXT_DESCRIPTIVE",
        CanExecute = "PixiEditor.Layer.SelectedMemberIsSelectedText",
        Key = Key.X, Modifiers = KeyModifiers.Control | KeyModifiers.Shift,
        Parameter = false,
        ShortcutContexts = [typeof(ViewportWindowViewModel), typeof(TextOverlay)],
        MenuItemPath = "LAYER/EXTRACT_SELECTED_TEXT", AnalyticsTrack = true)]
    [Command.Basic("PixiEditor.Layer.ExtractSelectedCharacters", "EXTRACT_SELECTED_CHARACTERS",
        "EXTRACT_SELECTED_CHARACTERS_DESCRIPTIVE",
        CanExecute = "PixiEditor.Layer.SelectedMemberIsSelectedText",
        Key = Key.X, Modifiers = KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt,
        Parameter = true,
        ShortcutContexts = [typeof(ViewportWindowViewModel), typeof(TextOverlay)],
        MenuItemPath = "LAYER/EXTRACT_SELECTED_CHARACTERS", AnalyticsTrack = true)]
    public void ExtractSelectedText(bool extractEachCharacter)
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        var member = doc?.SelectedStructureMember;
        if (member is null)
            return;

        if (member is not VectorLayerNodeViewModel vectorLayer)
            return;

        int startIndex = doc.TextOverlayViewModel.CursorPosition;
        int endIndex = doc.TextOverlayViewModel.SelectionEnd;

        doc!.Operations.ExtractSelectedText(vectorLayer.Id, startIndex, endIndex, extractEachCharacter);
    }

    [Evaluator.Icon("PixiEditor.Layer.ToggleReferenceLayerTopMostIcon")]
    public IImage GetAboveEverythingReferenceLayerIcon()
    {
        var doc = Owner.DocumentManagerSubViewModel.ActiveDocument;
        if (doc is null || doc.ReferenceLayerViewModel.IsTopMost)
        {
            return PixiPerfectIconExtensions.ToIcon(PixiPerfectIcons.LayersTop);
        }

        return PixiPerfectIconExtensions.ToIcon(PixiPerfectIcons.LayersBottom, 18, 180);
    }
}
