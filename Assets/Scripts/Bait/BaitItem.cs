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
}
