using UnityEngine;

public class DialogueNPC : MonoBehaviour
{
    public Dialogue dialogue;
    public GameObject promptUI; // The visual "E" or "!" icon above the NPC

    private bool playerInZone;

    void Start()
    {
        if (promptUI != null) promptUI.SetActive(false);
    }

    void Update()
    {
        // Check if player is in zone AND hits E
        if (playerInZone && Input.GetKeyDown(KeyCode.E))
        {
            // Only start if dialogue isn't already running
            if (DialogueManager.Instance != null && !DialogueManager.Instance.IsDialogueActive())
            {
                DialogueManager.Instance.StartDialogue(dialogue);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = true;
            if (promptUI != null) promptUI.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = false;

            if (promptUI != null) promptUI.SetActive(false);

            // Force close dialogue if the player walks away while talking
            if (DialogueManager.Instance != null)
            {
                DialogueManager.Instance.ForceCloseAllUI();
            }
        }
    }
}