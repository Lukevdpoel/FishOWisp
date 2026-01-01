using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class OsuFishingMinigame : MonoBehaviour
{
    [Header("References")]
    public GameObject dotPrefab;
    public Transform dotContainer;

    [Header("Spawning Settings")]
    public float spawnInterval = 0.5f;
    [Tooltip("Total time a dot stays on screen before being destroyed.")]
    public float dotLifetime = 3.0f;
    [Tooltip("Time in seconds before a dot starts its despawn timer/fade out.")]
    public float despawnDelay = 1.0f;
    public int minDotsForCombo = 3;

    [Header("Progress Settings")]
    public float drainRate = 5f;
    public float progressPerDot = 3f;
    public float comboMultiplier = 1.5f;

    private Transform trackingTarget;
    private Camera mainCamera;
    private float spawnTimer;
    private List<FishingDot> activeDots = new List<FishingDot>();
    private List<FishingDot> currentStroke = new List<FishingDot>();
    private bool isDragging;

    private void Awake()
    {
        mainCamera = Camera.main;
    }

    public void Activate()
    {
        gameObject.SetActive(true);
        ClearDots();
        spawnTimer = 0f;
        isDragging = false;
        currentStroke.Clear();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Deactivate()
    {
        ClearDots();
        gameObject.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void SetTrackingTarget(Transform target)
    {
        trackingTarget = target;
    }

    public float UpdateMinigame(float currentProgress, float maxProgress)
    {
        if (Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        currentProgress -= drainRate * Time.deltaTime;

        HandleSpawning();

        float progressGained = HandleInput();
        currentProgress += progressGained;

        HandleDotLifecycles();

        return Mathf.Clamp(currentProgress, 0f, maxProgress);
    }

    private void HandleSpawning()
    {
        if (trackingTarget == null || dotPrefab == null) return;

        spawnTimer -= Time.deltaTime;
        if (spawnTimer <= 0f)
        {
            SpawnDotAtTarget();
            spawnTimer = spawnInterval;
        }
    }

    private void SpawnDotAtTarget()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (dotContainer == null) dotContainer = transform;

        Vector3 screenPos = mainCamera.WorldToScreenPoint(trackingTarget.position);

        GameObject dotObj = Instantiate(dotPrefab, dotContainer);
        dotObj.transform.position = screenPos;

        FishingDot dot = dotObj.GetComponent<FishingDot>();
        if (dot != null)
        {
            dot.Initialize(dotLifetime);
            activeDots.Add(dot);
        }

        // Logic Change: Start fading dots as soon as we reach the minimum combo count.
        // If minDotsForCombo is 3, we want to start fading the oldest dot as soon as the 3rd one spawns.
        if (activeDots.Count >= minDotsForCombo)
        {
            foreach (var d in activeDots)
            {
                if (!d.IsDespawning)
                {
                    d.StartDespawn(dotLifetime);
                    break;
                }
            }
        }
    }

    private float HandleInput()
    {
        float addedProgress = 0f;

        if (Input.GetMouseButtonDown(0))
        {
            isDragging = true;
            currentStroke.Clear();
        }

        if (Input.GetMouseButton(0) && isDragging)
        {
            Vector2 mousePos = Input.mousePosition;

            foreach (var dot in activeDots)
            {
                if (!dot.IsHit && dot.IsMouseOver(mousePos))
                {
                    dot.OnHit();
                    currentStroke.Add(dot);
                }
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            isDragging = false;

            if (currentStroke.Count >= minDotsForCombo)
            {
                float baseScore = currentStroke.Count * progressPerDot;

                float extraDots = currentStroke.Count - minDotsForCombo;
                float multiplier = 1f + (extraDots * 0.1f);

                addedProgress = baseScore * multiplier;
                Debug.Log($"Combo! Hit {currentStroke.Count} dots. Gained {addedProgress:F1} progress.");

                foreach (var dot in currentStroke)
                {
                    activeDots.Remove(dot);
                    if (dot != null) Destroy(dot.gameObject);
                }
            }
            else
            {
                foreach (var dot in currentStroke)
                {
                    activeDots.Remove(dot);
                    if (dot != null) Destroy(dot.gameObject);
                }
            }
            currentStroke.Clear();
        }

        return addedProgress;
    }

    private void HandleDotLifecycles()
    {
        for (int i = activeDots.Count - 1; i >= 0; i--)
        {
            FishingDot dot = activeDots[i];

            if (dot.UpdateDespawn())
            {
                activeDots.RemoveAt(i);
                Destroy(dot.gameObject);
            }
        }
    }

    private void ClearDots()
    {
        foreach (var dot in activeDots)
        {
            if (dot != null) Destroy(dot.gameObject);
        }
        activeDots.Clear();
        currentStroke.Clear();
    }
}