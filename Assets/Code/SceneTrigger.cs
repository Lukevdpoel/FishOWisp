using UnityEngine;

public class SceneTrigger : MonoBehaviour
{
    public string sceneName;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            SceneTransitionManager.Instance.LoadScene(sceneName);
        }
    }
}
