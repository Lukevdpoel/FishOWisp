using UnityEngine;

public class ThirdPersonThrower : MonoBehaviour
{
    public GameObject throwablePrefab;         // The object to throw
    public Transform throwOrigin;              // Where to spawn the object
    public float throwForce = 15f;             // Force applied to the object

    [Header("Rotation Offset (Degrees)")]
    public Vector3 rotationOffsetEuler = Vector3.zero;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.E)) // 🔑 Throw when 'E' is pressed
        {
            Throw();
        }
    }

    void Throw()
    {
        if (throwablePrefab != null && throwOrigin != null)
        {
            Quaternion baseRotation = throwOrigin.rotation;
            Quaternion offset = Quaternion.Euler(rotationOffsetEuler);
            Quaternion spawnRotation = baseRotation * offset;

            GameObject thrownObject = Instantiate(throwablePrefab, throwOrigin.position, spawnRotation);

            Rigidbody rb = thrownObject.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.AddForce(throwOrigin.forward * throwForce, ForceMode.Impulse);
            }
        }
    }
}
