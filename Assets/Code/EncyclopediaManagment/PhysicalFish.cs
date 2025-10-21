using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Collider))]
public class PhysicalFish : MonoBehaviour
{
    public CaughtFish FishData { get; private set; }
    private Vector3 screenPoint;
    private Vector3 offset;
    private Camera mainCamera; // Use the active camera

    void Awake()
    {
        // Find the camera. If you use a dedicated bucket cam, you'll need
        // to assign this more dynamically when the bucket opens.
        mainCamera = Camera.main;
    }

    public void Initialize(CaughtFish data)
    {
        FishData = data;
    }

    void OnMouseDown()
    {
        // Don't drag if pointer is over a UI element
        if (EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        // Find the active camera
        mainCamera = (Camera.main.gameObject.activeInHierarchy) ? Camera.main : FindObjectOfType<Camera>();
        if (mainCamera == null) return;

        // Calculate offset
        screenPoint = mainCamera.WorldToScreenPoint(gameObject.transform.position);
        offset = gameObject.transform.position - mainCamera.ScreenToWorldPoint(
            new Vector3(Input.mousePosition.x, Input.mousePosition.y, screenPoint.z));

        // Temporarily disable physics while dragging
        GetComponent<Rigidbody>().isKinematic = true;
    }

    void OnMouseDrag()
    {
        if (mainCamera == null) return;

        Vector3 curScreenPoint = new Vector3(Input.mousePosition.x, Input.mousePosition.y, screenPoint.z);
        Vector3 curPosition = mainCamera.ScreenToWorldPoint(curScreenPoint) + offset;
        transform.position = curPosition;
    }

    void OnMouseUp()
    {
        // Check if we are dropping onto a UI element
        if (EventSystem.current.IsPointerOverGameObject())
        {
            // The EventSystem will handle the drop via IDropHandler
            // We need to tell the VendorDropZone which fish this is.
            // A simple (but slightly messy) way is a static variable:
            FishDragger.CurrentDraggedFish = this;
        }
        else
        {
            // Dropped back in the world, re-enable physics
            GetComponent<Rigidbody>().isKinematic = false;
        }
    }
}

// A simple static class to pass the fish reference
public static class FishDragger
{
    public static PhysicalFish CurrentDraggedFish { get; set; }
}