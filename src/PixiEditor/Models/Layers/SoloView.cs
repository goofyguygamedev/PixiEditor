using PixiEditor.Models.Handlers;

namespace PixiEditor.Models.Layers;

/// <summary>
/// Shows one layer on its own and remembers what was visible beforehand, so switching it off puts
/// the document back the way the artist left it. One of these belongs to each document.
/// </summary>
internal class SoloView
{
    private Guid? soloed;
    private readonly Dictionary<Guid, bool> visibilityBefore = new();

    public bool IsSoloing(Guid member) => soloed == member;

    /// <summary>
    /// Solos the member, or puts everything back if it is already the one being soloed. Soloing a
    /// second layer restores first, so the saved visibility is always the artist's own and never a
    /// previous solo's handiwork.
    /// </summary>
    public void Toggle(IDocument document, Guid member)
    {
        bool wasAlreadySoloed = soloed == member;

        Restore(document);

        if (!wasAlreadySoloed)
        {
            Show(document, member);
        }
    }

    public void Restore(IDocument document)
    {
        foreach ((Guid id, bool wasVisible) in visibilityBefore)
        {
            IStructureMemberHandler? member = document.StructureHelper.Find(id);
            if (member is not null && member.IsVisibleBindable != wasVisible)
            {
                member.IsVisibleBindable = wasVisible;
            }
        }

        visibilityBefore.Clear();
        soloed = null;
    }

    private void Show(IDocument document, Guid member)
    {
        // The folders a layer sits in have to stay visible too, or soloing something nested just
        // hides everything.
        HashSet<Guid> keepVisible = [member];
        foreach (IStructureMemberHandler parent in document.StructureHelper.GetParents(member))
        {
            keepVisible.Add(parent.Id);
        }

        foreach (IStructureMemberHandler other in document.StructureHelper.GetAllMembers())
        {
            visibilityBefore[other.Id] = other.IsVisibleBindable;

            bool shouldBeVisible = keepVisible.Contains(other.Id);
            if (other.IsVisibleBindable != shouldBeVisible)
            {
                other.IsVisibleBindable = shouldBeVisible;
            }
        }

        soloed = member;
    }
}
