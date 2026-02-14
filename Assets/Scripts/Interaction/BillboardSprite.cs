using UnityEngine;

public class BillboardSprite : MonoBehaviour
{
    public enum BillboardType { Vertical, Horizontal, FullCameraFacing }

    [Header("Settings")]
    [Tooltip("Vertical = Upright (Trees, NPCs)\nHorizontal = Flat (Runes, selection rings)\nFull = Faces camera perfectly (Projectiles, Particles)")]
    public BillboardType type = BillboardType.Vertical;

    [Tooltip("If true, the sprite can spin 360. If false, it's locked to fixed angles.")]
    public bool smoothRotation = true;

    private Camera mainCam;

    void Start()
    {
        mainCam = Camera.main;
    }

    void LateUpdate()
    {
        if (!mainCam) return;

        switch (type)
        {
            case BillboardType.Vertical:
                // Stands up like a tree, rotates on Y axis only
                Vector3 lookPos = transform.position + mainCam.transform.rotation * Vector3.forward;
                lookPos.y = transform.position.y; // Lock Y
                transform.LookAt(lookPos);
                break;

            case BillboardType.Horizontal:
                // Lies flat on the ground (XZ plane)
                transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                break;

            case BillboardType.FullCameraFacing:
                // Faces camera perfectly (Spherical)
                transform.rotation = mainCam.transform.rotation;
                break;
        }
    }
}