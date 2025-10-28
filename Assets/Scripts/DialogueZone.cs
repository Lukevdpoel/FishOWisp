using UnityEngine;

public class DialogueZone : MonoBehaviour
{
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            Debug.Log("Player exited zone"); // ✅ Debug check
            DialogueManager.Instance?.OnPlayerExitZone();
        }
    }
}
