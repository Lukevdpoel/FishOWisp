using UnityEngine;

// Place this on the same GameObject as the Animator that plays the morph clips.
// It exists so animation events on those clips can find SetBallMeshActive in the
// Function dropdown — Unity only scans MonoBehaviours on the Animator's GameObject.
//
// The PlayerController reference auto-fills by walking up the hierarchy, so it
// works no matter how deeply nested the Animator is under PlayerMaster.
public class PlayerAnimationEventRelay : MonoBehaviour
{
    [SerializeField] private PlayerController playerController;

    void Reset()
    {
        // Editor convenience: fires when the component is first added.
        playerController = GetComponentInParent<PlayerController>();
    }

    void Awake()
    {
        // Runtime safety: if the reference is missing for any reason, find it now.
        if (playerController == null)
            playerController = GetComponentInParent<PlayerController>();
    }

    public void SetBallMeshActive(bool active)
    {
        if (playerController) playerController.SetBallMeshActive(active);
        else Debug.LogWarning("PlayerAnimationEventRelay: no PlayerController found in parents.", this);
    }

    // Parameterless variants for animation events typed in by name (no dropdown / no param matching needed).
    public void ShowBallMesh() { SetBallMeshActive(true); }
    public void HideBallMesh() { SetBallMeshActive(false); }
}
