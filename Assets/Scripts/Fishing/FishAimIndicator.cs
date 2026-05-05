using UnityEngine;
using UnityEngine.UI;

public class FishAimIndicator : MonoBehaviour
{
    [Header("Colors")]
    [Tooltip("Default tint applied to non-legendary fish.")]
    public Color normalColor = Color.white;
    [Tooltip("Tint applied when the fish is legendary.")]
    public Color legendaryColor = new Color(1f, 0.85f, 0.3f, 1f);

    [Header("Targets")]
    [Tooltip("Specific Graphics (Images / Text) to tint. Leave empty to tint every Graphic in this prefab.")]
    public Graphic[] tintTargets;

    [Header("Optional")]
    [Tooltip("Extra GameObjects to enable only when the fish is legendary (e.g. a second rotated star).")]
    public GameObject[] legendaryOnlyExtras;

    public void ApplyPreset(FishPreset preset)
    {
        bool legendary = preset != null && preset.isLegendary;
        Color tint = legendary ? legendaryColor : normalColor;

        Graphic[] targets = (tintTargets != null && tintTargets.Length > 0)
            ? tintTargets
            : GetComponentsInChildren<Graphic>(true);

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;
            // Preserve each graphic's authored alpha so designers can keep layered transparency.
            Color c = targets[i].color;
            targets[i].color = new Color(tint.r, tint.g, tint.b, c.a);
        }

        if (legendaryOnlyExtras != null)
        {
            for (int i = 0; i < legendaryOnlyExtras.Length; i++)
            {
                if (legendaryOnlyExtras[i] != null)
                    legendaryOnlyExtras[i].SetActive(legendary);
            }
        }
    }
}
