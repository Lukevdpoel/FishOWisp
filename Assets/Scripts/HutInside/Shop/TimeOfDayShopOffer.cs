using UnityEngine;

// A time-of-day skip on the shop wall (e.g. a Night Lantern or a Sun Stone). Buying jumps the
// world clock to the start of night or day; time keeps flowing from there (not a permanent lock).
// Same call the old 2D shop used (WorldStateManager.SkipToTimeOfDay). Re-buyable.
[CreateAssetMenu(fileName = "TimeOfDayShopOffer", menuName = "FishOWisp/Shop/Time Of Day Offer")]
public class TimeOfDayShopOffer : ShopOffer
{
    [Tooltip("Checked = skip to the start of night. Unchecked = skip to the start of day.")]
    [SerializeField] private bool skipToNight = true;

    protected override bool CanGrant() => WorldStateManager.Instance != null;

    protected override void Grant() => WorldStateManager.Instance.SkipToTimeOfDay(skipToNight);
}
