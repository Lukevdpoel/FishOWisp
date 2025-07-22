using UnityEngine;

public class DialogueNPC : MonoBehaviour
{
    public Dialogue dialogue;
    public DialogueManager dialogueManager;
    public GameObject promptUI;

    private bool playerInZone;

    void Start()
    {
        if (promptUI != null)
            promptUI.SetActive(false);
    }

    void Update()
    {
        if (playerInZone && Input.GetKeyDown(KeyCode.E))
        {
            if (!dialogueManager.IsDialogueActive())
                dialogueManager.StartDialogue(dialogue);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = true;
            if (promptUI != null)
                promptUI.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = false;

            if (promptUI != null)
                promptUI.SetActive(false);

            // END DIALOGUE when leaving
            if (dialogueManager != null && dialogueManager.IsDialogueActive())
            {
                // Force it to end
                typeof(DialogueManager)
                    .GetMethod("EndDialogue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.Invoke(dialogueManager, null);
            }
        }
    }

}
