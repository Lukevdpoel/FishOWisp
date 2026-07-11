using UnityEngine;

// Fish feed on the shop wall. For now, BUYING it is what activates the effect — there's no
// inventory or throwing yet (that comes later). The effect runs for durationMinutes of real time
// via FishFeedController. Re-buyable (never sold out) so the player can refresh it.
[CreateAssetMenu(fileName = "FishFeedShopOffer", menuName = "FishOWisp/Shop/Fish Feed Offer")]
public class FishFeedShopOffer : ShopOffer
{
    [SerializeField] private FishFeedEffect effect = FishFeedEffect.RepelPredators;

    [Tooltip("Only used when Effect = SummonSpecies: the fish forced to spawn while active.")]
    [SerializeField] private FishPreset summonTarget;

    [Tooltip("How long the effect lasts, in real-time minutes.")]
    [Min(0f)][SerializeField] private float durationMinutes = 8f;

    protected override bool CanGrant() =>
        FishFeedController.Instance != null
        && (effect != FishFeedEffect.SummonSpecies || summonTarget != null);

    protected override void Grant() =>
        FishFeedController.Instance.Activate(effect, summonTarget, durationMinutes * 60f);
}
