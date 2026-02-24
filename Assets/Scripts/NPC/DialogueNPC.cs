using UnityEngine;

public class DialogueNPC : MonoBehaviour
{
    [Header("Dialogue Setup")]
    public Dialogue dialogue;
    [Tooltip("Drag the 'World Space Canvas' object that holds your 'E' icon here.")]
    public GameObject promptUI;

    private bool playerInZone;

    void Start()
    {
        // Ensure prompt is hidden at start
        if (promptUI != null) promptUI.SetActive(false);
    }

    void Update()
    {
        // 1. Check if Player is close AND presses E
        if (playerInZone && Input.GetKeyDown(KeyCode.E))
        {
            // 2. Check if we are already talking
            if (DialogueManager.Instance != null && !DialogueManager.Instance.IsDialogueActive())
            {
                // 3. Start Dialogue and pass 'transform' so the bubble knows where to go
                DialogueManager.Instance.StartDialogue(dialogue, this.transform);
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

            // Safety: If player runs away, close the window
            if (DialogueManager.Instance != null)
            {
                DialogueManager.Instance.ForceCloseAllUI();
            }
        }
    }
}