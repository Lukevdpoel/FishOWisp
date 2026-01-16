using UnityEngine;
using System.Collections;

[RequireComponent(typeof(SpriteRenderer))]
public class FrameByFrameEffect : MonoBehaviour
{
    [Header("Animation Settings")]
    [Tooltip("Drag your separate PNGs here in order")]
    [SerializeField] private Sprite[] animationFrames;

    [Tooltip("Higher number = Faster Animation")]
    [SerializeField] private float frameRate = 20f;

    [SerializeField] private bool randomRotation = false;

    [Header("Scale Settings")]
    [Tooltip("The minimum random size multiplier.")]
    [SerializeField] private float minScale = 0.8f;
    [Tooltip("The maximum random size multiplier.")]
    [SerializeField] private float maxScale = 1.2f;

    private SpriteRenderer spriteRenderer;
    private WaitForSeconds frameDuration;
    private Vector3 originalScale; // Stores the prefab's base scale

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        // Remember the scale you set in the Transform component as the "base"
        originalScale = transform.localScale;
    }

    private void OnEnable()
    {
        // 1. Calculate Duration
        frameDuration = new WaitForSeconds(1f / frameRate);

        // 2. Randomize Scale
        float randomSize = Random.Range(minScale, maxScale);
        transform.localScale = originalScale * randomSize;

        // 3. Play Animation
        if (animationFrames != null && animationFrames.Length > 0)
        {
            StartCoroutine(PlayAnimation());
        }
    }

    private IEnumerator PlayAnimation()
    {
        // Optional Rotation Logic
        if (randomRotation)
        {
            // Note: If you are using the "Perpendicular" logic in the other script, 
            // be careful with Z-rotation here as it might tilt the dust into the ground.
            // Usually, for floor dust, you want to flip X or Y, not rotate Z.

            // Simple flip for variety without breaking perspective:
            bool flipX = Random.value > 0.5f;
            spriteRenderer.flipX = flipX;
        }

        for (int i = 0; i < animationFrames.Length; i++)
        {
            spriteRenderer.sprite = animationFrames[i];
            yield return frameDuration;
        }

        gameObject.SetActive(false);
    }
}