using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Placeholder bait shop panel. While the inventory is open, this panel lists every
// BaitOffer configured on the first FishVendor in the scene. Each row shows the bait
// icon, name × stackSize, the price per stack, and a Buy button. Buying calls into
// FishVendor.TryBuyBait which deducts the player's currency and grants the bait stack.
public class BaitShopUI : MonoBehaviour
{
    [Header("Layout")]
    public Vector2 panelSize = new Vector2(420f, 320f);
    public float rowHeight = 56f;
    public float rowSpacing = 8f;
    public Vector2 anchoredOffsetFromCenter = new Vector2(0f, 0f);

    [Header("Style")]
    public Color panelColor = new Color(0f, 0f, 0f, 0.7f);
    public Color rowColor = new Color(1f, 1f, 1f, 0.08f);
    public Color buyButtonColor = new Color(0.25f, 0.65f, 0.3f, 1f);
    public Color buyButtonDisabledColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    public int titleFontSize = 22;
    public int rowFontSize = 18;

    private RectTransform panelRoot;
    private FishVendor activeVendor;
    private readonly List<RowUI> rows = new List<RowUI>();
    private bool lastVisible = true;

    private class RowUI
    {
        public FishVendor.BaitOffer offer;
        public Button buyButton;
        public Image buyButtonImage;
        public TextMeshProUGUI buyLabel;
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
    }

    private void Update()
    {
        bool shouldShow = InventoryUI.IsInventoryOpen && activeVendor != null && rows.Count > 0;
        if (shouldShow != lastVisible) ApplyVisibility(shouldShow);
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
        if (visible) RefreshAffordability();
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

        GameObject titleObj = new GameObject("Title", typeof(RectTransform));
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.SetParent(panelRoot, false);
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -8f);
        titleRect.sizeDelta = new Vector2(0f, 32f);
        TextMeshProUGUI titleText = titleObj.AddComponent<TextMeshProUGUI>();
        titleText.text = "Bait Shop";
        titleText.fontSize = titleFontSize;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = Color.white;
        titleText.raycastTarget = false;
    }

    private void Rebuild()
    {
        // Wipe existing rows.
        for (int i = panelRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = panelRoot.GetChild(i);
            if (child.name == "Title") continue;
            Destroy(child.gameObject);
        }
        rows.Clear();

        if (activeVendor == null || activeVendor.baitOffers == null) return;

        float startY = -48f; // below the title
        for (int i = 0; i < activeVendor.baitOffers.Count; i++)
        {
            FishVendor.BaitOffer offer = activeVendor.baitOffers[i];
            if (offer == null || offer.bait == null) continue;

            RowUI row = CreateRow(offer, activeVendor.baitStackSize);
            float y = startY - i * (rowHeight + rowSpacing);
            ((RectTransform)row.buyButton.transform.parent).anchoredPosition = new Vector2(0f, y);
            rows.Add(row);
        }

        RefreshAffordability();
    }

    private RowUI CreateRow(FishVendor.BaitOffer offer, int stackSize)
    {
        GameObject rowObj = new GameObject($"Row_{offer.bait.name}", typeof(RectTransform));
        RectTransform rowRect = rowObj.GetComponent<RectTransform>();
        rowRect.SetParent(panelRoot, false);
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.sizeDelta = new Vector2(-24f, rowHeight);

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
        iconRect.sizeDelta = new Vector2(rowHeight - 12f, rowHeight - 12f);
        Image icon = iconObj.AddComponent<Image>();
        icon.sprite = offer.bait.icon;
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        // Label
        GameObject labelObj = new GameObject("Label", typeof(RectTransform));
        RectTransform labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.SetParent(rowRect, false);
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(rowHeight, 0f);
        labelRect.offsetMax = new Vector2(-128f, 0f);
        TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
        string baitName = string.IsNullOrEmpty(offer.bait.displayName) ? offer.bait.name : offer.bait.displayName;
        label.text = $"{baitName} ×{stackSize}\n<size=14><color=#FFD96A>{offer.pricePerStack} coins</color></size>";
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
        buyRect.sizeDelta = new Vector2(110f, rowHeight - 16f);
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

        RowUI row = new RowUI
        {
            offer = offer,
            buyButton = buyButton,
            buyButtonImage = buyImage,
            buyLabel = buyLabel
        };
        buyButton.onClick.AddListener(() => HandleBuy(row));
        return row;
    }

    private void HandleBuy(RowUI row)
    {
        if (row == null || row.offer == null || activeVendor == null) return;
        activeVendor.TryBuyBait(row.offer);
    }

    private void RefreshAffordability()
    {
        if (PlayerInventory.Instance == null) return;
        int coins = PlayerInventory.Instance.currentCurrency;

        for (int i = 0; i < rows.Count; i++)
        {
            RowUI row = rows[i];
            bool canAfford = row.offer != null && coins >= row.offer.pricePerStack;
            row.buyButton.interactable = canAfford;
            row.buyButtonImage.color = canAfford ? buyButtonColor : buyButtonDisabledColor;
        }
    }
}
