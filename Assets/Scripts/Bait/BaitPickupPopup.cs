using UnityEngine;

public class BaitPickupPopup : MonoBehaviour
{
    private SpriteRenderer sr;
    private float elapsed;
    private float duration;
    private float riseDistance;
    private Vector3 startPos;
    private Color baseColor;
    private Transform cam;
    private AnimationCurve riseCurve;
    private float holdFraction;

    private float startDelay;

    public static void Spawn(Sprite icon, Vector3 worldPos, float scale = 0.5f, float duration = 1.4f, float riseDistance = 1.2f, float startDelay = 0f)
    {
        if (icon == null) return;

        GameObject go = new GameObject($"BaitPickupPopup_{icon.name}");
        go.transform.position = worldPos;
        go.transform.localScale = Vector3.one * scale;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = icon;
        sr.sortingOrder = 1000;
        if (startDelay > 0f)
        {
            Color c = sr.color; c.a = 0f; sr.color = c;
        }

        BaitPickupPopup popup = go.AddComponent<BaitPickupPopup>();
        popup.sr = sr;
        popup.duration = duration;
        popup.riseDistance = riseDistance;
        popup.startPos = worldPos;
        popup.baseColor = Color.white;
        popup.cam = Camera.main != null ? Camera.main.transform : null;
        popup.holdFraction = 0.55f;
        popup.startDelay = Mathf.Max(0f, startDelay);
        popup.riseCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 4f),
            new Keyframe(0.35f, 1.05f),
            new Keyframe(0.55f, 1.0f),
            new Keyframe(1f, 1.0f)
        );
    }

    private void Update()
    {
        if (startDelay > 0f)
        {
            startDelay -= Time.deltaTime;
            if (startDelay > 0f) return;
        }

        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);

        float rise = riseCurve != null ? riseCurve.Evaluate(t) : t;
        transform.position = startPos + Vector3.up * (rise * riseDistance);

        float alpha = t < holdFraction ? 1f : 1f - (t - holdFraction) / (1f - holdFraction);
        Color c = baseColor;
        c.a = baseColor.a * Mathf.Clamp01(alpha);
        sr.color = c;

        if (cam != null)
        {
            transform.rotation = Quaternion.LookRotation(transform.position - cam.position);
        }

        if (t >= 1f) Destroy(gameObject);
    }
}
