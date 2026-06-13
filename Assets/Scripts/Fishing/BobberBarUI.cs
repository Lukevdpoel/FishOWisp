using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Row of bobber/lure icons that appears alongside the bait bar while the inventory is open.
// Clicking a slot equips that bobber via BobberInventory, which in turn updates the rod's
// FishingLine. Swapping is blocked while the bobber is in the water.
public class BobberBarUI : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Wider than the bait bar slots because labels (e.g. 'Basic Bobber') need room.")]
    public Vector2 slotSize = new Vector2(140f, 48f);
    public float slotSpacing = 12f;
    [Tooltip("Pixels of padding above the bait bar. The bait bar sits at bottomPadding=24 with " +
             "slotSize.y=72, so a default of 110 stacks this row cleanly above it.")]
    public float bottomPadding = 110f;

    [Header("Style")]
    public Color slotBackgroundColor = new Color(0f, 0f, 0f, 0.85f);
    public Color slotEquippedColor = new Color(0.45f, 0.78f, 0.95f, 0.95f);
    public Color labelColor = Color.white;
    public int labelFontSize = 20;

    [Header("Visibility")]
    [Tooltip("If true, the bar is only visible while the fish inventory is open (B key).")]
    public bool onlyShowWhileInventoryOpen = true;

    private RectTransform rowRoot;
    private readonly List<Slot> slots = new List<Slot>();
    private bool lastVisibleState = true;
    private bool isFishingActive;

    // --- Gamepad focus (driven by InventoryUI's D-pad navigation) ---
    // How strongly the focused slot's background is pushed toward white.
    private const float GamepadFocusTint = 0.45f;
    private int gamepadFocusIndex = -1;

    public int SlotCount => slots.Count;

    /// <summary>Give this bar the D-pad focus. Lands on the equipped bobber/lure if any.</summary>
    public void FocusGamepad()
    {
        if (slots.Count == 0) return;
        if (gamepadFocusIndex < 0 || gamepadFocusIndex >= slots.Count)
        {
            int equipped = IndexOfSelected();
            gamepadFocusIndex = equipped >= 0 ? equipped : 0;
        }
        RefreshVisuals();
    }

    public void MoveGamepadFocus(int delta)
    {
        if (slots.Count == 0) return;
        if (gamepadFocusIndex < 0) gamepadFocusIndex = delta > 0 ? 0 : slots.Count - 1;
        else gamepadFocusIndex = ((gamepadFocusIndex + delta) % slots.Count + slots.Count) % slots.Count;
        RefreshVisuals();
    }

    public void ClearGamepadFocus()
    {
        if (gamepadFocusIndex == -1) return;
        gamepadFocusIndex = -1;
        RefreshVisuals();
    }

    /// <summary>A-button press: same path as clicking the focused slot.</summary>
    public void ActivateGamepadFocus()
    {
        if (gamepadFocusIndex < 0 || gamepadFocusIndex >= slots.Count) return;
        HandleClick(slots[gamepadFocusIndex]);
    }

    private int IndexOfSelected()
    {
        if (BobberInventory.Instance == null) return -1;
        BobberItem selected = BobberInventory.Instance.SelectedBobber;
        if (selected == null) return -1;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] != null && slots[i].bobber == selected) return i;
        }
        return -1;
    }

    private class Slot
    {
        public BobberItem bobber;
        public Button button;
        public Image background;
        public TextMeshProUGUI label;
    }

    private void Start()
    {
        EnsureCanvasParent();
        BuildRow();
        Rebuild();

        if (BobberInventory.Instance != null)
        {
            BobberInventory.Instance.OnOwnedBobbersChanged += Rebuild;
            BobberInventory.Instance.OnSelectedBobberChanged += HandleSelectedChanged;
        }

        FishingEvents.OnThrowBobber += HandleThrow;
        FishingEvents.OnCancelFishing += HandleFishingEnded;
        FishingEvents.OnReelingCompleted += HandleFishingEnded;

        ApplyVisibility(!onlyShowWhileInventoryOpen);
    }

    private void OnDestroy()
    {
        FishingEvents.OnThrowBobber -= HandleThrow;
        FishingEvents.OnCancelFishing -= HandleFishingEnded;
        FishingEvents.OnReelingCompleted -= HandleFishingEnded;

        if (BobberInventory.Instance != null)
        {
            BobberInventory.Instance.OnOwnedBobbersChanged -= Rebuild;
            BobberInventory.Instance.OnSelectedBobberChanged -= HandleSelectedChanged;
        }
    }

    private void HandleThrow(Vector3 dir, float force) => isFishingActive = true;
    private void HandleFishingEnded() => isFishingActive = false;
    private void HandleSelectedChanged(BobberItem _) => RefreshVisuals();

    private void Update()
    {
        if (!onlyShowWhileInventoryOpen)
        {
            if (!lastVisibleState) ApplyVisibility(true);
            return;
        }

        bool shouldShow = InventoryUI.IsInventoryOpen;
        if (shouldShow != lastVisibleState)
        {
            ApplyVisibility(shouldShow);
        }
    }

    private void ApplyVisibility(bool visible)
    {
        lastVisibleState = visible;
        if (rowRoot != null) rowRoot.gameObject.SetActive(visible);
    }

    private void EnsureCanvasParent()
    {
        Canvas parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas == null)
        {
            Canvas existing = FindFirstObjectByType<Canvas>();
            if (existing == null)
            {
                GameObject canvasObj = new GameObject("BobberBar_Canvas");
                Canvas c = canvasObj.AddComponent<Canvas>();
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasObj.AddComponent<CanvasScaler>();
                canvasObj.AddComponent<GraphicRaycaster>();
                existing = c;
            }
            transform.SetParent(existing.transform, false);
        }

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    private void BuildRow()
    {
        RectTransform self = GetComponent<RectTransform>();
        if (self == null) self = gameObject.AddComponent<RectTransform>();

        self.anchorMin = new Vector2(0f, 0f);
        self.anchorMax = new Vector2(1f, 0f);
        self.pivot = new Vector2(0.5f, 0f);
        self.anchoredPosition = new Vector2(0f, bottomPadding);
        self.sizeDelta = new Vector2(0f, slotSize.y);

        GameObject rowObj = new GameObject("Row", typeof(RectTransform));
        rowRoot = rowObj.GetComponent<RectTransform>();
        rowRoot.SetParent(self, false);
        rowRoot.anchorMin = new Vector2(0.5f, 0.5f);
        rowRoot.anchorMax = new Vector2(0.5f, 0.5f);
        rowRoot.pivot = new Vector2(0.5f, 0.5f);
        rowRoot.anchoredPosition = Vector2.zero;
        rowRoot.sizeDelta = new Vector2(0f, slotSize.y);
    }

    private void Rebuild()
    {
        if (rowRoot == null) return;

        for (int i = rowRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(rowRoot.GetChild(i).gameObject);
        }
        slots.Clear();

        if (BobberInventory.Instance == null) return;

        IReadOnlyList<BobberItem> owned = BobberInventory.Instance.OwnedBobbers;
        if (owned == null || owned.Count == 0) return;

        float totalWidth = owned.Count * slotSize.x + Mathf.Max(0, owned.Count - 1) * slotSpacing;
        float startX = -totalWidth * 0.5f + slotSize.x * 0.5f;

        for (int i = 0; i < owned.Count; i++)
        {
            BobberItem bobber = owned[i];
            if (bobber == null) continue;

            Slot s = CreateSlot(bobber);
            float x = startX + i * (slotSize.x + slotSpacing);
            ((RectTransform)s.button.transform).anchoredPosition = new Vector2(x, 0f);
            slots.Add(s);
        }

        RefreshVisuals();
    }

    private Slot CreateSlot(BobberItem bobber)
    {
        GameObject slotObj = new GameObject($"BobberSlot_{bobber.name}", typeof(RectTransform));
        RectTransform rect = slotObj.GetComponent<RectTransform>();
        rect.SetParent(rowRoot, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = slotSize;

        Image bg = slotObj.AddComponent<Image>();
        bg.color = slotBackgroundColor;

        Button btn = slotObj.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
        btn.colors = cb;
        btn.targetGraphic = bg;

        GameObject labelObj = new GameObject("Label", typeof(RectTransform));
        RectTransform labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 4f);
        labelRect.offsetMax = new Vector2(-8f, -4f);
        TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
        label.text = string.IsNullOrEmpty(bobber.displayName) ? bobber.name : bobber.displayName;
        label.color = labelColor;
        label.fontSize = labelFontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = 10;
        label.fontSizeMax = labelFontSize;
        label.raycastTarget = false;

        ItemHoverTooltip.Attach(slotObj,
            string.IsNullOrEmpty(bobber.displayName) ? bobber.name : bobber.displayName,
            bobber.description);

        Slot slot = new Slot
        {
            bobber = bobber,
            button = btn,
            background = bg,
            label = label,
        };
        btn.onClick.AddListener(() => HandleClick(slot));
        return slot;
    }

    private void HandleClick(Slot slot)
    {
        if (slot == null || slot.bobber == null || BobberInventory.Instance == null) return;
        if (isFishingActive) return;

        BobberInventory inv = BobberInventory.Instance;
        if (inv.SelectedBobber == slot.bobber) return;

        inv.SetSelectedBobber(slot.bobber);
    }

    private void RefreshVisuals()
    {
        if (BobberInventory.Instance == null) return;

        BobberItem selected = BobberInventory.Instance.SelectedBobber;
        for (int i = 0; i < slots.Count; i++)
        {
            Slot s = slots[i];
            if (s == null || s.bobber == null) continue;

            bool isEquipped = selected == s.bobber;
            s.background.color = isEquipped ? slotEquippedColor : slotBackgroundColor;
            s.label.color = labelColor;

            if (i == gamepadFocusIndex)
            {
                s.background.color = Color.Lerp(s.background.color, Color.white, GamepadFocusTint);
            }
        }
    }
}
