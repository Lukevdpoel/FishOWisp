using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[DisallowMultipleComponent]
public class MilestoneParticleSpawner : MonoBehaviour
{
    [System.Serializable]
    public class TierOverride
    {
        public int count;
        public GameObject particlePrefab;
    }

    [System.Serializable]
    public class CategoryOverride
    {
        public MilestoneCategory category;
        public GameObject particlePrefab;
        public List<TierOverride> tierOverrides = new List<TierOverride>();
    }

    [Title("Spawn Settings")]
    [Tooltip("If unset, particles spawn on this transform.")]
    public Transform spawnPoint;

    [Tooltip("Local offset applied to the spawn point.")]
    public Vector3 spawnOffset = Vector3.up;

    [Tooltip("Parent the spawned effect to the spawn point so it follows the player.")]
    public bool parentToSpawnPoint = true;

    [Tooltip("Auto-destroy spawned effects after this many seconds. 0 disables.")]
    public float autoDestroyAfter = 5f;

    [Title("Effects")]
    [Tooltip("Used when no category/tier override matches.")]
    public GameObject defaultParticlePrefab;

    [Tooltip("Per-category prefab overrides, with optional per-tier prefabs.")]
    public List<CategoryOverride> categoryOverrides = new List<CategoryOverride>();

    private void OnEnable()
    {
        FishMilestoneTracker.OnMilestoneReached += HandleMilestone;
    }

    private void OnDisable()
    {
        FishMilestoneTracker.OnMilestoneReached -= HandleMilestone;
    }

    private void HandleMilestone(MilestoneEvent evt)
    {
        GameObject prefab = ResolvePrefab(evt);
        if (prefab == null) return;

        Transform anchor = spawnPoint != null ? spawnPoint : transform;
        Vector3 worldPos = anchor.TransformPoint(spawnOffset);
        Quaternion rot = anchor.rotation;

        GameObject instance = parentToSpawnPoint
            ? Instantiate(prefab, worldPos, rot, anchor)
            : Instantiate(prefab, worldPos, rot);

        if (autoDestroyAfter > 0f)
        {
            Destroy(instance, autoDestroyAfter);
        }
    }

    private GameObject ResolvePrefab(MilestoneEvent evt)
    {
        foreach (var cat in categoryOverrides)
        {
            if (cat.category != evt.category) continue;

            foreach (var tier in cat.tierOverrides)
            {
                if (tier.count == evt.count && tier.particlePrefab != null)
                    return tier.particlePrefab;
            }

            if (cat.particlePrefab != null) return cat.particlePrefab;
        }

        return defaultParticlePrefab;
    }
}
