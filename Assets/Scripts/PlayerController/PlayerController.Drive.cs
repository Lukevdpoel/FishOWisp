using System.Collections;
using UnityEngine;

// Part of PlayerController (partial class). Serialized fields live in PlayerController.cs.
public partial class PlayerController
{
    // External-drive mode: while active, HandleMovement is bypassed and the player walks toward driveTarget.
    // IsDriveComplete becomes true once within arriveDistance. Caller should LockControls(true) before
    // starting and LockControls(false) (+ StopDrive) after.
    public void StartDrive(Vector3 worldTarget, float speed, float arriveDistance = 0.15f)
    {
        isDriven = true;
        driveTarget = worldTarget;
        driveSpeed = speed;
        driveArriveDistance = arriveDistance;
        isDriveComplete = false;
    }

    public void StopDrive()
    {
        isDriven = false;
        isDriveComplete = false;
        targetVelocity = new Vector3(0f, targetVelocity.y, 0f);
    }

    public bool IsDriveComplete => isDriveComplete;

    private bool isDriven;
    private Vector3 driveTarget;
    private float driveSpeed;
    private float driveArriveDistance;
    private bool isDriveComplete;

    // Smoothly lerps the player to worldTarget over duration with the CharacterController disabled,
    // so the player can't snag on geometry. Used to reposition the player to a clean start point
    // (just inside / just outside the door) before walking through the scripted path.
    public Coroutine StartGlide(Vector3 worldTarget, float duration, Quaternion? faceRotation = null)
    {
        return StartCoroutine(GlideRoutine(worldTarget, duration, faceRotation));
    }

    private IEnumerator GlideRoutine(Vector3 worldTarget, float duration, Quaternion? faceRotation)
    {
        if (characterController != null) characterController.enabled = false;

        Vector3 startPos = transform.position;
        Quaternion startRot = playerModel ? playerModel.rotation : Quaternion.identity;
        Quaternion targetRot = faceRotation ?? startRot;

        if (animator) animator.SetFloat(hashSpeed, 0f);
        targetVelocity = Vector3.zero;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
            transform.position = Vector3.Lerp(startPos, worldTarget, k);
            if (playerModel) playerModel.rotation = Quaternion.Slerp(startRot, targetRot, k);
            yield return null;
        }
        transform.position = worldTarget;
        if (playerModel) playerModel.rotation = targetRot;
        targetModelRotation = targetRot;

        if (characterController != null) characterController.enabled = true;
    }

    private void HandleDrive()
    {
        Vector3 flatPos = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatTarget = new Vector3(driveTarget.x, 0f, driveTarget.z);
        Vector3 toTarget = flatTarget - flatPos;
        float dist = toTarget.magnitude;

        if (dist <= driveArriveDistance)
        {
            targetVelocity = new Vector3(0f, targetVelocity.y, 0f);
            if (animator) animator.SetFloat(hashSpeed, 0f, speedAnimDampTime, Time.deltaTime);
            isDriveComplete = true;
            return;
        }

        Vector3 dir = toTarget / dist;
        targetVelocity = new Vector3(dir.x * driveSpeed, targetVelocity.y, dir.z * driveSpeed);

        Quaternion lookRot = Quaternion.LookRotation(dir);
        if (playerModel)
            playerModel.rotation = Quaternion.Slerp(playerModel.rotation, lookRot, rotationSpeed * Time.deltaTime);
        targetModelRotation = lookRot;

        if (animator) animator.SetFloat(hashSpeed, driveSpeed, speedAnimDampTime, Time.deltaTime);
    }

}
