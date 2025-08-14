using UnityEngine;

public class FishingSpot : MonoBehaviour
{
    [SerializeField] private FishPool fishPool; // instance ref (set by spawner or inspector)
    [SerializeField] private Collider trigger;  // make sure it's a trigger

    public void Init(FishPool pool) => fishPool = pool;

    private void Awake()
    {
        if (trigger == null) trigger = GetComponent<Collider>();
        if (trigger != null) trigger.isTrigger = true;
    }

    private void Start()
    {
        // Fallback: if not injected, try to find the area we’re inside
        if (fishPool == null)
        {
            var hits = Physics.OverlapSphere(transform.position, 0.25f);
            foreach (var h in hits)
            {
                var area = h.GetComponent<FishPoolArea>();
                if (area != null)
                {
                    fishPool = area.fishPool;
                    break;
                }
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Bobber")) return;

        if (fishPool == null)
        {
            Debug.LogWarning($"FishingSpot at {transform.position} has no FishPool assigned.");
            return;
        }

        string caughtFish = fishPool.GetRandomFish(); // <-- instance call
        Debug.Log("You caught a " + caughtFish);

        Destroy(gameObject); // consume the spot
    }
}
