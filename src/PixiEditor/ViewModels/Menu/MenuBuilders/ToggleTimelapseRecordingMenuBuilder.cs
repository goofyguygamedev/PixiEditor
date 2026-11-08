using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using PixiEditor.Helpers.Extensions;
using PixiEditor.UI.Common.Controls;
using PixiEditor.UI.Common.Fonts;
using PixiEditor.UI.Common.Localization;

namespace PixiEditor.ViewModels.Menu.MenuBuilders;

/// <summary>
/// Puts timelapse recording in the File menu as a checkbox rather than a plain command.
/// </summary>
/// <remarks>
/// Recording is on by default, so a stateless menu entry labelled "Record timelapse" was actively
/// misleading: pressing it switched recording off and looked like it had done nothing at all. A
/// checkbox shows what the current state actually is.
/// </remarks>
internal class ToggleTimelapseRecordingMenuBuilder : MenuItemBuilder
{
    private const string ToggleKey = "TIMELAPSE_TOGGLE";

    /// <summary>The item this one is inserted ahead of, to keep it beside the export entry.</summary>
    private const string AnchorKey = "TIMELAPSE_EXPORT_REPLAY";

    public override void ModifyMenuTree(ICollection<MenuItem> tree)
    {
        if (!TryFindMenuItem(tree, "FILE", out MenuItem? fileItem))
            return;

        ToggleableMenuItem recordItem = new();
        Translator.SetKey(recordItem, ToggleKey);
        recordItem.Icon = new Image()
        {
            Source = PixiPerfectIconExtensions.ToIcon(PixiPerfectIcons.Camera),
            Width = Models.Commands.XAML.Menu.IconDimensions,
            Height = Models.Commands.XAML.Menu.IconDimensions
        };

        BindItem(recordItem);
        InsertBeforeExport(fileItem!.Items, recordItem);
    }

    public override void ModifyMenuTree(ICollection<NativeMenuItem> tree)
    {
        if (!TryFindMenuItem(tree, "FILE", out NativeMenuItem? fileItem))
            return;

        NativeMenuItem recordItem = new() { ToggleType = MenuItemToggleType.CheckBox };
        Translator.SetKey(recordItem, ToggleKey);
        recordItem.Icon = PixiPerfectIconExtensions.ToIcon(PixiPerfectIcons.Camera).ToBitmap(IconDimensions);

        BindItem(recordItem);
        fileItem!.Menu.Items.Add(recordItem);
    }

    /// <summary>
    /// Keeps the toggle next to "Export timelapse" instead of appending it after Exit, which is
    /// where a plain Add would land it.
    /// </summary>
    private static void InsertBeforeExport(ItemCollection items, Control item)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is Control control && Translator.GetKey(control) == AnchorKey)
            {
                items.Insert(i, item);
                return;
            }
        }

        items.Add(item);
    }

    private static void BindItem(ToggleableMenuItem recordItem)
    {
        recordItem.Bind(ToggleableMenuItem.IsCheckedProperty,
            new Binding("TimelapseSubViewModel.IsRecording")
            {
                Source = ViewModelMain.Current, Mode = BindingMode.TwoWay
            });
    }

    private static void BindItem(NativeMenuItem recordItem)
    {
        recordItem.Bind(NativeMenuItem.IsCheckedProperty,
            new Binding("TimelapseSubViewModel.IsRecording") { Source = ViewModelMain.Current });

        recordItem.Command = new RelayCommand(() =>
            ViewModelMain.Current.TimelapseSubViewModel.ToggleRecording());
    }
}
