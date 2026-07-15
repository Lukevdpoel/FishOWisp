using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Part of InventoryUI (partial class). Serialized fields live in InventoryUI.cs.
public partial class InventoryUI
{
    // --- Loadout framing camera ---
    // While the gear menu is held open, swing the main camera to a pose in FRONT of the
    // angler looking back at the dangling tackle, so the player sees the bobber/lure they are
    // cycling with themselves behind it. PlayerCameraController yields the camera while the
    // inventory is open (see its UpdateCamera early-out), so we own it here uncontested.
    //
    // On close the player's orbit camera is resumed and snapped back by PlayerCameraController.
    // But a fixed camera (e.g. a shop interior view) is driven by nobody, so for that case we
    // captured its pose on open (BeginLoadoutCameraBorrow) and ease it back ourselves here.
    private void LateUpdate()
    {
        if (loadoutMode && IsInventoryOpen) UpdateLoadoutCamera();
        else if (loadoutCamRestoring) RestoreLoadoutCameraStep();
    }

    // Captures the active camera's pose when the gear menu opens, but only if it's a fixed camera
    // that PlayerCameraController won't restore (the player's own orbit camera is left alone — it
    // snaps back on its own). The captured pose is the target RestoreLoadoutCameraStep eases to.
    private void BeginLoadoutCameraBorrow()
    {
        Camera cam = ActiveCamera;
        if (cam == null) { loadoutBorrowedCamera = null; loadoutCamRestoring = false; return; }

        if (playerController == null) playerController = FindFirstObjectByType<PlayerController>();
        Transform restorable = playerController != null ? playerController.ActivePlayerCameraTransform : null;
        if (restorable != null && cam.transform == restorable)
        {
            // Player orbit camera — PlayerCameraController owns the reset, nothing to remember.
            loadoutBorrowedCamera = null;
            loadoutCamRestoring = false;
            return;
        }

        // Reopening mid-restore of the same camera: keep the original home pose as the target so a
        // half-finished restore doesn't become the new "home". Otherwise snapshot the current pose.
        if (loadoutBorrowedCamera != cam)
        {
            loadoutBorrowedCamera = cam;
            loadoutBorrowedCamPos = cam.transform.position;
            loadoutBorrowedCamRot = cam.transform.rotation;
        }
        loadoutCamRestoring = false;
    }

    // Closing the menu hands the player camera back to PlayerCameraController; a borrowed fixed
    // camera instead eases back to its captured pose over the next frames.
    private void EndLoadoutCameraBorrow()
    {
        if (loadoutBorrowedCamera != null) loadoutCamRestoring = true;
    }

    private void RestoreLoadoutCameraStep()
    {
        if (loadoutBorrowedCamera == null) { loadoutCamRestoring = false; return; }

        Transform ct = loadoutBorrowedCamera.transform;
        float t = 1f - Mathf.Exp(-cameraLerpSpeed * Time.unscaledDeltaTime);
        ct.position = Vector3.Lerp(ct.position, loadoutBorrowedCamPos, t);
        ct.rotation = Quaternion.Slerp(ct.rotation, loadoutBorrowedCamRot, t);

        if ((ct.position - loadoutBorrowedCamPos).sqrMagnitude < 0.0001f
            && Quaternion.Angle(ct.rotation, loadoutBorrowedCamRot) < 0.1f)
        {
            ct.SetPositionAndRotation(loadoutBorrowedCamPos, loadoutBorrowedCamRot);
            loadoutBorrowedCamera = null;
            loadoutCamRestoring = false;
        }
    }

    private void UpdateLoadoutCamera()
    {
        Camera cam = ActiveCamera;
        if (cam == null || fishingLine == null || fishingLine.rodTip == null) return;

        if (resolvedPlayer == null)
        {
            if (playerModel != null) resolvedPlayer = playerModel;
            else { var pc = FindFirstObjectByType<PlayerController>(); if (pc != null) resolvedPlayer = pc.transform; }
            if (resolvedPlayer == null) return;
        }

        Vector3 tackle = fishingLine.rodTip.position + Vector3.down * dangleDrop;

        // Horizontal direction from the player out to the tackle (≈ the way the rod points).
        Vector3 horizFwd = tackle - resolvedPlayer.position;
        horizFwd.y = 0f;
        if (horizFwd.sqrMagnitude < 0.0001f) { horizFwd = resolvedPlayer.forward; horizFwd.y = 0f; }
        if (horizFwd.sqrMagnitude < 0.0001f) horizFwd = Vector3.forward;
        horizFwd.Normalize();

        Vector3 targetPos = tackle + horizFwd * cameraFrontDistance + Vector3.up * cameraHeightOffset;
        Vector3 lookTarget = tackle + Vector3.up * cameraLookHeightOffset;
        Vector3 lookDir = lookTarget - targetPos;
        if (lookDir.sqrMagnitude < 0.0001f) return;
        Quaternion targetRot = Quaternion.LookRotation(lookDir.normalized, Vector3.up);

        // Ease in from wherever the camera currently is (the player view) for a smooth swing.
        float t = 1f - Mathf.Exp(-cameraLerpSpeed * Time.unscaledDeltaTime);
        cam.transform.position = Vector3.Lerp(cam.transform.position, targetPos, t);
        cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, targetRot, t);
    }

}
