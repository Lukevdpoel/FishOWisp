using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Holds the player's owned bobbers/lures and the currently equipped one.
// Bobbers are durable gear (no counts, no consumption) so this is intentionally
// lighter than BaitInventory. Swap to FishingLine happens in BobberInventory's
// own listener so any caller of SetSelectedBobber updates the rod automatically.
public class BobberInventory : GenericSingleton<BobberInventory>
{
    [Tooltip("All bobbers the player owns. Drag BobberItem assets here in the inspector.")]
    [SerializeField] private List<BobberItem> ownedBobbers = new List<BobberItem>();

    [Tooltip("The bobber currently dangling from the rod. Defaults to the first entry on Start if null.")]
    [SerializeField] private BobberItem selectedBobber;

    [Tooltip("Rod line to push the selected bobber to. Auto-resolved at runtime if left empty.")]
    [SerializeField] private FishingLine fishingLine;

    public BobberItem SelectedBobber => selectedBobber;
    public IReadOnlyList<BobberItem> OwnedBobbers => ownedBobbers;

    // Convenience flag every fishing/bait surface can query without null-walking the chain.
    public static bool IsLureEquipped =>
        Instance != null && Instance.selectedBobber != null
        && Instance.selectedBobber.kind == BobberKind.Lure;

    // True if the fish species responds to whatever is currently on the rod (lure vs regular
    // bobber). Mirrors BaitInventory.PresetAcceptsSelectedBait for the tackle dimension.
    public static bool PresetRespondsToEquippedTackle(FishPreset preset)
    {
        if (preset == null) return false;
        return IsLureEquipped ? preset.RespondsToLure : preset.RespondsToBobber;
    }

    public event Action<BobberItem> OnSelectedBobberChanged;
    public event Action OnOwnedBobbersChanged;

    private void Start()
    {
        if (fishingLine == null) fishingLine = FindFirstObjectByType<FishingLine>();

        if (selectedBobber == null && ownedBobbers.Count > 0)
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
        OnSelectedBobberChanged?.Invoke(selectedBobber);
    }

    public void AddBobber(BobberItem bobber)
    {
        if (bobber == null || ownedBobbers.Contains(bobber)) return;
        ownedBobbers.Add(bobber);
        OnOwnedBobbersChanged?.Invoke();
    }

    private void ApplyToRod(BobberItem bobber)
    {
        if (fishingLine == null) fishingLine = FindFirstObjectByType<FishingLine>();
        if (fishingLine != null && bobber != null) fishingLine.SetBobber(bobber);

        // Lures don't use bait; auto-clear any equipped bait so the bait bar visibly reflects
        // that it's idle until a regular bobber is re-equipped.
        if (bobber != null && bobber.kind == BobberKind.Lure && BaitInventory.Instance != null)
        {
            BaitInventory.Instance.SetSelectedBait(null);
        }
    }

    [Button("Re-apply selected bobber to rod")]
    private void DebugReapply() => ApplyToRod(selectedBobber);
}
