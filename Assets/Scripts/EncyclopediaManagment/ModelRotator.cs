using UnityEngine;

public class ModelRotator : MonoBehaviour
{
    public float rotationSpeed = 100f;
    private Vector3 lastMousePosition;

    void Update()
    {
        // 1. Reset last position on the frame the button is clicked
        if (Input.GetMouseButtonDown(0))
        {
            lastMousePosition = Input.mousePosition;
        }

        // 2. Calculate rotation while holding the button
        if (Input.GetMouseButton(0))
        {
            Vector3 delta = Input.mousePosition - lastMousePosition;

            // CHANGE: Use unscaledDeltaTime so it works while Paused
            float rotX = delta.y * rotationSpeed * Time.unscaledDeltaTime;
            float rotY = -delta.x * rotationSpeed * Time.unscaledDeltaTime;

            // Apply rotation
            transform.Rotate(Vector3.up, rotY, Space.World);
            transform.Rotate(Vector3.right, rotX, Space.World);

            lastMousePosition = Input.mousePosition;
        }
    }
}