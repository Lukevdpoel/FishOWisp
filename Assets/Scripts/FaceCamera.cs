using UnityEngine;

public class FaceCamera : MonoBehaviour
{
    void LateUpdate()
    {
        if (Camera.main != null)
        {
            transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward);
        }
    }
}
