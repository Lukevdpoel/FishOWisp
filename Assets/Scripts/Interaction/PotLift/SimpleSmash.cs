using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class SimpleSmash : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private PlayerController playerController;
    [SerializeField] private Animator animator;
    [SerializeField] private Transform playerModel;

    [Header("Rotation Fix")]
    [Range(-180, 180)]
    [SerializeField] private float modelForwardOffset = 0f;

    [Header("Visuals")]
    [SerializeField] private GameObject pointerPrefab;
    [SerializeField] private float pointerHeight = 1.8f; // Base height

    [Header("Visual Animation (The Wiggle)")]
    [Tooltip("How fast it bobs up and down")]
    [SerializeField] private float bobSpeed = 10f;
    [Tooltip("How far it moves (Keep this small, e.g. 0.1 or 0.2)")]
    [SerializeField] private float bobDistance = 0.2f;

    [Header("Setup")]
    [SerializeField] private Transform holdPoint;
    [SerializeField] private LayerMask obstacleLayer;

    [Header("Detection")]
    [SerializeField] private float potCenterHeightOffset = 0.5f;
    [SerializeField] private float raycastOriginHeight = 1.0f;

    [Header("Settings")]
    [SerializeField] private float maxPickupAngle = 60f;
    [SerializeField] private float liftDuration = 0.2f;
    [SerializeField] private float smashImpactDelay = 1.0f;
    [SerializeField] private float recoveryDelay = 0.1f;
    [SerializeField] private string smashTrigger = "LiftAndSmash";

    // Internal State
    private List<GameObject> potsInRange = new List<GameObject>();
    private GameObject currentBestPot;
    private GameObject activePointerInstance;
    private bool isBusy = false;

    void Start()
    {
        if (pointerPrefab != null)
        {
            activePointerInstance = Instantiate(pointerPrefab);
            activePointerInstance.SetActive(false);
        }
    }

    // --- TRIGGER LOGIC ---
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Pot") && !potsInRange.Contains(other.gameObject))
        {
            potsInRange.Add(other.gameObject);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Pot") && potsInRange.Contains(other.gameObject))
        {
            potsInRange.Remove(other.gameObject);
        }
    }

    // --- MAIN LOOP ---
    void Update()
    {
        // 1. Clean list
        potsInRange.RemoveAll(item => item == null);

        // 2. Hide pointer if busy
        if (isBusy || playerController.areControlsLocked)
        {
            if (activePointerInstance) activePointerInstance.SetActive(false);
            return;
        }

        // 3. Find Best Pot
        if (potsInRange.Count > 0)
        {
            currentBestPot = FindBestPotFromList();
        }
        else
        {
            currentBestPot = null;
        }

        // 4. Update Pointer Visuals (With Wiggle!)
        UpdatePointer();

        // 5. Input
        if ((Input.GetKeyDown(KeyCode.E) || GamepadInput.InteractPressed) && currentBestPot != null)
        {
            potsInRange.Remove(currentBestPot);
            StartCoroutine(PerformSmashSequence(currentBestPot));
        }
    }

    GameObject FindBestPotFromList()
    {
        Vector3 origin = playerModel != null ? playerModel.position : transform.position;
        Vector3 trueForward = GetAdjustedForward();

        GameObject best = null;
        float closestDist = Mathf.Infinity;

        foreach (GameObject pot in potsInRange)
        {
            Vector3 targetCenter = pot.transform.position + Vector3.up * potCenterHeightOffset;
            Vector3 rayOrigin = origin + Vector3.up * raycastOriginHeight;
            Vector3 dirToTarget = (targetCenter - rayOrigin).normalized;
            float dist = Vector3.Distance(rayOrigin, targetCenter);

            if (Vector3.Angle(trueForward, dirToTarget) > maxPickupAngle) continue;
            if (Physics.Raycast(rayOrigin, dirToTarget, dist, obstacleLayer)) continue;

            if (dist < closestDist)
            {
                closestDist = dist;
                best = pot;
            }
        }
        return best;
    }

    void UpdatePointer()
    {
        if (activePointerInstance == null) return;

        if (currentBestPot != null)
        {
            activePointerInstance.SetActive(true);

            // --- WIGGLE MATH ---
            // Calculate a new height based on time
            // Sin(Time * Speed) gives a value between -1 and 1
            float wiggleOffset = Mathf.Sin(Time.time * bobSpeed) * bobDistance;

            // Apply position: Pot Position + Base Height + Wiggle
            Vector3 finalPos = currentBestPot.transform.position + Vector3.up * (pointerHeight + wiggleOffset);

            activePointerInstance.transform.position = finalPos;

            // Billboard (Face Camera)
            if (Camera.main) activePointerInstance.transform.rotation = Camera.main.transform.rotation;
        }
        else
        {
            activePointerInstance.SetActive(false);
        }
    }

    private Vector3 GetAdjustedForward()
    {
        if (playerModel == null) return transform.forward;
        return Quaternion.Euler(0, modelForwardOffset, 0) * playerModel.forward;
    }

    // --- SMASH SEQUENCE ---
    IEnumerator PerformSmashSequence(GameObject pot)
    {
        isBusy = true;
        if (activePointerInstance) activePointerInstance.SetActive(false);
        playerController.areControlsLocked = true;

        if (animator) animator.SetTrigger(smashTrigger);

        Collider potCol = pot.GetComponent<Collider>();
        if (potCol) potCol.enabled = false;

        float timer = 0f;
        Vector3 startPos = pot.transform.position;

        while (timer < liftDuration)
        {
            timer += Time.deltaTime;
            float t = timer / liftDuration;
            pot.transform.position = Vector3.Lerp(startPos, holdPoint.position, t);
            yield return null;
        }

        pot.transform.SetParent(holdPoint, true);
        pot.transform.localPosition = Vector3.zero;

        float remainingWait = smashImpactDelay - liftDuration;
        if (remainingWait > 0) yield return new WaitForSeconds(remainingWait);

        Vector3 impactPoint = pot.transform.position;
        PotShatter shatter = pot.GetComponent<PotShatter>();
        if (shatter != null) shatter.Shatter(impactPoint);
        else Destroy(pot);
        if (recoveryDelay > 0) yield return new WaitForSeconds(recoveryDelay);

        playerController.areControlsLocked = false;
        isBusy = false;
    }
}