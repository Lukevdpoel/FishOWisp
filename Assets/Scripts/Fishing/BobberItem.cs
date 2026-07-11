using UnityEngine;

public enum BobberKind { Bobber, Lure }

// Sub-type for lures. Standard = the plain TP-style reel mimic. Popper = a noisy surface lure
// whose front chatters up and down on the water (see BobberController popper bounce / splash),
// and which popper-preferring fish (FishPreset.prefersPopper) notice and strike more readily.
// Ignored for BobberKind.Bobber.
public enum LureStyle { Standard, Popper }

[CreateAssetMenu(fileName = "BobberItem", menuName = "FishOWisp/Bobber Item", order = 1)]
public class BobberItem : ScriptableObject
{
    [Tooltip("Stable identifier used for save data. Do not change after release.")]
    public string id;

    public string displayName;

    [Tooltip("Drag a PNG (imported as Sprite) here to represent the item on screen.")]
    public Sprite icon;

    [TextArea] public string description;

    [Header("Behavior")]
    [Tooltip("Bobber = passive cast / wait / nibble / bite (requires bait). " +
             "Lure = active Twilight-Princess-style reel mimic, no bait, scare-bar driven attraction.")]
    public BobberKind kind = BobberKind.Bobber;

    [Tooltip("Only used when Kind = Lure. Standard = plain reel lure. Popper = surface lure that " +
             "chatters/splashes on the water and that popper-preferring fish (FishPreset.prefersPopper) " +
             "are extra drawn to. The visual chatter is also tuned per-prefab via the lure reel settings.")]
    public LureStyle lureStyle = LureStyle.Standard;

    [Header("Prefab")]
    [Tooltip("The bobber prefab used both for dangling from the rod and for casting. Must have a BobberController on the root.")]
    public GameObject bobberPrefab;

    [Header("Dangle")]
    [Tooltip("Extra spin (degrees, about the vertical line) applied to the dangling rest pose so an " +
             "elongated lure shows the side you want toward the player. The pose already points the " +
             "LineAttachPoint up and locks the heading to the rod; this just turns which face shows. " +
             "Leave 0 for a round bobber (its spin is invisible). Tune it live in Play mode.")]
    [Range(-180f, 180f)] public float dangleFacingYawOffset = 0f;
}
