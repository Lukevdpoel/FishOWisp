using UnityEngine;

// A cosmetic rod on the shop wall. Bought once (then SOLD OUT); purchasing equips the look via
// RodCosmeticController. Falls back to the RodSkin's name/description when the offer's are blank.
[CreateAssetMenu(fileName = "RodShopOffer", menuName = "FishOWisp/Shop/Rod Offer")]
public class RodShopOffer : ShopOffer
{
    [SerializeField] private RodSkin skin;

    public override string DisplayName =>
        !string.IsNullOrEmpty(displayName) ? displayName
        : (skin != null ? (string.IsNullOrEmpty(skin.displayName) ? skin.name : skin.displayName) : "Rod");

    public override string Description =>
        !string.IsNullOrEmpty(description) ? description : (skin != null ? skin.description : string.Empty);

    public override bool IsSoldOut() =>
        skin != null && RodCosmeticController.Instance != null && RodCosmeticController.Instance.Owns(skin);

    protected override bool CanGrant() => skin != null && RodCosmeticController.Instance != null;

    protected override void Grant() => RodCosmeticController.Instance.Equip(skin);
}
