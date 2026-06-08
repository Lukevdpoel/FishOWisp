using UnityEngine;
using UnityEngine.InputSystem;

public class ModelRotator : MonoBehaviour
{
    public float rotationSpeed = 100f;
    private Vector2 lastMousePosition;
    private bool isDragging;

    void Update()
    {
        if (Mouse.current == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            lastMousePosition = Mouse.current.position.ReadValue();
            isDragging = true;
        }
        else if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            isDragging = false;
        }

        if (isDragging && Mouse.current.leftButton.isPressed)
        {
            Vector2 currentPos = Mouse.current.position.ReadValue();
            Vector2 delta = currentPos - lastMousePosition;
            float rotY = -delta.x * rotationSpeed * Time.unscaledDeltaTime;
            transform.Rotate(Vector3.up, rotY, Space.World);
            lastMousePosition = currentPos;
        }
    }
}
