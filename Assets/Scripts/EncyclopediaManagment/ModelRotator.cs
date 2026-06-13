using UnityEngine;
using UnityEngine.InputSystem;

public class ModelRotator : MonoBehaviour
{
    public float rotationSpeed = 100f;
    [Tooltip("Degrees per second the model spins at full right-stick deflection.")]
    public float gamepadRotationSpeed = 120f;
    private Vector2 lastMousePosition;
    private bool isDragging;

    void Update()
    {
        // Right stick spins the model while the notebook is open. The orbit camera stands
        // down whenever the notebook is open, so the stick is free here — and the gate
        // keeps this from spinning anything if the rotator is ever active during gameplay.
        if (NoteMenu.IsNotebookOpen)
        {
            float stickX = GamepadInput.Look.x;
            if (Mathf.Abs(stickX) > 0.01f)
            {
                // Same sign convention as the mouse drag: stick right = drag right.
                transform.Rotate(Vector3.up, -stickX * gamepadRotationSpeed * Time.unscaledDeltaTime, Space.World);
            }
        }

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
