using UnityEngine;

public class ModelRotator : MonoBehaviour
{
    public float rotationSpeed = 100f;
    private Vector3 lastMousePosition;

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            lastMousePosition = Input.mousePosition;
        }

        if (Input.GetMouseButton(0))
        {
            Vector3 delta = Input.mousePosition - lastMousePosition;

            float rotY = -delta.x * rotationSpeed * Time.unscaledDeltaTime;

            transform.Rotate(Vector3.up, rotY, Space.World);

            lastMousePosition = Input.mousePosition;
        }
    }
}