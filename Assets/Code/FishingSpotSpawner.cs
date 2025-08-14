using UnityEngine;

public class FishingSpotSpawner : MonoBehaviour
{
    public GameObject fishingSpotPrefab;
    public float spawnInterval = 5f;
    public float spotLifetime = 8f;

    private FishPoolArea[] areas;
    private float nextSpawn;

    private void Awake()
    {
        areas = FindObjectsOfType<FishPoolArea>();
    }

    private void Update()
    {
        if (Time.time >= nextSpawn)
        {
            SpawnFishingSpot();
            nextSpawn = Time.time + spawnInterval;
        }
    }

    void SpawnFishingSpot()
    {
        if (areas == null || areas.Length == 0 || fishingSpotPrefab == null) return;

        var area = areas[Random.Range(0, areas.Length)];
        var col = area.GetComponent<Collider>();
        if (col == null) return;

        var bounds = col.bounds;
        Vector3 pos = new Vector3(
            Random.Range(bounds.min.x, bounds.max.x),
            bounds.max.y, // surface
            Random.Range(bounds.min.z, bounds.max.z)
        );

        var spotGO = Instantiate(fishingSpotPrefab, pos, Quaternion.identity);
        var spot = spotGO.GetComponent<FishingSpot>();
        if (spot != null) spot.Init(area.fishPool); // <-- inject instance here

        if (spotLifetime > 0) Destroy(spotGO, spotLifetime);
    }
}
