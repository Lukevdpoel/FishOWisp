using UnityEngine;

public class ToggleUIVisibility : MonoBehaviour
{
    // Drag the UI element you want to show/hide into this slot in the Inspector.
    public GameObject uiElement;

    // This function is called when another collider enters this trigger.
    private void OnTriggerEnter(Collider other)
    {
        // We check if the object that entered has the "Player" tag.
        if (other.CompareTag("Player"))
        {
            // If it's the player, activate the UI element.
            uiElement.SetActive(true);
        }
    }

    // This function is called when a collider that was inside leaves this trigger.
    private void OnTriggerExit(Collider other)
    {
        // We check if the object that left has the "Player" tag.
        if (other.CompareTag("Player"))
        {
            // If it's the player, deactivate the UI element.
            uiElement.SetActive(false);
        }
    }
}