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

    [Header("Bait Hint")]
    [Tooltip("UI Image whose sprite is overwritten with the fish's preferred-bait icon. Leave empty if the indicator is a world-space SpriteRenderer.")]
    public Image baitIconTarget;
    [Tooltip("World-space SpriteRenderer whose sprite is overwritten with the fish's preferred-bait icon. If left empty, the script auto-finds a SpriteRenderer in this prefab's children at runtime.")]
    public SpriteRenderer baitSpriteTarget;
    [Tooltip("Hide the bait icon target when the fish has no preferred bait listed. If false, the existing sprite stays as-is.")]
    public bool hideBaitIconWhenNoPreference = true;

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

        BaitItem bait = null;
        if (preset != null && preset.preferredBaits != null)
        {
            for (int i = 0; i < preset.preferredBaits.Count; i++)
            {
                if (preset.preferredBaits[i] != null) { bait = preset.preferredBaits[i]; break; }
            }
        }
        Sprite baitSprite = bait != null ? bait.icon : null;

        if (baitIconTarget != null)
        {
            if (baitSprite != null)
            {
                baitIconTarget.sprite = baitSprite;
                baitIconTarget.enabled = true;
            }
            else if (hideBaitIconWhenNoPreference)
            {
                baitIconTarget.enabled = false;
            }
        }

        SpriteRenderer sr = baitSpriteTarget != null ? baitSpriteTarget : GetComponentInChildren<SpriteRenderer>(true);
        if (sr != null)
        {
            if (baitSprite != null)
            {
                sr.sprite = baitSprite;
                sr.enabled = true;
            }
            else if (hideBaitIconWhenNoPreference)
            {
                sr.enabled = false;
            }
        }
    }
}
