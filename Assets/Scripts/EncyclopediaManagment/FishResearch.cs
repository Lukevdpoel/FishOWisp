using System;

// Single source of truth for a fish's research checklist, so the notebook UI and any progress
// readout can't drift. Four items for now: catch 10×/20× and zoom 10×/20× (zoom = right-mouse aim
// lock on a fish, see FishResearchScanner). The name-row icon changes by Tier: tier 1 at >=50%
// completion, tier 2 at 100%.
public static class FishResearch
{
    // Order matters: the UI lays these out as a 2x2 grid — rows = {Catch, Zoom}, columns = {10x, 20x}.
    public enum Item { Catch10, Catch20, Zoom10, Zoom20 }

    public static readonly Item[] AllItems =
    {
        Item.Catch10, Item.Catch20, Item.Zoom10, Item.Zoom20
    };

    public const int ItemCount = 4;

    // True when the given checklist item is complete for this entry.
    public static bool IsComplete(FishEncyclopediaEntry entry, Item item)
    {
        if (entry == null) return false;
        switch (item)
        {
            case Item.Catch10: return entry.hasCaught >= 10;
            case Item.Catch20: return entry.hasCaught >= 20;
            case Item.Zoom10:  return entry.zoomResearchCount >= 10;
            case Item.Zoom20:  return entry.zoomResearchCount >= 20;
            default:           return false;
        }
    }

    // Number of completed checklist items (0..ItemCount).
    public static int CompletedCount(FishEncyclopediaEntry entry)
    {
        int n = 0;
        for (int i = 0; i < AllItems.Length; i++)
            if (IsComplete(entry, AllItems[i])) n++;
        return n;
    }

    // Completion as 0..1.
    public static float Percent(FishEncyclopediaEntry entry)
        => ItemCount > 0 ? (float)CompletedCount(entry) / ItemCount : 0f;

    // Icon tier shown next to the fish name: 0 = below 50% (no icon), 1 = >=50%, 2 = 100%.
    public static int Tier(FishEncyclopediaEntry entry)
    {
        int done = CompletedCount(entry);
        if (done >= ItemCount) return 2;
        if (done * 2 >= ItemCount) return 1; // >=50%
        return 0;
    }
}
