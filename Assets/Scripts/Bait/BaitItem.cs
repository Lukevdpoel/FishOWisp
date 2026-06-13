using UnityEngine;

[CreateAssetMenu(fileName = "BaitItem", menuName = "FishOWisp/Bait Item", order = 0)]
public class BaitItem : ScriptableObject
{
    [Tooltip("Stable identifier used for save data. Do not change after release.")]
    public string id;

    public string displayName;
    public string displayNamePlural;

    [Tooltip("Drag a PNG (imported as Sprite) here to represent the item on screen.")]
    public Sprite icon;

    [TextArea] public string description;

    [Tooltip("If true, this bait is treated as infinite: it can never be bought, dropped, or consumed, and is always equipped at full stock.")]
    public bool isAlwaysAvailable;

    [Tooltip("Master switch: untick to shelve this bait for now — vendors stop selling it and it disappears from the bait bar.")]
    public bool isAvailable = true;
}
