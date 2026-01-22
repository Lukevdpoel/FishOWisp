using UnityEngine;

public class SpawnPoint : MonoBehaviour
{
    [Tooltip("The unique ID for this spawn point (e.g., 'DungeonEntrance', 'TownGate')")]
    public string spawnID;

    private void OnDrawGizmos()
    {
        // Draws a blue sphere in the editor so you can see where the point is
        Gizmos.color = Color.blue;
        Gizmos.DrawSphere(transform.position, 0.5f);
        Gizmos.DrawRay(transform.position, transform.forward * 2);
    }
}