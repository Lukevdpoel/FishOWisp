using System.Collections.Generic;
using UnityEngine;

public enum ShopCategory { Fishing, Aquarium }

// Marks a 3D model on the shop wall as buyable. Put one on each item GameObject, give it a
// Collider (for mouse hover hit-testing) and a ShopOffer asset. ShopController handles navigation
// and purchasing; this component just owns the per-item highlight visuals (an outline material
// appended to the target renderers + a small lift) and exposes the offer.
[RequireComponent(typeof(Collider))]
public class ShopItem : MonoBehaviour
{
    [SerializeField] private ShopOffer offer;
    [Tooltip("Which shop counter this belongs to. Only 'Fishing' is browsable right now.")]
    [SerializeField] private ShopCategory category = ShopCategory.Fishing;

    [Header("Highlight — Outline")]
    [Tooltip("Renderers that get the outline material appended while highlighted. Usually the item's MeshRenderer(s).")]
    [SerializeField] private Renderer[] outlineTargets;
    [Tooltip("Outline material to append while highlighted (e.g. Shaders/Outline/OutlineWhite.mat).")]
    [SerializeField] private Material outlineMaterial;

    [Header("Highlight — Lift")]
    [Tooltip("Transform lifted while highlighted. Defaults to this object's transform.")]
    [SerializeField] private Transform liftRoot;
    [SerializeField] private float liftHeight = 0.15f;
    [SerializeField] private float liftSpeed = 8f;

    public ShopOffer Offer => offer;
    public ShopCategory Category => category;
    public bool IsHighlighted => highlighted;

    // When false, the highlight no longer lifts this item (e.g. tackle swings instead — see
    // ShopTackleHanger). Outline still applies.
    public bool LiftEnabled { get; set; } = true;

    private bool highlighted;
    private Vector3 liftBaseLocalPos;
    private List<Collider> hoverColliders;
    // Original materials per outline target, captured when the outline is added so it can be
    // cleanly removed (renderers can have different material counts).
    private readonly List<Material[]> savedMaterials = new List<Material[]>();

    private void Awake()
    {
        if (liftRoot == null) liftRoot = transform;
        liftBaseLocalPos = liftRoot.localPosition;
    }

    public void SetHighlighted(bool on)
    {
        if (on == highlighted) return;
        highlighted = on;
        ApplyOutline(on);
    }

    // Test a ray against just THIS item's own collider(s) — used by ShopController's hover check so it
    // never has to query the whole physics scene (no dedicated layer needed). Returns the nearest hit
    // distance. Disabled/hidden colliders (e.g. the hung bobber's own colliders) are skipped.
    public bool RaycastHover(Ray ray, float maxDistance, out float distance)
    {
        EnsureHoverColliders();
        distance = float.MaxValue;
        bool hitAny = false;
        for (int i = 0; i < hoverColliders.Count; i++)
        {
            Collider c = hoverColliders[i];
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
            if (c.Raycast(ray, out RaycastHit hit, maxDistance) && hit.distance < distance)
            {
                distance = hit.distance;
                hitAny = true;
            }
        }
        return hitAny;
    }

    private void EnsureHoverColliders()
    {
        if (hoverColliders != null && hoverColliders.Count > 0) return;
        hoverColliders ??= new List<Collider>();
        GetComponentsInChildren(true, hoverColliders); // self + children; disabled ones filtered at test time
    }

    private void OnDisable()
    {
        // Don't leave an appended outline material (or a raised pose) behind if the item is hidden.
        if (highlighted)
        {
            highlighted = false;
            ApplyOutline(false);
        }
        if (liftRoot != null) liftRoot.localPosition = liftBaseLocalPos;
    }

    private void Update()
    {
        if (liftRoot == null || !LiftEnabled) return;
        Vector3 target = liftBaseLocalPos + (highlighted ? Vector3.up * liftHeight : Vector3.zero);
        float k = 1f - Mathf.Exp(-liftSpeed * Time.unscaledDeltaTime);
        liftRoot.localPosition = Vector3.Lerp(liftRoot.localPosition, target, k);
    }

    // Renderers to outline: the explicitly-assigned ones, or — if none are set — every child
    // renderer (so a runtime-spawned model, e.g. ShopTackleHanger's hung bobber, outlines with no
    // manual wiring). An array holding only empty (None) slots counts as "not set", so a leftover
    // size-1 null entry in the inspector doesn't silently disable the fallback.
    // Resolved lazily and cached once renderers actually exist.
    private Renderer[] resolvedOutlineTargets;

    private Renderer[] ResolveOutlineTargets()
    {
        if (resolvedOutlineTargets != null && resolvedOutlineTargets.Length > 0) return resolvedOutlineTargets;
        bool anyAssigned = false;
        if (outlineTargets != null)
            foreach (Renderer r in outlineTargets)
                if (r != null) { anyAssigned = true; break; }
        Renderer[] t = anyAssigned ? outlineTargets : GetComponentsInChildren<Renderer>(true);
        if (t != null && t.Length > 0) resolvedOutlineTargets = t;
        return t;
    }

    private const bool OutlineEnabled = true;

    private void ApplyOutline(bool on)
    {
        if (!OutlineEnabled) return;
        if (outlineMaterial == null) return;
        Renderer[] targets = ResolveOutlineTargets();
        if (targets == null || targets.Length == 0) return;

        if (on)
        {
            savedMaterials.Clear();
            foreach (Renderer r in targets)
            {
                if (r == null) { savedMaterials.Add(null); continue; }
                Material[] current = r.sharedMaterials;
                savedMaterials.Add(current);

                Material[] withOutline = new Material[current.Length + 1];
                for (int i = 0; i < current.Length; i++) withOutline[i] = current[i];
                withOutline[current.Length] = outlineMaterial;
                r.sharedMaterials = withOutline;
            }
        }
        else
        {
            for (int i = 0; i < targets.Length; i++)
            {
                Renderer r = targets[i];
                if (r == null) continue;
                if (i < savedMaterials.Count && savedMaterials[i] != null)
                    r.sharedMaterials = savedMaterials[i];
            }
            savedMaterials.Clear();
        }
    }
}
