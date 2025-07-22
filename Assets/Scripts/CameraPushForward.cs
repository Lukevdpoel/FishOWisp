using UnityEngine;

[RequireComponent(typeof(Camera))]
public class SmoothCameraPushForward : MonoBehaviour
{
    public float checkRadius = 0.3f;
    public float pushDistance = 0.1f;
    public float maxPush = 2f;
    public float smoothSpeed = 10f;
    public LayerMask collisionLayers;

    private Vector3 originalLocalPosition;
    private Transform camTransform;

    void Start()
    {
        camTransform = transform;
        originalLocalPosition = camTransform.localPosition;
    }

    void LateUpdate()
    {
        if (camTransform.parent == null)
        {
            Debug.LogWarning("Camera must be a child of another object.");
            return;
        }

        Vector3 startWorldPos = camTransform.parent.TransformPoint(originalLocalPosition);
        Vector3 direction = camTransform.forward;
        Vector3 targetWorldPos = startWorldPos;

        float totalPush = 0f;
        Vector3 testPos = startWorldPos;

        bool detectedCollision = false;

        while (totalPush < maxPush)
        {
            if (!Physics.CheckSphere(testPos, checkRadius, collisionLayers, QueryTriggerInteraction.Ignore))
                break;

            detectedCollision = true;
            testPos += direction * pushDistance;
            totalPush += pushDistance;
        }

        targetWorldPos = testPos;
        Vector3 targetLocalPos = camTransform.parent.InverseTransformPoint(targetWorldPos);

        camTransform.localPosition = Vector3.Lerp(camTransform.localPosition, targetLocalPos, Time.deltaTime * smoothSpeed);

        if (detectedCollision)
        {
            Debug.Log("Camera collision detected. Pushing forward.");
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, checkRadius);
    }
}
