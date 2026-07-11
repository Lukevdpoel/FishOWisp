using System.Collections.Generic;
using UnityEngine;

// Applies cosmetic RodSkins to the player's rod. Wire the rod's MeshFilter/MeshRenderer in the
// inspector. The stock look (whatever's on the renderer at Awake) is captured so a skin with no
// mesh/material override can still cleanly restore it. Ownership is tracked by skin id so the
// shop can show "SOLD OUT" once a rod is bought.
//
// Scene singleton (GenericSingleton): RodShopOffer reaches it via RodCosmeticController.Instance.
public class RodCosmeticController : GenericSingleton<RodCosmeticController>
{
    [Header("Rod Visual")]
    [SerializeField] private MeshFilter rodMeshFilter;
    [SerializeField] private MeshRenderer rodRenderer;

    [Header("Starting Skin (optional)")]
    [Tooltip("Skin the player already owns at the start (the default rod). Marked owned so the shop " +
             "never charges for the look they begin with.")]
    [SerializeField] private RodSkin defaultSkin;

    private readonly HashSet<string> ownedIds = new HashSet<string>();
    private RodSkin equipped;

    private Mesh stockMesh;
    private Material[] stockMaterials;

    protected override void Awake()
    {
        base.Awake();
        if (rodMeshFilter != null) stockMesh = rodMeshFilter.sharedMesh;
        if (rodRenderer != null) stockMaterials = rodRenderer.sharedMaterials;
        if (defaultSkin != null) ownedIds.Add(Key(defaultSkin));
    }

    public bool Owns(RodSkin skin) => skin != null && ownedIds.Contains(Key(skin));
    public bool IsEquipped(RodSkin skin) => skin != null && equipped == skin;

    /// <summary>Grant ownership without changing the equipped look.</summary>
    public void Grant(RodSkin skin) { if (skin != null) ownedIds.Add(Key(skin)); }

    /// <summary>Mark owned and apply the look to the rod.</summary>
    public void Equip(RodSkin skin)
    {
        if (skin == null) return;
        ownedIds.Add(Key(skin));
        equipped = skin;

        Mesh m = skin.mesh != null ? skin.mesh : stockMesh;
        if (rodMeshFilter != null && m != null) rodMeshFilter.sharedMesh = m;

        Material[] mats = (skin.materials != null && skin.materials.Length > 0) ? skin.materials : stockMaterials;
        if (rodRenderer != null && mats != null && mats.Length > 0) rodRenderer.sharedMaterials = mats;
    }

    private static string Key(RodSkin s) => string.IsNullOrEmpty(s.id) ? s.name : s.id;
}
