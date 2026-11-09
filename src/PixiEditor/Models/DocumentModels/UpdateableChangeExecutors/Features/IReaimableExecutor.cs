namespace PixiEditor.Models.DocumentModels.UpdateableChangeExecutors.Features;

/// <summary>
/// An executor that treats a fresh press of its own tool as adjusting what it is already doing,
/// rather than as the start of a new use of the tool.
/// </summary>
public interface IReaimableExecutor : IExecutorFeature;
