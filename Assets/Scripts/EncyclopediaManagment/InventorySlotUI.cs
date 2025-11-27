using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using TMPro;

public class InventorySlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("UI References")]
    public Image iconImage;
    public GameObject selectionHighlight;

    public CaughtFish CurrentFish { get; private set; }

    // Callbacks
    private Action<CaughtFish, RectTransform> onHoverEnter;
    private Action onHoverExit;
    private Action<CaughtFish> onDragOut; // New callback for pulling the fish out

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
        // 1. Always hide tooltip when leaving
        onHoverExit?.Invoke();
        Deselect();

        // 2. Logic: If we are holding the mouse down AND we leave the button area...
        if (isPressed && CurrentFish != null)
        {
            // ...Trigger the "Pull Out" 3D model event
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