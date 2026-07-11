using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// On-screen UI for the 3D shop, driven by ShopController. Three states:
//   Conversation - buttons: Fishing Supplies / Sell Fish / Aquarium Supplies.
//   Browsing     - a fixed info card (name, price / SOLD OUT / can't afford, description) for the
//                  highlighted wall item.
//   Selling      - a list of caught fish + an "are you sure?" confirm dialog.
//
// The UI is authored by you in the editor: build your own Canvas / panels / text / backgrounds and
// wire the slots below. This script only DRIVES those objects — it never creates any UI. Any slot
// left unwired simply does nothing (that piece won't appear).
public class ShopMenuUI : MonoBehaviour
{
    [Header("Highlight colours")]
    [Tooltip("Tint applied to the currently-selected conversation button / sell row (gamepad nav).")]
    public Color highlightColor = new Color(0.4f, 0.7f, 0.95f, 1f);
    [Tooltip("Tint applied to non-selected conversation buttons. Set to white to leave your art untinted.")]
    public Color buttonColor = new Color(0.25f, 0.5f, 0.7f, 1f);
    [Tooltip("Tint applied to non-selected sell rows. Set to white/clear to leave your art untinted.")]
    public Color rowColor = new Color(1f, 1f, 1f, 0.08f);
    [Tooltip("Price colour when the player can afford the item.")]
    public Color goldColor = new Color(1f, 0.85f, 0.4f, 1f);
    [Tooltip("Price colour for SOLD OUT / can't-afford.")]
    public Color soldOutColor = new Color(1f, 0.45f, 0.4f, 1f);

    // Only one ShopMenuUI may drive the shop — a second instance (e.g. a leftover copy) is disabled
    // in Awake so we never get two fighting over the controller.
    private static ShopMenuUI active;

    // ---- Authored layout (wire these to your own UI) ------------------------
    [Header("Conversation screen")]
    [Tooltip("Root of the vendor conversation screen (the Fishing/Sell/Aquarium buttons).")]
    [SerializeField] private RectTransform conversationRoot;
    [Tooltip("The 'Fishing Supplies' button. Give it an Image target graphic for the gamepad highlight.")]
    [SerializeField] private Button fishingSuppliesButton;
    [Tooltip("The 'Sell Fish' button. Give it an Image target graphic for the gamepad highlight.")]
    [SerializeField] private Button sellFishButton;

    [Header("Browsing info card")]
    [Tooltip("Root object of the browsing screen (parents the info card).")]
    [SerializeField] private RectTransform browsingRoot;
    [Tooltip("Root of the info card (your background image lives here). Shown only while an item is " +
             "highlighted.")]
    [SerializeField] private RectTransform cardRoot;
    [Tooltip("TMP label for the highlighted item's name (the title).")]
    [SerializeField] private TextMeshProUGUI cardName;
    [Tooltip("TMP label for the price / SOLD OUT line.")]
    [SerializeField] private TextMeshProUGUI cardPrice;
    [Tooltip("TMP label for the item description.")]
    [SerializeField] private TextMeshProUGUI cardDescription;

    [Header("Sell screen")]
    [Tooltip("Root of the sell screen.")]
    [SerializeField] private RectTransform sellRoot;
    [Tooltip("Container the fish rows are spawned into. Add a VerticalLayoutGroup to it so rows stack " +
             "automatically.")]
    [SerializeField] private RectTransform sellListRoot;
    [Tooltip("A row template cloned once per caught fish: a Button with an Image (its background/" +
             "highlight) and a child TMP label. Use a PREFAB ASSET, or a template kept OUTSIDE the " +
             "list container (the list is cleared on rebuild).")]
    [SerializeField] private GameObject sellRowPrefab;
    [Tooltip("TMP 'No fish to sell.' label, shown when the list is empty.")]
    [SerializeField] private TextMeshProUGUI sellEmptyLabel;
    [Tooltip("Root of the 'are you sure?' confirm dialog (your background lives here).")]
    [SerializeField] private RectTransform confirmRoot;
    [Tooltip("TMP label inside the confirm dialog.")]
    [SerializeField] private TextMeshProUGUI confirmText;
    [Tooltip("The confirm dialog's 'Yes' button.")]
    [SerializeField] private Button confirmYesButton;
    [Tooltip("The confirm dialog's 'No' button.")]
    [SerializeField] private Button confirmNoButton;

    [Header("Buy-result toast")]
    [Tooltip("Root of the transient buy-result toast ('Bought X' / 'Not enough coins'). Leave it " +
             "inactive by default.")]
    [SerializeField] private GameObject toastRoot;
    [Tooltip("TMP label inside the toast.")]
    [SerializeField] private TextMeshProUGUI toastLabel;

    // Conversation button backgrounds, indexed to match ShopController.ConversationIndex
    // (0 = Fishing Supplies, 1 = Sell Fish), so the selected one can be brightened for gamepad.
    private readonly Image[] conversationButtons = new Image[2];

    // The per-row highlight graphic, index-aligned with PlayerInventory.caughtFishes.
    private readonly List<Image> sellRowImages = new List<Image>();
    private int lastSellCount = -1;

    private float toastHideTime;

    private ShopController controller;
    private ShopItem lastHighlighted;
    private int lastCurrency = int.MinValue;
    private bool lastSoldOut;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => active = null;

    private void Awake()
    {
        if (active != null && active != this)
        {
            Debug.LogWarning($"[ShopMenuUI] A second ShopMenuUI ('{gameObject.name}') was found and " +
                $"disabled — keep only one (under PlayerMaster). Active one: '{active.gameObject.name}'.", active);
            // Park this copy's authored panels: once disabled, Start/Update never run, so panels
            // saved active in the editor would otherwise stay on screen forever.
            SetActiveRoot(null);
            enabled = false;
            return;
        }
        active = this;
    }

    private void OnDestroy()
    {
        if (active == this) active = null;
        if (controller != null) controller.OnPurchaseFeedback -= ShowToast;
        GamepadInput.OnActiveDeviceChanged -= HandleDeviceChanged;
        SteamInputGlyphs.OnGlyphsChanged -= InvalidateCard;
    }

    private void ShowToast(string message, bool success)
    {
        if (toastRoot == null || toastLabel == null) return;
        toastLabel.text = message;
        toastLabel.color = success ? new Color(0.55f, 1f, 0.6f) : new Color(1f, 0.5f, 0.45f);
        toastRoot.SetActive(true);
        toastHideTime = Time.unscaledTime + 1.6f;
    }

    private void Start()
    {
        WireButtons();
        SetActiveRoot(null);

        // The card caches by highlighted item, so a device swap / new Steam glyph set won't re-resolve
        // its {verb} tokens on its own — force the next refresh to rebuild the description.
        GamepadInput.OnActiveDeviceChanged += HandleDeviceChanged;
        SteamInputGlyphs.OnGlyphsChanged += InvalidateCard;
    }

    private void HandleDeviceChanged(ActiveInputDevice _) => InvalidateCard();
    private void InvalidateCard() => lastHighlighted = null; // != current highlight → forces a re-resolve

    // Attach click handlers + fill the highlight-tint array from the two conversation buttons and the
    // confirm dialog's Yes/No. Wiring your own Buttons is all it takes to make them work.
    private void WireButtons()
    {
        conversationButtons[0] = fishingSuppliesButton != null ? fishingSuppliesButton.targetGraphic as Image : null;
        conversationButtons[1] = sellFishButton != null ? sellFishButton.targetGraphic as Image : null;

        if (fishingSuppliesButton != null)
            fishingSuppliesButton.onClick.AddListener(() =>
                { if (ShopController.Instance != null) ShopController.Instance.OpenFishingWall(); });
        if (sellFishButton != null)
            sellFishButton.onClick.AddListener(() =>
                { if (ShopController.Instance != null) ShopController.Instance.OpenSellMenu(); });
        if (confirmYesButton != null)
            confirmYesButton.onClick.AddListener(() =>
                { if (ShopController.Instance != null) ShopController.Instance.ConfirmSell(); });
        if (confirmNoButton != null)
            confirmNoButton.onClick.AddListener(() =>
                { if (ShopController.Instance != null) ShopController.Instance.CancelSell(); });
    }

    private void Update()
    {
        if (controller == null)
        {
            controller = ShopController.Instance;
            if (controller != null) controller.OnPurchaseFeedback += ShowToast;
        }

        // Hide the buy toast once its time is up.
        if (toastRoot != null && toastRoot.activeSelf && Time.unscaledTime >= toastHideTime)
            toastRoot.SetActive(false);

        ShopController.ShopScreen screen = controller != null ? controller.Screen : ShopController.ShopScreen.Closed;

        switch (screen)
        {
            case ShopController.ShopScreen.Conversation:
                SetActiveRoot(conversationRoot);
                RefreshConversationHighlight();
                break;
            case ShopController.ShopScreen.Browsing:
                SetActiveRoot(browsingRoot);
                RefreshCardIfChanged();
                // Show the fixed bottom card only while something is highlighted (toggle on change).
                bool show = controller != null && controller.Highlighted != null;
                if (cardRoot != null && cardRoot.gameObject.activeSelf != show)
                    cardRoot.gameObject.SetActive(show);
                break;
            case ShopController.ShopScreen.Selling:
                SetActiveRoot(sellRoot);
                RefreshSellScreen();
                break;
            default: SetActiveRoot(null); break;
        }
    }

    private void SetActiveRoot(RectTransform active)
    {
        if (conversationRoot != null) conversationRoot.gameObject.SetActive(active == conversationRoot);
        if (browsingRoot != null) browsingRoot.gameObject.SetActive(active == browsingRoot);
        if (sellRoot != null) sellRoot.gameObject.SetActive(active == sellRoot);
    }

    private void RefreshConversationHighlight()
    {
        int idx = controller != null ? controller.ConversationIndex : 0;
        for (int i = 0; i < conversationButtons.Length; i++)
            if (conversationButtons[i] != null)
                conversationButtons[i].color = (i == idx) ? highlightColor : buttonColor;
    }

    private void RefreshSellScreen()
    {
        int count = PlayerInventory.Instance != null ? PlayerInventory.Instance.caughtFishes.Count : 0;
        if (count != lastSellCount) { RebuildSellList(); lastSellCount = count; }

        int sel = controller != null ? controller.SellIndex : 0;
        for (int i = 0; i < sellRowImages.Count; i++)
            if (sellRowImages[i] != null)
                sellRowImages[i].color = (i == sel) ? highlightColor : rowColor;

        if (sellEmptyLabel != null) sellEmptyLabel.gameObject.SetActive(count == 0);

        bool awaiting = controller != null && controller.AwaitingSellConfirm;
        if (confirmRoot != null) confirmRoot.gameObject.SetActive(awaiting);
        if (awaiting && confirmText != null)
        {
            List<CaughtFish> fishes = PlayerInventory.Instance != null ? PlayerInventory.Instance.caughtFishes : null;
            if (fishes != null && sel >= 0 && sel < fishes.Count && fishes[sel] != null && fishes[sel].preset != null)
                confirmText.text = $"Sell {fishes[sel].preset.fishName} for {fishes[sel].GetValue()} coins?";
        }
    }

    // Rebuild the info card only when the highlighted item, the player's coins, or the sold-out
    // state actually changes — cheap polling instead of an every-frame rebuild.
    private void RefreshCardIfChanged()
    {
        // Nothing to fill if the card labels aren't wired yet (while you're still authoring the
        // panel) — bail rather than throw.
        if (cardName == null || cardPrice == null || cardDescription == null) return;

        ShopItem hi = controller != null ? controller.Highlighted : null;
        int coins = PlayerInventory.Instance != null ? PlayerInventory.Instance.currentCurrency : 0;
        bool soldOut = hi != null && hi.Offer != null && hi.Offer.IsSoldOut();

        if (hi == lastHighlighted && coins == lastCurrency && soldOut == lastSoldOut) return;
        lastHighlighted = hi;
        lastCurrency = coins;
        lastSoldOut = soldOut;

        if (hi == null || hi.Offer == null)
        {
            cardName.text = "Fishing Supplies";
            cardPrice.text = string.Empty;
            cardDescription.text = GamepadInput.IsGamepadActive
                ? "Move to an item to inspect it." : "Hover an item to inspect it.";
            return;
        }

        ShopOffer offer = hi.Offer;
        cardName.text = offer.DisplayName;
        // Descriptions carry {verb} tokens (tackle/bait blurbs) — resolve them to inline controller
        // glyphs / device-correct text, same as the gear-menu selectors.
        GlyphRichText.Apply(cardDescription, offer.Description);

        if (soldOut)
        {
            cardPrice.text = "SOLD OUT";
            cardPrice.color = soldOutColor;
        }
        else
        {
            bool afford = coins >= offer.Price;
            cardPrice.text = $"{offer.Price} coins";
            cardPrice.color = afford ? goldColor : soldOutColor;
        }
    }

    // Clone the authored sell-row template once per caught fish into the list container. The
    // container's own layout (e.g. a VerticalLayoutGroup) positions the rows.
    private void RebuildSellList()
    {
        if (sellListRoot == null || sellRowPrefab == null) return;

        for (int i = sellListRoot.childCount - 1; i >= 0; i--)
            Destroy(sellListRoot.GetChild(i).gameObject);
        sellRowImages.Clear();

        if (PlayerInventory.Instance == null) return;
        List<CaughtFish> fishes = PlayerInventory.Instance.caughtFishes;
        for (int i = 0; i < fishes.Count; i++)
        {
            CaughtFish f = fishes[i];
            int index = i; // capture for the click closure
            string label = (f != null && f.preset != null) ? f.preset.fishName : "—";
            int value = f != null ? f.GetValue() : 0;

            GameObject row = Instantiate(sellRowPrefab, sellListRoot);
            row.SetActive(true);

            TextMeshProUGUI rowLabel = row.GetComponentInChildren<TextMeshProUGUI>(true);
            if (rowLabel != null)
            {
                rowLabel.richText = true;
                rowLabel.text = $"{label}    <color=#FFD96A>{value}c</color>";
            }

            Button rowButton = row.GetComponentInChildren<Button>(true);
            if (rowButton != null)
                rowButton.onClick.AddListener(() =>
                    { if (ShopController.Instance != null) ShopController.Instance.RequestSellAt(index); });

            // The graphic tinted for the selection highlight: the button's target graphic, else any
            // Image on the row root.
            Image rowImg = rowButton != null ? rowButton.targetGraphic as Image : null;
            if (rowImg == null) rowImg = row.GetComponent<Image>();
            sellRowImages.Add(rowImg); // index i stays aligned with caughtFishes[i]
        }
    }
}
