using UnityEngine;

public class TriggerUIActivator : MonoBehaviour
{
    public GameObject uiElement;           // The main UI to toggle
    public GameObject interactionPrompt;   // "Press E to interact" text

    private bool playerIsInRange = false;
    private bool uiIsActive = false;

    void Update()
    {
        if (playerIsInRange && Input.GetKeyDown(KeyCode.E))
        {
            uiIsActive = !uiIsActive;
            uiElement.SetActive(uiIsActive);

            // Hide prompt when UI is shown, show when UI is hidden (if player is still nearby)
            interactionPrompt.SetActive(!uiIsActive && playerIsInRange);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerIsInRange = true;

            if (!uiIsActive)
                interactionPrompt.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerIsInRange = false;

            // Hide both UI and prompt on exit
            interactionPrompt.SetActive(false);
            uiElement.SetActive(false);
            uiIsActive = false;
        }
    }
}
