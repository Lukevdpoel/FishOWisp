using System.Collections.Generic;
using UnityEngine;

// A lure/bobber hanging on the shop board. Durable gear: bought once, then it reads SOLD OUT.
// Grants the BobberItem into BobberInventory (which de-dupes, so a re-buy is harmless anyway).
[CreateAssetMenu(fileName = "TackleShopOffer", menuName = "FishOWisp/Shop/Tackle Offer")]
public class TackleShopOffer : ShopOffer
{
    [SerializeField] private BobberItem tackle;

    public override string DisplayName =>
        !string.IsNullOrEmpty(displayName) ? displayName
        : (tackle != null ? (string.IsNullOrEmpty(tackle.displayName) ? tackle.name : tackle.displayName) : "Tackle");

    public override string Description =>
        !string.IsNullOrEmpty(description) ? description : (tackle != null ? tackle.description : string.Empty);

    public override bool IsSoldOut()
    {
        if (tackle == null || BobberInventory.Instance == null) return false;
        IReadOnlyList<BobberItem> owned = BobberInventory.Instance.OwnedBobbers;
        if (owned == null) return false;
        for (int i = 0; i < owned.Count; i++)
            if (owned[i] == tackle) return true;
        return false;
    }

    protected override bool CanGrant() => tackle != null && BobberInventory.Instance != null;

    protected override void Grant() => BobberInventory.Instance.AddBobber(tackle);
}
