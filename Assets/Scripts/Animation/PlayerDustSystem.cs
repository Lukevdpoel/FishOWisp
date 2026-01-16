using UnityEngine;
using System.Collections.Generic;

public class PlayerDustSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterController characterController;
    [SerializeField] private ParticleSystem dustPrefab;
    [SerializeField] private Transform feetPosition;

    [Header("Spawning Settings")]
    [Tooltip("Distance in meters traveled before spawning next dust puff.")]
    [SerializeField] private float stepDistance = 1.5f;
    [SerializeField] private float minSpeedToSpawn = 0.5f;
    [SerializeField] private Vector3 positionOffset = new Vector3(0, 0.1f, -0.2f);

    private List<ParticleSystem> pool = new List<ParticleSystem>();
    private Vector3 lastSpawnPosition;

    void Start()
    {
        lastSpawnPosition = transform.position;
    }

    void Update()
    {
        if (characterController == null) return;

        // Calculate horizontal speed (ignoring vertical falling speed)
        Vector3 horizontalVelocity = new Vector3(characterController.velocity.x, 0, characterController.velocity.z);
        float currentSpeed = horizontalVelocity.magnitude;

        if (characterController.isGrounded && currentSpeed > minSpeedToSpawn)
        {
            float distanceCovered = Vector3.Distance(transform.position, lastSpawnPosition);

            if (distanceCovered >= stepDistance)
            {
                SpawnDust();
                lastSpawnPosition = transform.position;
            }
        }
        else
        {
            // Reset position tracker so we don't spawn instantly after stopping
            lastSpawnPosition = transform.position;
        }
    }

    private void SpawnDust()
    {
        ParticleSystem dust = GetDustFromPool();

        // 1. Force settings to prevent dragging
        var main = dust.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // FORCE WORLD SPACE
        main.loop = false; // FORCE NO LOOPING

        // 2. Position
        // We still rotate the *offset* so the dust spawns behind the heels relative to facing
        Vector3 relativeOffset = transform.rotation * positionOffset;
        Vector3 spawnPos = (feetPosition != null ? feetPosition.position : transform.position) + relativeOffset;

        dust.transform.position = spawnPos;

        // 3. ROTATION FIX:
        // Force rotation to Identity (0,0,0). 
        // This ensures the particle system is always upright in the world, 
        // regardless of how the player is turning.
        dust.transform.rotation = Quaternion.identity;

        // 4. Play
        dust.gameObject.SetActive(true);
        dust.Play();
    }

    private ParticleSystem GetDustFromPool()
    {
        foreach (var item in pool)
        {
            // Only grab items that are NOT currently playing
            if (!item.gameObject.activeInHierarchy && !item.isPlaying) return item;
        }

        ParticleSystem newDust = Instantiate(dustPrefab);

        // Ensure it handles its own cleanup
        var main = newDust.main;
        main.stopAction = ParticleSystemStopAction.Disable;

        pool.Add(newDust);
        return newDust;
    }
}