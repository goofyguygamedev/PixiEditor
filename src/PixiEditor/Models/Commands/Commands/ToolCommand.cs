using PixiEditor.Models.Handlers;

namespace PixiEditor.Models.Commands.Commands;

internal partial class Command
{
    /// <remarks>
    /// <paramref name="owner"/> is the controller this command belongs to. It matters because
    /// <see cref="CommandController.Current"/> is assigned with <c>??=</c>, so the first controller
    /// ever constructed keeps it for the lifetime of the process. Reading the evaluators off
    /// <c>Current</c> therefore reached a *different* controller's dictionary whenever more than one
    /// exists - and if that other controller had not finished loading its evaluators yet, the lookup
    /// below threw KeyNotFoundException. Taking them from the controller doing the initialising is
    /// identical in the app, which only ever builds one.
    /// </remarks>
    internal class ToolCommand(IToolsHandler handler, bool transient, CommandController? owner = null)
        : Command(transient ? handler.SetToolTransient : handler.SetTool,
            (owner ?? CommandController.Current).CanExecuteEvaluators["PixiEditor.HasDocument"])
    {
        public Type ToolType { get; init; }
        public IToolHandler? Handler { get; init; }
        public bool TransientImmediate { get; init; } = false;

        public override object GetParameter() => Handler != null ? Handler : ToolType;
    }
}
