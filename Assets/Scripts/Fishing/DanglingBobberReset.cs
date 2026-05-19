using UnityEngine;

public class DanglingBobberReset : MonoBehaviour
{
    [SerializeField] private Rigidbody dobberRigidbody;

    private Vector3 restLocalPosition;
    private Quaternion restLocalRotation;
    private bool restCaptured;

    private void Awake()
    {
        if (dobberRigidbody == null)
        {
            Debug.LogWarning($"[DanglingBobberReset] No dobberRigidbody assigned on {name}.");
            return;
        }

        Transform t = dobberRigidbody.transform;
        restLocalPosition = t.localPosition;
        restLocalRotation = t.localRotation;
        restCaptured = true;
    }

    private void OnEnable()
    {
        if (!restCaptured || dobberRigidbody == null) return;

        Transform t = dobberRigidbody.transform;
        t.localPosition = restLocalPosition;
        t.localRotation = restLocalRotation;
        dobberRigidbody.linearVelocity = Vector3.zero;
        dobberRigidbody.angularVelocity = Vector3.zero;
    }
}
