using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using System.Collections; // Required for IEnumerator
using System.Collections.Generic;

public class InventorySlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("UI References")]
    public Image iconImage;
    public GameObject selectionHighlight;

    public CaughtFish CurrentFish { get; private set; }

    // Callbacks
    private Action<CaughtFish, RectTransform> onHoverEnter;
    private Action onHoverExit;
    private Action<CaughtFish> onDragOut;

    private bool isPressed = false;

    public void Populate(CaughtFish fish, Action<CaughtFish, RectTransform> onEnter, Action onExit, Action<CaughtFish> onDrag)
    {
        CurrentFish = fish;
        onHoverEnter = onEnter;
        onHoverExit = onExit;
        onDragOut = onDrag;

        // Setup Icon
        if (iconImage != null && fish.preset != null)
        {
            iconImage.gameObject.SetActive(true);
            iconImage.sprite = fish.preset.fishImage;
        }

        Deselect();

        // --- FIX 1: Wait for end of frame before checking hover ---
        // If we check immediately, the UI Raycaster might not be ready yet.
        if (gameObject.activeInHierarchy)
        {
            StartCoroutine(CheckInitialHoverRoutine());
        }
    }

    private IEnumerator CheckInitialHoverRoutine()
    {
        // Wait 1 frame so the UI layout and Raycasters are fully rebuilt
        yield return new WaitForEndOfFrame();

        if (EventSystem.current == null) yield break;

        PointerEventData pointerData = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        foreach (var result in results)
        {
            if (result.gameObject == this.gameObject || result.gameObject.transform.IsChildOf(this.transform))
            {
                // We are under the mouse! Trigger hover.
                OnPointerEnter(pointerData);
                break;
            }
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (CurrentFish != null)
        {
            onHoverEnter?.Invoke(CurrentFish, GetComponent<RectTransform>());
            Select();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        onHoverExit?.Invoke();
        Deselect();

        if (isPressed && CurrentFish != null)
        {
            onDragOut?.Invoke(CurrentFish);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isPressed = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPressed = false;
    }

    private void Select()
    {
        if (selectionHighlight != null) selectionHighlight.SetActive(true);
    }

    private void Deselect()
    {
        if (selectionHighlight != null) selectionHighlight.SetActive(false);
    }
}