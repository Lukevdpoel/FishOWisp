using System;
using System.Collections.Generic;
using System.IO;
using Sirenix.OdinInspector;
using UnityEngine;

// Holds the player's owned bobbers/lures and the currently equipped one.
// Bobbers are durable gear (no counts, no consumption) so this is intentionally
// lighter than BaitInventory. Swap to FishingLine happens in BobberInventory's
// own listener so any caller of SetSelectedBobber updates the rod automatically.
//
// Ownership persists to bobbers.json (by stable BobberItem.id, never object refs —
// instanceIDs mis-resolve in builds) so shop-bought tackle survives scene loads and
// quitting. On a fresh save the serialized `ownedBobbers` list is the starting grant.
// DebugProgressReset deletes bobbers.json as part of its wipe, so Ctrl+R drops back to
// that starting set (just the basic bobber).
public class BobberInventory : GenericSingleton<BobberInventory>
{
    [Tooltip("Catalog of EVERY bobber the game knows about. Add every BobberItem asset here so " +
             "save/load can resolve owned tackle back to assets by id. Must include everything " +
             "sold in the shop, not just the starting gear.")]
    [SerializeField] private List<BobberItem> registeredBobbers = new List<BobberItem>();

    [Tooltip("Starting tackle on a fresh save (no bobbers.json yet). Once a save exists this is " +
             "ignored and the saved owned set is loaded instead.")]
    [SerializeField] private List<BobberItem> ownedBobbers = new List<BobberItem>();

    [Tooltip("The bobber currently dangling from the rod. Defaults to the first entry on Start if null.")]
    [SerializeField] private BobberItem selectedBobber;

    [Tooltip("Rod line to push the selected bobber to. Auto-resolved at runtime if left empty.")]
    [SerializeField] private FishingLine fishingLine;

    // Bait equipped right before switching to a lure, so switching back to a regular bobber
    // restores it instead of leaving the hook reading "none equipped" (EnsureBaitSelected only
    // validates an existing selection — it never re-selects anything from null).
    private BaitItem baitBeforeLure;

    private string SavePath => Path.Combine(Application.persistentDataPath, "bobbers.json");

    public BobberItem SelectedBobber => selectedBobber;
    public IReadOnlyList<BobberItem> OwnedBobbers => ownedBobbers;

    // Convenience flag every fishing/bait surface can query without null-walking the chain.
    public static bool IsLureEquipped =>
        Instance != null && Instance.selectedBobber != null
        && Instance.selectedBobber.kind == BobberKind.Lure;

    // True only when the equipped tackle is a Popper-style lure. Drives the popper's surface
    // chatter (LureReelHandler/BobberController) and the popper-preference attraction bonus
    // (LureBiteBrain). A basic lure or bobber leaves this false.
    public static bool IsPopperEquipped =>
        IsLureEquipped && Instance.selectedBobber.lureStyle == LureStyle.Popper;

    // True if the fish species responds to whatever is currently on the rod (lure vs regular
    // bobber). Mirrors BaitInventory.PresetAcceptsSelectedBait for the tackle dimension.
    //
    // The Popper splits the lure fish into two disjoint sets (TP: the popper's aggressive pop
    // lures the "nervous" fish up but scares the sensitive ones off): with the Popper out only
    // popper-loving fish (FishPreset.prefersPopper) respond, and with the plain lure out only
    // the non-lovers do. So a popper fish is popper-EXCLUSIVE — it ignores the basic lure — and
    // a plain-lure fish never engages the popper (the zone's scare sweep chases it off instead).
    public static bool PresetRespondsToEquippedTackle(FishPreset preset)
    {
        if (preset == null) return false;
        if (!IsLureEquipped) return preset.RespondsToBobber;
        if (!preset.RespondsToLure) return false;
        return IsPopperEquipped ? preset.prefersPopper : !preset.prefersPopper;
    }

    public event Action<BobberItem> OnSelectedBobberChanged;
    public event Action OnOwnedBobbersChanged;

    private void Start()
    {
        if (fishingLine == null) fishingLine = FindFirstObjectByType<FishingLine>();

        // Saved ownership wins; a fresh save keeps the serialized starting set and writes it out.
        if (!LoadOwned()) SaveOwned();

        if ((selectedBobber == null || !ownedBobbers.Contains(selectedBobber)) && ownedBobbers.Count > 0)
        {
            selectedBobber = ownedBobbers[0];
        }

        ApplyToRod(selectedBobber);
    }

    public void SetSelectedBobber(BobberItem bobber)
    {
        if (selectedBobber == bobber) return;
        if (bobber != null && !ownedBobbers.Contains(bobber)) return;

        selectedBobber = bobber;
        ApplyToRod(selectedBobber);
        SaveOwned();   // remember the equipped tackle across scene loads / sessions
        OnSelectedBobberChanged?.Invoke(selectedBobber);
    }

    public void AddBobber(BobberItem bobber)
    {
        if (bobber == null || ownedBobbers.Contains(bobber)) return;
        ownedBobbers.Add(bobber);
        SaveOwned();
        OnOwnedBobbersChanged?.Invoke();
    }

    private void ApplyToRod(BobberItem bobber)
    {
        if (fishingLine == null) fishingLine = FindFirstObjectByType<FishingLine>();
        if (fishingLine != null && bobber != null) fishingLine.SetBobber(bobber);

        if (bobber != null && BaitInventory.Instance != null)
        {
            if (bobber.kind == BobberKind.Lure)
            {
                // Lures don't use bait; remember whatever was equipped (unless we're swapping
                // between two lures, where the current selection is already null from the first
                // swap) so switching back to a regular bobber can restore it.
                if (BaitInventory.Instance.SelectedBait != null)
                    baitBeforeLure = BaitInventory.Instance.SelectedBait;
                BaitInventory.Instance.SetSelectedBait(null);
            }
            else
            {
                // Regular bobber: restore the bait that was equipped before switching to a lure.
                // EnsureBaitSelected only validates an existing selection (drops it if it ran out
                // meanwhile) — it never re-selects from null, so without this the hook always came
                // back reading "none equipped" after a lure round-trip.
                if (baitBeforeLure != null)
                {
                    BaitInventory.Instance.SetSelectedBait(baitBeforeLure);
                    baitBeforeLure = null;
                }
                BaitInventory.Instance.EnsureBaitSelected();
            }
        }
    }

    // --- Persistence: owned tackle + equipped selection, keyed by stable BobberItem.id ---

    private BobberItem ResolveById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (BobberItem b in registeredBobbers)
            if (b != null && b.id == id) return b;
        return null;
    }

    private void SaveOwned()
    {
        BobberSaveData data = new BobberSaveData();
        foreach (BobberItem b in ownedBobbers)
        {
            if (b != null && !string.IsNullOrEmpty(b.id)) data.ownedIds.Add(b.id);
        }
        data.selectedId = selectedBobber != null ? selectedBobber.id : null;
        File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
    }

    // Rebuilds ownedBobbers (and selection) from the save file. Returns false when no save
    // exists yet, so the caller can fall back to the serialized starting grant.
    private bool LoadOwned()
    {
        if (!File.Exists(SavePath)) return false;

        try
        {
            BobberSaveData data = JsonUtility.FromJson<BobberSaveData>(File.ReadAllText(SavePath));
            if (data == null) return true;

            List<BobberItem> loaded = new List<BobberItem>();
            if (data.ownedIds != null)
            {
                foreach (string id in data.ownedIds)
                {
                    BobberItem b = ResolveById(id);
                    // Skip unknown/dropped ids and de-dupe; registeredBobbers is the source of truth.
                    if (b != null && !loaded.Contains(b)) loaded.Add(b);
                }
            }

            // Only adopt the saved set if it resolved to something; a corrupt/empty save must not
            // strip the player down to zero tackle (a bobber is always meant to be equipped). In
            // that case keep the serialized starting set untouched.
            if (loaded.Count == 0) return true;

            ownedBobbers.Clear();
            ownedBobbers.AddRange(loaded);
            selectedBobber = ResolveById(data.selectedId);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[BobberInventory] Failed to load: {e.Message}");
        }
        return true;
    }

    [Serializable]
    private class BobberSaveData
    {
        public List<string> ownedIds = new List<string>();
        public string selectedId;
    }

    [Button("Re-apply selected bobber to rod")]
    private void DebugReapply() => ApplyToRod(selectedBobber);

    // --- Debug: control owned tackle in the editor (e.g. to test shop buying / SOLD OUT) ---
    // The Owned Bobbers list above is the primary lever — drag BobberItem assets in to own them,
    // remove them to make the shop sell them again. These buttons are conveniences on top of that.

    [Button("Debug: Clear Owned Tackle")]
    private void DebugClearOwned()
    {
        ownedBobbers.Clear();
        selectedBobber = null;
        if (Application.isPlaying) SaveOwned();
        OnOwnedBobbersChanged?.Invoke();
        OnSelectedBobberChanged?.Invoke(selectedBobber);
    }

    // Call after hand-editing the Owned Bobbers list at runtime so the gear selector / shop refresh.
    [Button("Debug: Refresh Owned (after editing list)")]
    private void DebugRefreshOwned() => OnOwnedBobbersChanged?.Invoke();
}
