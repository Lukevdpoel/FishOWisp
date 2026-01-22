using UnityEngine;

public class SceneTrigger : MonoBehaviour
{
    public string sceneName;

    [Tooltip("The ID of the SpawnPoint in the NEXT scene where the player should appear.")]
    public string targetSpawnID;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            // Pass the targetID to the manager
            SceneTransitionManager.Instance.LoadScene(sceneName, targetSpawnID);
        }
    }
}