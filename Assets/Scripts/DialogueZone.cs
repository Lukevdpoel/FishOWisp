using UnityEngine;

public class DialogueZone : MonoBehaviour
{
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            // Safe null check
            if (DialogueManager.Instance != null)
            {
                DialogueManager.Instance.ForceCloseAllUI();
            }
        }
    }
}