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
            float rotX = delta.y * rotationSpeed * Time.deltaTime;
            float rotY = -delta.x * rotationSpeed * Time.deltaTime;

            transform.Rotate(Vector3.up, rotY, Space.World);
            transform.Rotate(Vector3.right, rotX, Space.World);

            lastMousePosition = Input.mousePosition;
        }
    }
}
