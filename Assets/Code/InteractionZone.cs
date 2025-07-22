using UnityEngine;

public class InteractionZone : MonoBehaviour
{
    private IInteractable interactable;
    private bool playerInZone;

    public GameObject promptUI;            // Full UI GameObject (can contain text/icon)
    public TMPro.TMP_Text promptLabel;     // Optional label inside it (for text like “Press E to Talk”)

    void Start()
    {
        interactable = GetComponent<IInteractable>();
        if (promptUI != null)
            promptUI.SetActive(false);
    }

    void Update()
    {
        if (playerInZone && Input.GetKeyDown(KeyCode.E))
        {
            interactable?.Interact();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = true;
            if (promptUI != null)
                promptUI.SetActive(true);

            if (promptLabel != null && interactable != null)
                promptLabel.text = interactable.GetPromptMessage();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = false;
            if (promptUI != null)
                promptUI.SetActive(false);
        }
    }
}
