using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Vendor shop panel. While the inventory is open and a FishVendor is active, this
// panel renders two sections:
//   - "Time of Day" — vertical list of TimeOfDayOffer rows (e.g. Night Lantern, Sun
//     Stone). Buying sets WorldStateManager.CurrentTimeMode permanently until another
//     entry is bought.
//   - "Bait"        — horizontal strip of bait offers. Buying grants a stack via FishVendor.
// A status line under the title shows which time mode is currently active.
public class BaitShopUI : MonoBehaviour
{
    [Header("Layout")]
    public Vector2 panelSize = new Vector2(500f, 460f);
    public float worldRowHeight = 56f;
    public float worldRowSpacing = 8f;
    public float baitCellWidth = 92f;
    public float baitCellHeight = 110f;
    public float baitCellSpacing = 10f;
    public Vector2 anchoredOffsetFromCenter = new Vector2(0f, 0f);

    [Header("Style")]
    public Color panelColor = new Color(0f, 0f, 0f, 0.7f);
    public Color rowColor = new Color(1f, 1f, 1f, 0.08f);
    public Color buyButtonColor = new Color(0.25f, 0.65f, 0.3f, 1f);
    public Color buyButtonDisabledColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    public Color sectionHeaderColor = new Color(1f, 0.85f, 0.4f, 1f);
    public int titleFontSize = 22;
    public int sectionFontSize = 18;
    public int rowFontSize = 16;
    public int baitCellFontSize = 14;

    private RectTransform panelRoot;
    private FishVendor activeVendor;
    private TextMeshProUGUI statusText;
    private readonly List<WorldRowUI> worldRows = new List<WorldRowUI>();
    private readonly List<BaitCellUI> baitCells = new List<BaitCellUI>();
    private bool lastVisible = true;

    private class WorldRowUI
    {
        public FishVendor.TimeOfDayOffer offer;
        public Button buyButton;
        public Image buyButtonImage;
    }

    private class BaitCellUI
    {
        public FishVendor.BaitOffer offer;
        public Button buyButton;
        public Image buyButtonImage;
    }

    private void Start()
    {
        EnsureCanvasParent();
        BuildPanel();
        SyncActiveVendor();

        if (PlayerInventory.Instance != null)
        {
            PlayerInventory.Instance.OnInventoryChanged += RefreshAffordability;
        }
        FishVendor.OnVendorInventoryChanged += RefreshAffordability;
        FishVendor.OnCurrentShoppingVendorChanged += SyncActiveVendor;
        WorldStateManager.OnWorldStateChanged += RefreshStatusLine;

        ApplyVisibility(false);
    }

    private void OnDestroy()
    {
        if (PlayerInventory.Instance != null)
        {
            PlayerInventory.Instance.OnInventoryChanged -= RefreshAffordability;
        }
        FishVendor.OnVendorInventoryChanged -= RefreshAffordability;
        FishVendor.OnCurrentShoppingVendorChanged -= SyncActiveVendor;
        WorldStateManager.OnWorldStateChanged -= RefreshStatusLine;
    }

    private void Update()
    {
        bool hasContent = activeVendor != null &&
                         ((worldRows.Count > 0) || (baitCells.Count > 0));
        bool shouldShow = InventoryUI.IsInventoryOpen && hasContent;
        if (shouldShow != lastVisible) ApplyVisibility(shouldShow);
        if (lastVisible) RefreshStatusLine();
    }

    private void SyncActiveVendor()
    {
        FishVendor next = FishVendor.CurrentShoppingVendor;
        if (next == activeVendor) return;
        activeVendor = next;
        Rebuild();
    }

    private void ApplyVisibility(bool visible)
    {
        lastVisible = visible;
        if (panelRoot != null) panelRoot.gameObject.SetActive(visible);
        if (visible)
        {
            RefreshAffordability();
            RefreshStatusLine();
        }
    }

    private void EnsureCanvasParent()
    {
        Canvas parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas == null)
        {
            Canvas existing = FindFirstObjectByType<Canvas>();
            if (existing == null)
            {
                GameObject canvasObj = new GameObject("BaitShop_Canvas");
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

    private void BuildPanel()
    {
        RectTransform self = GetComponent<RectTransform>();
        if (self == null) self = gameObject.AddComponent<RectTransform>();
        self.anchorMin = new Vector2(0.5f, 0.5f);
        self.anchorMax = new Vector2(0.5f, 0.5f);
        self.pivot = new Vector2(0.5f, 0.5f);
        self.anchoredPosition = anchoredOffsetFromCenter;
        self.sizeDelta = panelSize;

        GameObject panelObj = new GameObject("Panel", typeof(RectTransform));
        panelRoot = panelObj.GetComponent<RectTransform>();
        panelRoot.SetParent(self, false);
        panelRoot.anchorMin = Vector2.zero;
        panelRoot.anchorMax = Vector2.one;
        panelRoot.offsetMin = Vector2.zero;
        panelRoot.offsetMax = Vector2.zero;

        Image bg = panelObj.AddComponent<Image>();
        bg.color = panelColor;

        CreateTitle();
        CreateStatusLine();
    }

    private void CreateTitle()
    {
        GameObject titleObj = new GameObject("Title", typeof(RectTransform));
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.SetParent(panelRoot, false);
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -8f);
        titleRect.sizeDelta = new Vector2(0f, 32f);
        TextMeshProUGUI titleText = titleObj.AddComponent<TextMeshProUGUI>();
        titleText.text = "Shop";
        titleText.fontSize = titleFontSize;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = Color.white;
        titleText.raycastTarget = false;
    }

    private void CreateStatusLine()
    {
        GameObject statusObj = new GameObject("Status", typeof(RectTransform));
        RectTransform statusRect = statusObj.GetComponent<RectTransform>();
        statusRect.SetParent(panelRoot, false);
        statusRect.anchorMin = new Vector2(0f, 1f);
        statusRect.anchorMax = new Vector2(1f, 1f);
        statusRect.pivot = new Vector2(0.5f, 1f);
        statusRect.anchoredPosition = new Vector2(0f, -44f);
        statusRect.sizeDelta = new Vector2(0f, 20f);
        statusText = statusObj.AddComponent<TextMeshProUGUI>();
        statusText.text = "";
        statusText.fontSize = 14;
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.color = new Color(0.8f, 0.9f, 1f, 0.9f);
        statusText.raycastTarget = false;
    }

    private void Rebuild()
    {
        // Wipe everything except Title and Status.
        for (int i = panelRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = panelRoot.GetChild(i);
            if (child.name == "Title" || child.name == "Status") continue;
            Destroy(child.gameObject);
        }
        worldRows.Clear();
        baitCells.Clear();

        if (activeVendor == null) return;

        // ---- Time-of-Day section ----
        float cursorY = -72f;
        int timeCount = activeVendor.timeOfDayOffers != null ? activeVendor.timeOfDayOffers.Count : 0;
        if (timeCount > 0)
        {
            CreateSectionHeader("Time of Day", cursorY);
            cursorY -= 28f;

            for (int i = 0; i < timeCount; i++)
            {
                FishVendor.TimeOfDayOffer offer = activeVendor.timeOfDayOffers[i];
                if (offer == null) continue;
                WorldRowUI row = CreateWorldRow(offer, cursorY);
                worldRows.Add(row);
                cursorY -= (worldRowHeight + worldRowSpacing);
            }
            cursorY -= 8f;
        }

        // ---- Bait section ----
        int baitCount = activeVendor.baitOffers != null ? activeVendor.baitOffers.Count : 0;
        if (baitCount > 0)
        {
            CreateSectionHeader("Bait", cursorY);
            cursorY -= 28f;
            CreateBaitStrip(cursorY);
        }

        RefreshAffordability();
        RefreshStatusLine();
    }

    private void CreateSectionHeader(string text, float y)
    {
        GameObject obj = new GameObject($"Header_{text}", typeof(RectTransform));
        RectTransform r = obj.GetComponent<RectTransform>();
        r.SetParent(panelRoot, false);
        r.anchorMin = new Vector2(0f, 1f);
        r.anchorMax = new Vector2(1f, 1f);
        r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = new Vector2(16f, y);
        r.sizeDelta = new Vector2(-32f, 24f);
        TextMeshProUGUI t = obj.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = sectionFontSize;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.color = sectionHeaderColor;
        t.raycastTarget = false;
    }

    private WorldRowUI CreateWorldRow(FishVendor.TimeOfDayOffer offer, float y)
    {
        GameObject rowObj = new GameObject($"World_{offer.displayName}", typeof(RectTransform));
        RectTransform rowRect = rowObj.GetComponent<RectTransform>();
        rowRect.SetParent(panelRoot, false);
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = new Vector2(0f, y);
        rowRect.sizeDelta = new Vector2(-24f, worldRowHeight);

        Image rowBg = rowObj.AddComponent<Image>();
        rowBg.color = rowColor;

        // Icon
        GameObject iconObj = new GameObject("Icon", typeof(RectTransform));
        RectTransform iconRect = iconObj.GetComponent<RectTransform>();
        iconRect.SetParent(rowRect, false);
        iconRect.anchorMin = new Vector2(0f, 0.5f);
        iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(8f, 0f);
        iconRect.sizeDelta = new Vector2(worldRowHeight - 12f, worldRowHeight - 12f);
        Image icon = iconObj.AddComponent<Image>();
        icon.sprite = offer.icon;
        icon.color = offer.icon != null ? Color.white : new Color(1f, 1f, 1f, 0.15f);
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        // Label
        GameObject labelObj = new GameObject("Label", typeof(RectTransform));
        RectTransform labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.SetParent(rowRect, false);
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(worldRowHeight, 0f);
        labelRect.offsetMax = new Vector2(-128f, 0f);
        TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
        string effectDesc = DescribeOffer(offer);
        label.text = $"{offer.displayName}\n<size=12><color=#BBBBBB>{effectDesc}</color></size>  <size=12><color=#FFD96A>{offer.price} coins</color></size>";
        label.fontSize = rowFontSize;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = Color.white;
        label.raycastTarget = false;
        label.richText = true;

        // Buy button
        GameObject buyObj = new GameObject("BuyButton", typeof(RectTransform));
        RectTransform buyRect = buyObj.GetComponent<RectTransform>();
        buyRect.SetParent(rowRect, false);
        buyRect.anchorMin = new Vector2(1f, 0.5f);
        buyRect.anchorMax = new Vector2(1f, 0.5f);
        buyRect.pivot = new Vector2(1f, 0.5f);
        buyRect.anchoredPosition = new Vector2(-8f, 0f);
        buyRect.sizeDelta = new Vector2(110f, worldRowHeight - 16f);
        Image buyImage = buyObj.AddComponent<Image>();
        buyImage.color = buyButtonColor;
        Button buyButton = buyObj.AddComponent<Button>();
        buyButton.targetGraphic = buyImage;

        GameObject buyLabelObj = new GameObject("BuyLabel", typeof(RectTransform));
        RectTransform buyLabelRect = buyLabelObj.GetComponent<RectTransform>();
        buyLabelRect.SetParent(buyRect, false);
        buyLabelRect.anchorMin = Vector2.zero;
        buyLabelRect.anchorMax = Vector2.one;
        buyLabelRect.offsetMin = Vector2.zero;
        buyLabelRect.offsetMax = Vector2.zero;
        TextMeshProUGUI buyLabel = buyLabelObj.AddComponent<TextMeshProUGUI>();
        buyLabel.text = "Buy";
        buyLabel.fontSize = rowFontSize;
        buyLabel.alignment = TextAlignmentOptions.Center;
        buyLabel.color = Color.white;
        buyLabel.raycastTarget = false;

        WorldRowUI row = new WorldRowUI
        {
            offer = offer,
            buyButton = buyButton,
            buyButtonImage = buyImage,
        };
        buyButton.onClick.AddListener(() => HandleBuyTimeOfDay(row));
        return row;
    }

    private string DescribeOffer(FishVendor.TimeOfDayOffer offer)
    {
        switch (offer.mode)
        {
            case WorldStateManager.TimeMode.ForcedDay:   return "Permanent Day";
            case WorldStateManager.TimeMode.ForcedNight: return "Permanent Night";
            default:                                     return "Auto (real time)";
        }
    }

    private void CreateBaitStrip(float topY)
    {
        // Container row anchored to top-left, height = baitCellHeight.
        GameObject stripObj = new GameObject("BaitStrip", typeof(RectTransform));
        RectTransform stripRect = stripObj.GetComponent<RectTransform>();
        stripRect.SetParent(panelRoot, false);
        stripRect.anchorMin = new Vector2(0f, 1f);
        stripRect.anchorMax = new Vector2(1f, 1f);
        stripRect.pivot = new Vector2(0f, 1f);
        stripRect.anchoredPosition = new Vector2(16f, topY);
        stripRect.sizeDelta = new Vector2(-32f, baitCellHeight);

        int count = activeVendor.baitOffers.Count;
        for (int i = 0; i < count; i++)
        {
            FishVendor.BaitOffer offer = activeVendor.baitOffers[i];
            if (offer == null || offer.bait == null) continue;
            BaitCellUI cell = CreateBaitCell(stripRect, offer, activeVendor.baitStackSize, i);
            baitCells.Add(cell);
        }
    }

    private BaitCellUI CreateBaitCell(RectTransform parent, FishVendor.BaitOffer offer, int stackSize, int index)
    {
        GameObject cellObj = new GameObject($"BaitCell_{offer.bait.name}", typeof(RectTransform));
        RectTransform cellRect = cellObj.GetComponent<RectTransform>();
        cellRect.SetParent(parent, false);
        cellRect.anchorMin = new Vector2(0f, 1f);
        cellRect.anchorMax = new Vector2(0f, 1f);
        cellRect.pivot = new Vector2(0f, 1f);
        cellRect.anchoredPosition = new Vector2(index * (baitCellWidth + baitCellSpacing), 0f);
        cellRect.sizeDelta = new Vector2(baitCellWidth, baitCellHeight);

        Image cellBg = cellObj.AddComponent<Image>();
        cellBg.color = rowColor;

        // Icon (top)
        GameObject iconObj = new GameObject("Icon", typeof(RectTransform));
        RectTransform iconRect = iconObj.GetComponent<RectTransform>();
        iconRect.SetParent(cellRect, false);
        iconRect.anchorMin = new Vector2(0.5f, 1f);
        iconRect.anchorMax = new Vector2(0.5f, 1f);
        iconRect.pivot = new Vector2(0.5f, 1f);
        iconRect.anchoredPosition = new Vector2(0f, -6f);
        iconRect.sizeDelta = new Vector2(44f, 44f);
        Image icon = iconObj.AddComponent<Image>();
        icon.sprite = offer.bait.icon;
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        // Label (middle)
        GameObject labelObj = new GameObject("Label", typeof(RectTransform));
        RectTransform labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.SetParent(cellRect, false);
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(4f, 28f);
        labelRect.offsetMax = new Vector2(-4f, -54f);
        TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
        string baitName = string.IsNullOrEmpty(offer.bait.displayName) ? offer.bait.name : offer.bait.displayName;
        label.text = $"{baitName} ×{stackSize}\n<color=#FFD96A>{offer.pricePerStack}c</color>";
        label.fontSize = baitCellFontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        label.richText = true;

        // Buy button (bottom)
        GameObject buyObj = new GameObject("BuyButton", typeof(RectTransform));
        RectTransform buyRect = buyObj.GetComponent<RectTransform>();
        buyRect.SetParent(cellRect, false);
        buyRect.anchorMin = new Vector2(0f, 0f);
        buyRect.anchorMax = new Vector2(1f, 0f);
        buyRect.pivot = new Vector2(0.5f, 0f);
        buyRect.anchoredPosition = new Vector2(0f, 4f);
        buyRect.sizeDelta = new Vector2(-8f, 24f);
        Image buyImage = buyObj.AddComponent<Image>();
        buyImage.color = buyButtonColor;
        Button buyButton = buyObj.AddComponent<Button>();
        buyButton.targetGraphic = buyImage;

        GameObject buyLabelObj = new GameObject("BuyLabel", typeof(RectTransform));
        RectTransform buyLabelRect = buyLabelObj.GetComponent<RectTransform>();
        buyLabelRect.SetParent(buyRect, false);
        buyLabelRect.anchorMin = Vector2.zero;
        buyLabelRect.anchorMax = Vector2.one;
        buyLabelRect.offsetMin = Vector2.zero;
        buyLabelRect.offsetMax = Vector2.zero;
        TextMeshProUGUI buyLabel = buyLabelObj.AddComponent<TextMeshProUGUI>();
        buyLabel.text = "Buy";
        buyLabel.fontSize = baitCellFontSize;
        buyLabel.alignment = TextAlignmentOptions.Center;
        buyLabel.color = Color.white;
        buyLabel.raycastTarget = false;

        BaitCellUI cell = new BaitCellUI
        {
            offer = offer,
            buyButton = buyButton,
            buyButtonImage = buyImage,
        };
        buyButton.onClick.AddListener(() => HandleBuyBait(cell));
        return cell;
    }

    private void HandleBuyBait(BaitCellUI cell)
    {
        if (cell == null || cell.offer == null || activeVendor == null) return;
        activeVendor.TryBuyBait(cell.offer);
    }

    private void HandleBuyTimeOfDay(WorldRowUI row)
    {
        if (row == null || row.offer == null || activeVendor == null) return;
        activeVendor.TryBuyTimeOfDay(row.offer);
    }

    private void RefreshAffordability()
    {
        if (PlayerInventory.Instance == null) return;
        int coins = PlayerInventory.Instance.currentCurrency;

        for (int i = 0; i < worldRows.Count; i++)
        {
            WorldRowUI row = worldRows[i];
            bool canAfford = row.offer != null && coins >= row.offer.price;
            row.buyButton.interactable = canAfford;
            row.buyButtonImage.color = canAfford ? buyButtonColor : buyButtonDisabledColor;
        }
        for (int i = 0; i < baitCells.Count; i++)
        {
            BaitCellUI cell = baitCells[i];
            bool canAfford = cell.offer != null && coins >= cell.offer.pricePerStack;
            cell.buyButton.interactable = canAfford;
            cell.buyButtonImage.color = canAfford ? buyButtonColor : buyButtonDisabledColor;
        }
    }

    private void RefreshStatusLine()
    {
        if (statusText == null) return;
        WorldStateManager world = WorldStateManager.Instance;
        if (world == null)
        {
            statusText.text = "";
            return;
        }

        switch (world.CurrentTimeMode)
        {
            case WorldStateManager.TimeMode.ForcedDay:
                statusText.text = "Day (permanent)";
                break;
            case WorldStateManager.TimeMode.ForcedNight:
                statusText.text = "Night (permanent)";
                break;
            default:
                statusText.text = "";
                break;
        }
    }
}
