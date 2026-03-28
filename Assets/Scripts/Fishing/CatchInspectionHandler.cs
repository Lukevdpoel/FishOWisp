using UnityEngine;

public class CatchInspectionHandler : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator playerAnimator;
    [SerializeField] private Transform fishHoldPoint;
    [SerializeField] private PlayerController playerController;

    [Header("Inspection Settings")]
    [SerializeField] private float turnToCameraSpeed = 10f;
    [SerializeField] private float fishRotationSpeed = 45f;
    [SerializeField] private float minInspectionTime = 0.5f;

    [Header("Animation")]
    [SerializeField] private string showCatchAnimBool = "ShowCatch";

    public bool IsInspecting { get; private set; }

    public Transform GetFishHoldPoint() => fishHoldPoint;

    private CaughtFish caughtFish;
    private GameObject heldFishVisual;
    private float inspectionStartTime;
    private int hashShowCatch;

    private void Awake()
    {
        hashShowCatch = Animator.StringToHash(showCatchAnimBool);
        if (playerController == null) playerController = GetComponent<PlayerController>();
    }

    public void BeginInspection(CaughtFish fish, Transform playerModel)
    {
        IsInspecting = true;
        caughtFish = fish;
        inspectionStartTime = Time.time;

        if (playerController != null)
        {
            playerController.areControlsLocked = true;
            playerController.SetCatchCamera(true);
        }

        if (playerAnimator != null)
        {
            playerAnimator.SetBool(hashShowCatch, true);
        }

        if (fishHoldPoint != null && fish.preset.fishPrefab != null)
        {
            heldFishVisual = Instantiate(fish.preset.fishPrefab, fishHoldPoint);
            heldFishVisual.transform.localPosition = Vector3.zero;
            heldFishVisual.transform.localRotation = Quaternion.identity;
            if (playerModel != null) SetLayerRecursively(heldFishVisual, playerModel.gameObject.layer);
        }
    }

    public void UpdateInspection(Transform playerModel)
    {
        if (!IsInspecting) return;

        if (playerModel != null)
        {
            Vector3 camForward = Camera.main.transform.forward;
            camForward.y = 0;
            if (camForward != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(-camForward);
                playerModel.rotation = Quaternion.Slerp(playerModel.rotation, targetRotation, Time.deltaTime * turnToCameraSpeed);
            }
        }

        if (heldFishVisual != null)
        {
            heldFishVisual.transform.Rotate(Vector3.up, fishRotationSpeed * Time.deltaTime, Space.World);
        }
    }

    public bool TryFinishInspection(out CaughtFish fish)
    {
        fish = null;
        if (!IsInspecting) return false;
        if (Time.time <= inspectionStartTime + minInspectionTime) return false;

        fish = caughtFish;
        if (fish != null) PlayerInventory.Instance.AddFish(fish);
        Cleanup();
        return true;
    }

    public void ForceCleanup()
    {
        if (!IsInspecting) return;
        Cleanup();
    }

    private void Cleanup()
    {
        if (heldFishVisual != null) Destroy(heldFishVisual);
        if (playerAnimator != null) playerAnimator.SetBool(hashShowCatch, false);

        IsInspecting = false;
        caughtFish = null;
        heldFishVisual = null;
    }

    private void SetLayerRecursively(GameObject obj, int newLayer)
    {
        if (obj == null) return;
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            if (child == null) continue;
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }
}
