using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Placeholder bait HUD: horizontal row of bait icons anchored to the bottom of the screen.
// Each slot shows the player's count for that bait. Clicking a slot with at least one in
// stock equips it; clicking the already-equipped slot unequips it. While a bait is equipped
// the displayed count is reduced by one to preview the upcoming consumption; the actual
// inventory count is only consumed when a fish bites (BobberController.HookFish).
public class BaitBarUI : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Width and height of each bait slot in pixels.")]
    public Vector2 slotSize = new Vector2(72f, 72f);
    [Tooltip("Horizontal spacing between adjacent slots in pixels.")]
    public float slotSpacing = 12f;
    [Tooltip("Pixels of padding between the row and the bottom of the screen.")]
    public float bottomPadding = 24f;

    [Header("Style")]
    public Color slotBackgroundColor = new Color(0f, 0f, 0f, 0.55f);
    public Color slotEquippedColor = new Color(0.95f, 0.78f, 0.25f, 0.85f);
    public Color slotOutOfStockColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
    public Color iconTint = Color.white;
    public Color iconOutOfStockTint = new Color(1f, 1f, 1f, 0.35f);
    public int countFontSize = 22;

    [Header("Visibility")]
    [Tooltip("If true, the bar is only visible while the fish inventory is open (I key).")]
    public bool onlyShowWhileInventoryOpen = true;

    private RectTransform rowRoot;
    private readonly List<Slot> slots = new List<Slot>();
    private bool lastVisibleState = true;
    private bool isFishingActive;

    private class Slot
    {
        public BaitItem bait;
        public Button button;
        public Image background;
        public Image icon;
        public TextMeshProUGUI countText;
    }

    private void Start()
    {
        EnsureCanvasParent();
        BuildRow();
        Rebuild();

        if (BaitInventory.Instance != null)
        {
            BaitInventory.Instance.OnBaitChanged += HandleBaitChanged;
            BaitInventory.Instance.OnSelectedBaitChanged += HandleSelectedChanged;
        }

        FishingEvents.OnThrowBobber += HandleThrow;
        FishingEvents.OnCancelFishing += HandleFishingEnded;
        FishingEvents.OnReelingCompleted += HandleFishingEnded;

        ApplyVisibility(!onlyShowWhileInventoryOpen);
    }

    private void HandleThrow(Vector3 dir, float force) => isFishingActive = true;
    private void HandleFishingEnded() => isFishingActive = false;

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

    private void OnDestroy()
    {
        FishingEvents.OnThrowBobber -= HandleThrow;
        FishingEvents.OnCancelFishing -= HandleFishingEnded;
        FishingEvents.OnReelingCompleted -= HandleFishingEnded;

        if (BaitInventory.Instance != null)
        {
            BaitInventory.Instance.OnBaitChanged -= HandleBaitChanged;
            BaitInventory.Instance.OnSelectedBaitChanged -= HandleSelectedChanged;
        }
    }

    private void HandleBaitChanged() => Rebuild();
    private void HandleSelectedChanged(BaitItem _) => RefreshVisuals();

    private void EnsureCanvasParent()
    {
        // If this script is not already under a Canvas, find or create one so the bar shows up
        // without requiring scene setup.
        Canvas parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas == null)
        {
            Canvas existing = FindFirstObjectByType<Canvas>();
            if (existing == null)
            {
                GameObject canvasObj = new GameObject("BaitBar_Canvas");
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

        // Bottom-stretched container so the row sits at the bottom regardless of screen size.
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

        if (BaitInventory.Instance == null) return;

        IReadOnlyList<BaitItem> registered = BaitInventory.Instance.RegisteredBaits;
        if (registered == null || registered.Count == 0) return;

        float totalWidth = registered.Count * slotSize.x + Mathf.Max(0, registered.Count - 1) * slotSpacing;
        float startX = -totalWidth * 0.5f + slotSize.x * 0.5f;

        for (int i = 0; i < registered.Count; i++)
        {
            BaitItem bait = registered[i];
            if (bait == null) continue;

            Slot s = CreateSlot(bait);
            float x = startX + i * (slotSize.x + slotSpacing);
            ((RectTransform)s.button.transform).anchoredPosition = new Vector2(x, 0f);
            slots.Add(s);
        }

        RefreshVisuals();
    }

    private Slot CreateSlot(BaitItem bait)
    {
        GameObject slotObj = new GameObject($"BaitSlot_{bait.name}", typeof(RectTransform));
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

        GameObject iconObj = new GameObject("Icon", typeof(RectTransform));
        RectTransform iconRect = iconObj.GetComponent<RectTransform>();
        iconRect.SetParent(rect, false);
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = new Vector2(8f, 8f);
        iconRect.offsetMax = new Vector2(-8f, -8f);
        Image icon = iconObj.AddComponent<Image>();
        icon.sprite = bait.icon;
        icon.color = iconTint;
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        GameObject countObj = new GameObject("Count", typeof(RectTransform));
        RectTransform countRect = countObj.GetComponent<RectTransform>();
        countRect.SetParent(rect, false);
        countRect.anchorMin = new Vector2(1f, 0f);
        countRect.anchorMax = new Vector2(1f, 0f);
        countRect.pivot = new Vector2(1f, 0f);
        countRect.anchoredPosition = new Vector2(-4f, 2f);
        countRect.sizeDelta = new Vector2(40f, 24f);
        TextMeshProUGUI countText = countObj.AddComponent<TextMeshProUGUI>();
        countText.fontSize = countFontSize;
        countText.alignment = TextAlignmentOptions.BottomRight;
        countText.color = Color.white;
        countText.raycastTarget = false;
        countText.text = "0";

        Slot slot = new Slot
        {
            bait = bait,
            button = btn,
            background = bg,
            icon = icon,
            countText = countText
        };
        btn.onClick.AddListener(() => HandleClick(slot));
        return slot;
    }

    private void HandleClick(Slot slot)
    {
        if (slot == null || slot.bait == null || BaitInventory.Instance == null) return;
        if (isFishingActive) return; // No swapping bait while the bobber is in the water.

        BaitInventory inv = BaitInventory.Instance;
        bool isAlreadySelected = inv.SelectedBait == slot.bait;

        if (isAlreadySelected)
        {
            // Clicking the equipped one unequips it. The count was never actually consumed,
            // so nothing else to clean up.
            inv.SetSelectedBait(null);
            return;
        }

        int actualCount = inv.GetCount(slot.bait);
        if (actualCount <= 0) return; // Can't equip what you don't have.

        inv.SetSelectedBait(slot.bait);
    }

    private void RefreshVisuals()
    {
        if (BaitInventory.Instance == null) return;

        BaitItem selected = BaitInventory.Instance.SelectedBait;
        for (int i = 0; i < slots.Count; i++)
        {
            Slot s = slots[i];
            if (s == null || s.bait == null) continue;

            int actual = BaitInventory.Instance.GetCount(s.bait);
            bool isEquipped = selected == s.bait;
            int displayed = Mathf.Max(0, actual - (isEquipped ? 1 : 0));

            s.countText.text = displayed.ToString();

            bool inStock = actual > 0;
            if (isEquipped)
            {
                s.background.color = slotEquippedColor;
                s.icon.color = iconTint;
            }
            else if (!inStock)
            {
                s.background.color = slotOutOfStockColor;
                s.icon.color = iconOutOfStockTint;
            }
            else
            {
                s.background.color = slotBackgroundColor;
                s.icon.color = iconTint;
            }
        }
    }
}
