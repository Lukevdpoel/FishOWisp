using UnityEngine;

// Base for everything purchasable in the 3D shop. A ShopItem in the scene points at one of these
// assets; the shop UI reads DisplayName/Description/Price and the highlight buy path calls
// TryPurchase(). Concrete offers (bait, tackle, rod, fish feed, time-of-day) implement Grant()
// against the existing inventories/managers so there's a single, shared currency-spend chokepoint.
public abstract class ShopOffer : ScriptableObject
{
    [SerializeField] protected string displayName;
    [TextArea][SerializeField] protected string description;
    [Min(0)][SerializeField] protected int price = 25;

    public virtual string DisplayName => displayName;
    public virtual string Description => description;
    public int Price => price;

    /// <summary>One-time items (tackle, rods) report true once owned so the wall shows "SOLD OUT".</summary>
    public virtual bool IsSoldOut() => false;

    /// <summary>
    /// Spend the price, then grant. No-op (and spends nothing) if sold out, the grant can't run
    /// (missing refs), or the player can't afford it. Returns true only when the grant happened.
    /// </summary>
    public bool TryPurchase()
    {
        if (IsSoldOut()) return false;
        if (PlayerInventory.Instance == null) return false;
        if (!CanGrant()) return false;
        if (!PlayerInventory.Instance.TrySpendCurrency(price)) return false;
        Grant();
        return true;
    }

    /// <summary>Are the references this offer needs present? Checked before any currency is spent.</summary>
    protected virtual bool CanGrant() => true;

    /// <summary>Hand over the goods. Only called after the price has been successfully spent.</summary>
    protected abstract void Grant();
}
