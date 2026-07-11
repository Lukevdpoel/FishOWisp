using UnityEngine;

// Buy bait by the unit (a "bucket of bait" on the shop wall). One purchase grants
// amountPerPurchase units of the bait into BaitInventory. Never sold out — buy as much as you can
// afford. Falls back to the BaitItem's own name/description when the offer's are left blank.
[CreateAssetMenu(fileName = "BaitShopOffer", menuName = "FishOWisp/Shop/Bait Offer")]
public class BaitShopOffer : ShopOffer
{
    [SerializeField] private BaitItem bait;
    [Tooltip("Units granted per purchase.")]
    [Min(1)][SerializeField] private int amountPerPurchase = 1;

    public override string DisplayName =>
        !string.IsNullOrEmpty(displayName) ? displayName
        : (bait != null ? (string.IsNullOrEmpty(bait.displayName) ? bait.name : bait.displayName) : "Bait");

    public override string Description =>
        !string.IsNullOrEmpty(description) ? description : (bait != null ? bait.description : string.Empty);

    protected override bool CanGrant() =>
        bait != null && !bait.isAlwaysAvailable && bait.isAvailable && BaitInventory.Instance != null;

    protected override void Grant() => BaitInventory.Instance.AddBait(bait, amountPerPurchase);
}
