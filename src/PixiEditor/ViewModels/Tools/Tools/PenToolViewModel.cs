using Avalonia.Input;
using Drawie.Numerics;
using PixiEditor.Models.Commands.Attributes.Commands;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.UI.Common.Localization;
using PixiEditor.ViewModels.Tools.ToolSettings;
using PixiEditor.ViewModels.Tools.ToolSettings.Settings;
using PixiEditor.ViewModels.Tools.ToolSettings.Toolbars;

namespace PixiEditor.ViewModels.Tools.Tools
{
    [Command.Tool(Key = Key.B)]
    internal class PenToolViewModel : BrushBasedToolViewModel, IPenToolHandler
    {
        public override string ToolNameLocalizationKey => "PEN_TOOL";

        public PenToolViewModel()
        {
            ActionDisplay = new LocalizedString("PEN_TOOL_ACTION_DISPLAY");
            ViewModelMain.Current.ToolsSubViewModel.SelectedToolChanged += SelectedToolChanged;

            SizeSettingViewModel? sizeSetting = SizeSetting();
            if (sizeSetting is not null)
            {
                sizeSetting.ValueChanged += OnSizeChanged;
            }
        }

        public override LocalizedString Tooltip => new LocalizedString("PEN_TOOL_TOOLTIP", Shortcut);

        [Settings.Bool("PIXEL_PERFECT_SETTING", Notify = nameof(PixelPerfectChanged), ExposedByDefault = false)]
        public bool PixelPerfectEnabled => GetValue<bool>();

        public override string DefaultIcon => PixiPerfectIcons.Pen;

        public override void KeyChanged(bool ctrlIsDown, bool shiftIsDown, bool altIsDown, Key argsKey)
        {
            if (shiftIsDown)
            {
                ActionDisplay = new LocalizedString("PEN_TOOL_ACTION_DISPLAY_STRAIGHT_LINE");
            }
            else
            {
                ActionDisplay = new LocalizedString("PEN_TOOL_ACTION_DISPLAY");
            }
        }

        protected override Toolbar CreateToolbar()
        {
            return ToolbarFactory.Create<PenToolViewModel, BrushToolbar>(this);
        }

        public override void UseTool(VecD pos)
        {
            EnsureBrushShapeSettings();
            ViewModelMain.Current?.DocumentManagerSubViewModel.ActiveDocument?.Tools.UsePenTool();
        }

        private void SelectedToolChanged(object sender, SelectedToolEventArgs e)
        {
            if (e.NewTool == this)
            {
                ApplyPixelPerfectToSize();
            }
        }

        private void PixelPerfectChanged()
        {
            ApplyPixelPerfectToSize();
        }

        /// <summary>
        /// Pixel perfect draws a single pixel, so turning it on takes the size down to 1. It sets
        /// the size rather than pinning it: a pinned setting swallows every later write while still
        /// looking editable, which is what left the size field dead with no way back. Raising the
        /// size now simply switches pixel perfect off instead, so the field always does something.
        /// </summary>
        private void ApplyPixelPerfectToSize()
        {
            SizeSettingViewModel? sizeSetting = SizeSetting();
            if (sizeSetting is null)
            {
                return;
            }

            // Both of these undo a build that did pin the size, so an editor already in that state
            // recovers as soon as the pen is picked up again.
            sizeSetting.ResetOverwrite();
            sizeSetting.IsEnabled = true;

            if (PixelPerfectEnabled && Math.Abs(sizeSetting.Value - 1) > double.Epsilon)
            {
                sizeSetting.Value = 1d;
            }
        }

        /// <summary>
        /// Asking for a bigger brush is a clear enough intention to take pixel perfect off, which
        /// is the only thing holding the size at one.
        /// </summary>
        private void OnSizeChanged(object? sender, SettingValueChangedEventArgs<double> e)
        {
            if (!PixelPerfectEnabled || Math.Abs(e.NewValue - 1) <= double.Epsilon)
            {
                return;
            }

            BoolSettingViewModel? pixelPerfect = PixelPerfectSetting();
            if (pixelPerfect is not null)
            {
                pixelPerfect.Value = false;
            }
        }

        private SizeSettingViewModel? SizeSetting() =>
            Toolbar is BrushToolbar toolbar
                ? toolbar.Settings.FirstOrDefault(x => x.Name == nameof(toolbar.ToolSize)) as SizeSettingViewModel
                : null;

        private BoolSettingViewModel? PixelPerfectSetting() =>
            Toolbar?.Settings.FirstOrDefault(x => x.Name == nameof(PixelPerfectEnabled)) as BoolSettingViewModel;
    }
}
