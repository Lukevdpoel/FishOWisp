using UnityEngine;
using UnityEngine.UI;

public class FishingDot : MonoBehaviour
{
    [Header("Visuals")]
    public Image targetImage;
    public Color normalColor = Color.white;
    public Color hitColor = Color.green;
    public Color despawnColor = Color.red;

    [Header("Settings")]
    public float radius = 50f; // Hit radius

    // State
    public bool IsHit { get; private set; }
    public bool IsDespawning { get; private set; }

    private float despawnTimer;
    private float maxDespawnTime;
    private RectTransform rectTransform;

    public void Initialize(float lifeTime)
    {
        rectTransform = GetComponent<RectTransform>();

        // FIX: Ensure targetImage is assigned to prevent NullReferenceException
        if (targetImage == null)
        {
            targetImage = GetComponent<Image>();
        }

        if (targetImage != null)
        {
            targetImage.color = normalColor;
        }
        else
        {
            Debug.LogError($"FishingDot '{name}' has no Image component assigned!");
        }

        IsHit = false;
        IsDespawning = false;

        transform.localScale = Vector3.one;
    }

    public void OnHit()
    {
        if (IsHit) return;
        IsHit = true;

        if (targetImage != null)
            targetImage.color = hitColor;
    }

    public void StartDespawn(float time)
    {
        IsDespawning = true;
        maxDespawnTime = time;
        despawnTimer = time;
    }

    public bool UpdateDespawn()
    {
        if (!IsDespawning) return false;

        despawnTimer -= Time.deltaTime;

        float t = despawnTimer / maxDespawnTime;
        if (!IsHit && targetImage != null)
            targetImage.color = Color.Lerp(despawnColor, normalColor, t);

        return despawnTimer <= 0;
    }

    public bool IsMouseOver(Vector2 mousePos)
    {
        if (rectTransform == null) return false;
        return Vector2.Distance(mousePos, rectTransform.position) <= radius;
    }
}