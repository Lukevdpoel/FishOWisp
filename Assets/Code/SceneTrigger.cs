using UnityEngine;
using UnityEngine.SceneManagement; // Needed for SceneManager

public class SceneTrigger : MonoBehaviour
{
    public string sceneName; // The name of the scene to load

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")) // Make sure only the player triggers it
        {
            SceneManager.LoadScene(sceneName);
        }
    }
}
