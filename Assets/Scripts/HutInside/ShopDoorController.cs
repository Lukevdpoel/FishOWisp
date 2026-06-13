using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Collider))]
public class ShopDoorController : MonoBehaviour
{
    private enum State { Outside, EnteringOpenDoor, WalkingIn, EnteringCloseDoor, Inside, ExitingOpenDoor, WalkingOut, ExitingCloseDoor }

    [Header("Shop References")]
    [Tooltip("Root of everything that only exists inside the shop (interior mesh, props, NPCs, interior camera). Toggled active/inactive.")]
    [SerializeField] private GameObject shopRoot;
    [Tooltip("Camera used while the player is inside the shop. Should live under shopRoot.")]
    [SerializeField] private Camera interiorCamera;
    [Tooltip("Fallback exterior camera. Auto-detected from Camera.main if left empty.")]
    [SerializeField] private Camera exteriorCamera;

    [Header("Door")]
    [Tooltip("Transform that pivots when opening/closing. Rotated locally by openAngle.")]
    [SerializeField] private Transform doorPivot;
    [SerializeField] private Vector3 openEulerOffset = new Vector3(0f, 90f, 0f);
    [SerializeField] private float openDuration = 0.45f;
    [SerializeField] private float closeDuration = 0.4f;
    [SerializeField] private AnimationCurve doorEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Wall Fade")]
    [SerializeField] private WallFader wallFader;

    [Header("Walk Markers")]
    [Tooltip("Where the player smoothly glides to before walking in. Place right in front of the door, outside.")]
    [SerializeField] private Transform entryStartMarker;
    [Tooltip("Where the player walks to after entering. Should be just inside the door.")]
    [SerializeField] private Transform entryMarker;
    [Tooltip("Where the player smoothly glides to before walking out. Place right in front of the door, inside.")]
    [SerializeField] private Transform exitStartMarker;
    [Tooltip("Where the player is placed/walks to when leaving. Should be just outside the door.")]
    [SerializeField] private Transform exitMarker;
    [Tooltip("Facing the player should snap to after fully entering.")]
    [SerializeField] private Transform faceAfterEntry;
    [Tooltip("Facing the player should snap to after fully exiting.")]
    [SerializeField] private Transform faceAfterExit;
    [SerializeField] private float walkSpeed = 3f;
    [Tooltip("Duration of the smooth glide from the player's current position to the start marker.")]
    [SerializeField] private float glideDuration = 0.35f;

    [Header("Interaction")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField] private Sprite promptIcon;

    [Header("Exit Trigger (optional)")]
    [Tooltip("Optional second trigger inside the shop. When player enters it and presses E, the exit sequence runs. Leave null to use this same trigger.")]
    [SerializeField] private Collider insideExitTrigger;

    [Header("Overlay Cameras")]
    [Tooltip("URP Overlay cameras (e.g. the player's NotebookCamera) that should follow the active base camera. They're removed from the old stack and added to the new one during the swap.")]
    [SerializeField] private Camera[] overlayCameras;

    [Header("Camera Transition")]
    [Tooltip("How long the camera takes to fly between exterior and interior views. 0 = hard cut.")]
    [SerializeField] private float cameraTransitionDuration = 0.6f;
    [SerializeField] private AnimationCurve cameraTransitionEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Vector3 interiorHomePos;
    private Quaternion interiorHomeRot;
    private float interiorHomeFov;

    private State state = State.Outside;
    private bool playerInOutsideZone;
    private bool playerInInsideZone;
    private PlayerController currentPlayer;
    private Quaternion doorClosedLocalRot;

    private void Awake()
    {
        if (doorPivot != null) doorClosedLocalRot = doorPivot.localRotation;

        // Capture the interior camera's editor pose BEFORE shopRoot is deactivated, so we can
        // lerp back to it on entry and lerp away from it on exit.
        if (interiorCamera != null)
        {
            interiorHomePos = interiorCamera.transform.position;
            interiorHomeRot = interiorCamera.transform.rotation;
            interiorHomeFov = interiorCamera.fieldOfView;
        }

        if (shopRoot != null) shopRoot.SetActive(false);

        if (exteriorCamera == null && Camera.main != null) exteriorCamera = Camera.main;

        if (insideExitTrigger != null)
        {
            var relay = insideExitTrigger.gameObject.AddComponent<ShopInsideTriggerRelay>();
            relay.owner = this;
        }
    }

    private void Update()
    {
        if (state != State.Outside && state != State.Inside) return;
        if (currentPlayer == null) return;

        bool canEnter = state == State.Outside && playerInOutsideZone;
        bool canExit = state == State.Inside && (insideExitTrigger == null ? playerInOutsideZone : playerInInsideZone);

        bool interactPressed = Input.GetKeyDown(interactKey) || GamepadInput.InteractPressed;
        if (canEnter && interactPressed) StartCoroutine(EnterSequence());
        else if (canExit && interactPressed) StartCoroutine(ExitSequence());
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        currentPlayer = other.GetComponent<PlayerController>();
        playerInOutsideZone = true;
        ShowPromptForCurrentState();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInOutsideZone = false;
        if (InteractionPromptUI.Instance != null) InteractionPromptUI.Instance.Hide();
    }

    // Called by ShopInsideTriggerRelay if an inside exit trigger is configured.
    internal void OnInsideTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInInsideZone = true;
        if (state == State.Inside) ShowPromptForCurrentState();
    }

    internal void OnInsideTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInInsideZone = false;
        if (InteractionPromptUI.Instance != null) InteractionPromptUI.Instance.Hide();
    }

    private void ShowPromptForCurrentState()
    {
        if (currentPlayer == null || InteractionPromptUI.Instance == null) return;
        if (state == State.Outside && playerInOutsideZone)
            InteractionPromptUI.Instance.Show(currentPlayer.transform, promptIcon);
        else if (state == State.Inside && (insideExitTrigger == null ? playerInOutsideZone : playerInInsideZone))
            InteractionPromptUI.Instance.Show(currentPlayer.transform, promptIcon);
    }

    private IEnumerator EnterSequence()
    {
        // Flip state BEFORE any yields so a spammed E press in the same frame can't start a second
        // sequence — Update only fires on State.Outside / State.Inside.
        state = State.EnteringOpenDoor;

        if (InteractionPromptUI.Instance != null) InteractionPromptUI.Instance.Hide();
        currentPlayer.LockControls(true);

        // Activate interior BEHIND the closed door so the shader/upload spike is hidden by the door swing.
        if (shopRoot != null) shopRoot.SetActive(true);
        yield return null; // let URP/Animator register the newly-active hierarchy

        // Glide the player to a clean position in front of the door before the door opens, so the
        // scripted walk never has to navigate around walls/props.
        if (entryStartMarker != null)
            yield return currentPlayer.StartGlide(entryStartMarker.position, glideDuration, entryStartMarker.rotation);
        Coroutine fade = wallFader != null ? wallFader.FadeOut() : null;
        yield return RotateDoor(doorClosedLocalRot, doorClosedLocalRot * Quaternion.Euler(openEulerOffset), openDuration);
        if (fade != null) yield return fade;

        // Swap to interior camera right as the door finishes opening.
        yield return SwapCamera(toInterior: true);

        state = State.WalkingIn;
        if (entryMarker != null)
        {
            currentPlayer.StartDrive(entryMarker.position, walkSpeed);
            while (!currentPlayer.IsDriveComplete) yield return null;
            currentPlayer.StopDrive();
        }
        if (faceAfterEntry != null) currentPlayer.SetFacing(faceAfterEntry.rotation);

        state = State.EnteringCloseDoor;
        yield return RotateDoor(doorPivot.localRotation, doorClosedLocalRot, closeDuration);
        // Wall stays faded the whole time the player is inside so the interior camera can see through it.
        // FadeIn only runs at the end of ExitSequence.

        currentPlayer.LockControls(false);
        state = State.Inside;
        ShowPromptForCurrentState();
    }

    private IEnumerator ExitSequence()
    {
        // Same guard as EnterSequence — flip state before any yields so spammed E can't double-fire.
        state = State.ExitingOpenDoor;

        if (InteractionPromptUI.Instance != null) InteractionPromptUI.Instance.Hide();
        currentPlayer.LockControls(true);

        // Glide the player to a clean position in front of the door (inside) before the exit sequence,
        // so the walk-out path is always the same regardless of where they were standing in the shop.
        if (exitStartMarker != null)
            yield return currentPlayer.StartGlide(exitStartMarker.position, glideDuration, exitStartMarker.rotation);
        Coroutine fade = wallFader != null ? wallFader.FadeOut() : null;
        yield return RotateDoor(doorClosedLocalRot, doorClosedLocalRot * Quaternion.Euler(openEulerOffset), openDuration);
        if (fade != null) yield return fade;

        // Swap back to exterior camera before walking out, so the player sees the world from the right angle.
        yield return SwapCamera(toInterior: false);

        state = State.WalkingOut;
        if (exitMarker != null)
        {
            currentPlayer.StartDrive(exitMarker.position, walkSpeed);
            while (!currentPlayer.IsDriveComplete) yield return null;
            currentPlayer.StopDrive();
        }
        if (faceAfterExit != null) currentPlayer.SetFacing(faceAfterExit.rotation);

        state = State.ExitingCloseDoor;
        yield return RotateDoor(doorPivot.localRotation, doorClosedLocalRot, closeDuration);
        if (wallFader != null) yield return wallFader.FadeIn();

        if (shopRoot != null) shopRoot.SetActive(false);

        currentPlayer.LockControls(false);
        state = State.Outside;
        playerInInsideZone = false;
        ShowPromptForCurrentState();
    }

    private IEnumerator SwapCamera(bool toInterior)
    {
        if (toInterior) yield return EnterCameraTransition();
        else yield return ExitCameraTransition();
    }

    // Entry: interior camera starts at the exterior's current pose, then lerps to its editor-home pose.
    private IEnumerator EnterCameraTransition()
    {
        if (exteriorCamera == null || interiorCamera == null)
        {
            HardSwap(true);
            yield break;
        }

        // Snapshot where the exterior camera is right now — that's the lerp start pose.
        Vector3 startPos = exteriorCamera.transform.position;
        Quaternion startRot = exteriorCamera.transform.rotation;
        float startFov = exteriorCamera.fieldOfView;

        // Move the interior camera onto that pose so the swap is invisible.
        interiorCamera.transform.SetPositionAndRotation(startPos, startRot);
        interiorCamera.fieldOfView = startFov;

        // Hand cameras over: from now on the interior camera is what the player sees.
        TransferOverlays(exteriorCamera, interiorCamera);
        EnableCamera(interiorCamera, true);
        EnableCamera(exteriorCamera, false);

        // Lerp the interior camera from the exterior's pose back to its editor home pose.
        yield return LerpInteriorCamera(startPos, startRot, startFov, interiorHomePos, interiorHomeRot, interiorHomeFov);

        currentPlayer.SetStaticCamera(true, interiorCamera.transform, followPlayer: false);
    }

    // Exit: interior camera lerps from its home pose to wherever the exterior camera currently sits
    // (it's still being driven by PlayerCameraController in the background while inactive), then swap.
    private IEnumerator ExitCameraTransition()
    {
        if (exteriorCamera == null || interiorCamera == null)
        {
            HardSwap(false);
            yield break;
        }

        // Release static-camera input remapping before the lerp so post-swap WASD doesn't reference
        // the interior camera mid-flight if something else queries it.
        currentPlayer.SetStaticCamera(false, null);

        Vector3 startPos = interiorCamera.transform.position;
        Quaternion startRot = interiorCamera.transform.rotation;
        float startFov = interiorCamera.fieldOfView;

        Vector3 endPos = exteriorCamera.transform.position;
        Quaternion endRot = exteriorCamera.transform.rotation;
        float endFov = exteriorCamera.fieldOfView;

        // PlayerCameraController keeps driving the exterior camera every LateUpdate, so endPos/endRot
        // can drift slightly during the lerp. We refresh the target each frame inside LerpInteriorCamera.
        yield return LerpInteriorCameraToMovingTarget(startPos, startRot, startFov, exteriorCamera);

        TransferOverlays(interiorCamera, exteriorCamera);
        EnableCamera(exteriorCamera, true);
        EnableCamera(interiorCamera, false);

        // Restore the interior camera's home pose so the next entry starts from a known state.
        interiorCamera.transform.SetPositionAndRotation(interiorHomePos, interiorHomeRot);
        interiorCamera.fieldOfView = interiorHomeFov;
    }

    private IEnumerator LerpInteriorCamera(Vector3 fromPos, Quaternion fromRot, float fromFov, Vector3 toPos, Quaternion toRot, float toFov)
    {
        if (cameraTransitionDuration <= 0f)
        {
            interiorCamera.transform.SetPositionAndRotation(toPos, toRot);
            interiorCamera.fieldOfView = toFov;
            yield break;
        }
        float t = 0f;
        while (t < cameraTransitionDuration)
        {
            t += Time.deltaTime;
            float k = cameraTransitionEase.Evaluate(Mathf.Clamp01(t / cameraTransitionDuration));
            interiorCamera.transform.SetPositionAndRotation(
                Vector3.Lerp(fromPos, toPos, k),
                Quaternion.Slerp(fromRot, toRot, k));
            interiorCamera.fieldOfView = Mathf.Lerp(fromFov, toFov, k);
            yield return null;
        }
        interiorCamera.transform.SetPositionAndRotation(toPos, toRot);
        interiorCamera.fieldOfView = toFov;
    }

    private IEnumerator LerpInteriorCameraToMovingTarget(Vector3 fromPos, Quaternion fromRot, float fromFov, Camera movingTarget)
    {
        if (cameraTransitionDuration <= 0f) yield break;
        float t = 0f;
        while (t < cameraTransitionDuration)
        {
            t += Time.deltaTime;
            float k = cameraTransitionEase.Evaluate(Mathf.Clamp01(t / cameraTransitionDuration));
            Vector3 toPos = movingTarget.transform.position;
            Quaternion toRot = movingTarget.transform.rotation;
            float toFov = movingTarget.fieldOfView;
            interiorCamera.transform.SetPositionAndRotation(
                Vector3.Lerp(fromPos, toPos, k),
                Quaternion.Slerp(fromRot, toRot, k));
            interiorCamera.fieldOfView = Mathf.Lerp(fromFov, toFov, k);
            yield return null;
        }
    }

    private void HardSwap(bool toInterior)
    {
        Camera from = toInterior ? exteriorCamera : interiorCamera;
        Camera to = toInterior ? interiorCamera : exteriorCamera;
        TransferOverlays(from, to);
        EnableCamera(from, false);
        EnableCamera(to, true);
        if (toInterior)
            currentPlayer.SetStaticCamera(true, interiorCamera != null ? interiorCamera.transform : null, followPlayer: false);
        else
            currentPlayer.SetStaticCamera(false, null);
    }

    private void EnableCamera(Camera cam, bool enabled)
    {
        if (cam == null) return;
        if (enabled)
        {
            cam.gameObject.SetActive(true);
            cam.tag = "MainCamera";
        }
        else
        {
            cam.gameObject.SetActive(false);
        }
        var listener = cam.GetComponent<AudioListener>();
        if (listener != null) listener.enabled = enabled;
    }

    private void TransferOverlays(Camera oldBase, Camera newBase)
    {
        if (overlayCameras == null || overlayCameras.Length == 0) return;

        var oldData = oldBase != null ? oldBase.GetUniversalAdditionalCameraData() : null;
        var newData = newBase != null ? newBase.GetUniversalAdditionalCameraData() : null;

        foreach (var overlay in overlayCameras)
        {
            if (overlay == null) continue;
            if (oldData != null && oldData.cameraStack.Contains(overlay))
                oldData.cameraStack.Remove(overlay);
            if (newData != null && !newData.cameraStack.Contains(overlay))
                newData.cameraStack.Add(overlay);
        }
    }

    private IEnumerator RotateDoor(Quaternion from, Quaternion to, float duration)
    {
        if (doorPivot == null || duration <= 0f)
        {
            if (doorPivot != null) doorPivot.localRotation = to;
            yield break;
        }
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = doorEase.Evaluate(Mathf.Clamp01(t / duration));
            doorPivot.localRotation = Quaternion.Slerp(from, to, k);
            yield return null;
        }
        doorPivot.localRotation = to;
    }
}

// Tiny relay so a separate inside collider can forward its trigger events to the door controller.
public class ShopInsideTriggerRelay : MonoBehaviour
{
    internal ShopDoorController owner;
    private void OnTriggerEnter(Collider other) { if (owner != null) owner.OnInsideTriggerEnter(other); }
    private void OnTriggerExit(Collider other) { if (owner != null) owner.OnInsideTriggerExit(other); }
}
