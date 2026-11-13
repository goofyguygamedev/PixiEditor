using Drawie.Backend.Core.Numerics;

namespace PixiEditor.Models.Handlers.Tools;

/// <summary>
/// What a shape tool remembers about the shape it just finished, so undo can put it back as
/// something still being adjusted rather than simply removing it.
/// </summary>
/// <remarks>
/// Only the corners are kept. The stroke, fill and width live on the toolbar and are still there
/// after the shape is applied, so the shape can be rebuilt from the corners and whatever the
/// toolbar currently says - which also means changing a colour before reopening is respected.
/// </remarks>
/// <param name="LayerId">The layer the shape was drawn on. A reopen on any other layer is wrong.</param>
/// <param name="Corners">Where the shape was when it was applied.</param>
/// <param name="Frame">The animation frame it was drawn on.</param>
internal readonly record struct ShapeReopenData(Guid LayerId, ShapeCorners Corners, int Frame);
