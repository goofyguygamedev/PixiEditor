using System.Reflection;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using PixiEditor.Models.Commands.Attributes.Commands;
using PixiEditor.ViewModels.SubViewModels;

namespace PixiEditor.Tests;

/// <summary>
/// Covers the Edit menu entry that pastes the clipboard into a new document. It delegates to the
/// File menu's existing command, so what can break is the registration around it: a menu path, a
/// localization key, or an order that collides with a neighbour.
/// </summary>
public class PasteAsNewImageTests : FullPixiEditorTest
{
    [AvaloniaFact]
    public void ItSitsInTheEditMenuWithTheOtherPasteCommands()
    {
        Command.BasicAttribute attribute = PasteAsNewImageAttribute();

        Assert.Equal("EDIT/PASTE_AS_NEW_IMAGE", attribute.MenuItemPath);

        // Paste is 4 and Delete selected is 6, so this has to land between them.
        Assert.Equal(5, attribute.MenuItemOrder);
    }

    [AvaloniaFact]
    public void ItsNameAndDescriptionAreRealLocalizationKeys()
    {
        Command.BasicAttribute attribute = PasteAsNewImageAttribute();
        HashSet<string> keys = EnglishKeys();

        Assert.Contains(attribute.DisplayName.Key, keys);
        Assert.Contains(attribute.Description.Key, keys);
    }

    [AvaloniaFact]
    public void ItIsGreyedOutUntilThereIsAnImageOnTheClipboard()
    {
        Command.BasicAttribute attribute = PasteAsNewImageAttribute();

        // Without a CanExecute evaluator the command controller throws at startup, so this is also
        // a guard against the app refusing to launch.
        Assert.Equal("PixiEditor.Clipboard.CanPasteFromClipboard", attribute.CanExecute);
    }

    private static Command.BasicAttribute PasteAsNewImageAttribute()
    {
        MethodInfo method = typeof(ClipboardViewModel)
            .GetMethod(nameof(ClipboardViewModel.PasteAsNewImage))!;

        return method.GetCustomAttributes<Command.BasicAttribute>()
            .Single(attribute => attribute.InternalName == "PixiEditor.Clipboard.PasteAsNewImage");
    }

    private static HashSet<string> EnglishKeys()
    {
        using Stream stream = AssetLoader.Open(new Uri("avares://PixiEditor/Data/Localization/Languages/en.json"));
        using JsonDocument document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet();
    }
}
