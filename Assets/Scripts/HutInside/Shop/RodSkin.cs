using UnityEngine;

// A cosmetic rod look sold in the shop. Purely visual — swaps the rod's mesh and/or materials.
// Leave a field null/empty to keep the rod's current mesh or materials for that slot.
[CreateAssetMenu(fileName = "RodSkin", menuName = "FishOWisp/Shop/Rod Skin")]
public class RodSkin : ScriptableObject
{
    [Tooltip("Stable identifier used for ownership tracking. Do not change after release.")]
    public string id;

    public string displayName;
    [TextArea] public string description;
    public Sprite icon;

    [Header("Visual override (optional)")]
    [Tooltip("Mesh to swap onto the rod. Leave null to keep the rod's current mesh.")]
    public Mesh mesh;
    [Tooltip("Materials to swap onto the rod. Leave empty to keep the rod's current materials.")]
    public Material[] materials;
}
