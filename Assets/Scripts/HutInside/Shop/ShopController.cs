using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Drives the 3D shop. Flow: FishVendor calls BeginConversation -> the one shop camera flies in and
// the conversation buttons appear (Fishing / Sell / Aquarium). Picking Fishing Supplies enters
// Browsing (same camera view) where the player highlights physical items (mouse hover on PC,
// d-pad/left-stick raster on a pad) and buys the highlighted one (left click / A). Sell Fish opens a
// list with an "are you sure?" confirm. Backing out (B / Esc) steps wall/sell -> conversation -> closed.
//
// Camera handling mirrors the project's existing pattern (CameraTrigger / ShopDoorController): a
// single dedicated shopCamera is enabled on top of whatever was Camera.main and flown from the
// interior view to its own authored resting pose on open (the ONE camera change — it frames both the
// conversation and the wall); the previous camera is restored on close.
public class ShopController : GenericSingleton<ShopController>
{
    public enum ShopScreen { Closed, Conversation, Browsing, Selling }

    [Header("Camera")]
    [Tooltip("Dedicated camera enabled while shopping. Position it in the shop interior so it frames " +
             "both the vendor conversation and the item wall — that single placed pose is the shop view.")]
    [SerializeField] private Camera shopCamera;
    [Tooltip("Seconds the camera takes to fly from the interior view to the shop view on open. 0 = hard cut.")]
    [SerializeField] private float cameraLerpDuration = 0.6f;
    [SerializeField] private AnimationCurve cameraEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Selection")]
    [Tooltip("Max ray distance for mouse hover hit-testing against shop items.")]
    [SerializeField] private float mouseRayDistance = 100f;

    public ShopScreen Screen { get; private set; } = ShopScreen.Closed;
    public bool IsOpen => Screen != ShopScreen.Closed;
    public ShopItem Highlighted { get; private set; }

    // Conversation menu selection (gamepad highlight): 0 = Fishing Supplies, 1 = Sell Fish.
    public const int ConversationOptionCount = 2;
    public int ConversationIndex { get; private set; }

    // Sell screen state — index into PlayerInventory.caughtFishes, plus the "are you sure?" gate.
    public int SellIndex { get; private set; }
    public bool AwaitingSellConfirm { get; private set; }
    public int SellableCount => PlayerInventory.Instance != null ? PlayerInventory.Instance.caughtFishes.Count : 0;

    // UI listens to these. State change fires on every screen transition; highlight change when the
    // browsed item changes; purchase/denied drive buy feedback.
    public event Action OnStateChanged;
    public event Action<ShopItem> OnHighlightChanged;
    public event Action<ShopItem> OnPurchase;
    public event Action OnPurchaseDenied;
    // (message, success) for on-screen buy feedback.
    public event Action<string, bool> OnPurchaseFeedback;

    private FishVendor activeVendor;
    private PlayerController player;
    private Camera previousCamera;
    private Coroutine transitionCo;
    private bool isTransitioning;
    private bool stickReady = true;

    private readonly List<ShopItem> browseItems = new List<ShopItem>();

    // The shop camera's authored resting pose, captured once before it's ever moved, so the open
    // transition always flies back to the exact spot it was placed at in the editor.
    private Vector3 shopCamRestPos;
    private Quaternion shopCamRestRot = Quaternion.identity;

    protected override void Awake()
    {
        base.Awake(); // GenericSingleton claims the instance
        if (shopCamera != null)
        {
            shopCamRestPos = shopCamera.transform.position;
            shopCamRestRot = shopCamera.transform.rotation;
        }
    }

    // ---- Entry / exit -------------------------------------------------------

    public void BeginConversation(FishVendor vendor)
    {
        if (IsOpen) return;
        activeVendor = vendor;
        player = FindFirstObjectByType<PlayerController>();
        if (player != null) player.LockControls(true);
        if (InteractionPromptUI.Instance != null) InteractionPromptUI.Instance.Hide();

        // The one and only camera change: fly the shop camera from wherever the interior camera was
        // to its authored resting pose. The conversation and the item wall share that single view.
        previousCamera = Camera.main;
        if (shopCamera != null)
        {
            if (previousCamera != null && previousCamera != shopCamera)
            {
                shopCamera.transform.SetPositionAndRotation(
                    previousCamera.transform.position, previousCamera.transform.rotation);
                EnableCamera(previousCamera, false);
            }
            EnableCamera(shopCamera, true);
            StartTransition(shopCamRestPos, shopCamRestRot);
        }
        else
        {
            Debug.LogWarning("[ShopController] No shopCamera assigned — opening shop without a camera move.");
        }

        ConversationIndex = 0;
        SetScreen(ShopScreen.Conversation);
    }

    public void OpenFishingWall()
    {
        if (Screen != ShopScreen.Conversation || isTransitioning) return;
        CacheBrowseItems(ShopCategory.Fishing);
        SetScreen(ShopScreen.Browsing); // no camera move — same single shop view
    }

    // The sell list is a UI overlay over the existing vendor-view framing — no camera move.
    public void OpenSellMenu()
    {
        if (Screen != ShopScreen.Conversation || isTransitioning) return;
        SellIndex = 0;
        AwaitingSellConfirm = false;
        SetScreen(ShopScreen.Selling);
    }

    // One step "back": wall/sell -> conversation, or conversation -> closed.
    public void BackOut()
    {
        if (!IsOpen || isTransitioning) return;
        if (Screen == ShopScreen.Browsing)
        {
            ClearHighlight();
            ConversationIndex = 0;
            SetScreen(ShopScreen.Conversation); // no camera move — same single shop view
        }
        else if (Screen == ShopScreen.Selling)
        {
            AwaitingSellConfirm = false;
            ConversationIndex = 0;
            SetScreen(ShopScreen.Conversation); // already at the vendor-view pose; no camera move
        }
        else
        {
            CloseShop();
        }
    }

    public void CloseShop()
    {
        if (!IsOpen) return;
        ClearHighlight();
        SetScreen(ShopScreen.Closed);

        if (transitionCo != null) { StopCoroutine(transitionCo); transitionCo = null; }
        isTransitioning = false;

        if (shopCamera != null) EnableCamera(shopCamera, false);
        if (previousCamera != null) EnableCamera(previousCamera, true);
        previousCamera = null;

        if (player != null) player.LockControls(false);
        player = null;

        // Clear the vendor's static without recursing back into CloseShop.
        FishVendor.ClearShoppingVendor();
        activeVendor = null;
    }

    // ---- Per-frame input ----------------------------------------------------

    private void Update()
    {
        if (!IsOpen || isTransitioning) return;
        switch (Screen)
        {
            case ShopScreen.Conversation: UpdateConversation(); break;
            case ShopScreen.Browsing: UpdateBrowsing(); break;
            case ShopScreen.Selling: UpdateSelling(); break;
        }
    }

    private static bool CancelKey() => GamepadInput.CancelPressed || Input.GetKeyDown(KeyCode.Escape);
    private static bool ConfirmKey() => GamepadInput.ConfirmPressed || Input.GetKeyDown(KeyCode.Return);

    // Fire once when the left stick is pushed past 0.6 from a neutral position; returns the
    // dominant axis direction (or zero). Shared by the conversation/sell list nav.
    private Vector2 StickStep()
    {
        Vector2 mv = GamepadInput.Move;
        if (mv.magnitude < 0.4f) { stickReady = true; return Vector2.zero; }
        if (!stickReady || mv.magnitude <= 0.6f) return Vector2.zero;
        stickReady = false;
        return Mathf.Abs(mv.x) >= Mathf.Abs(mv.y)
            ? new Vector2(Mathf.Sign(mv.x), 0f) : new Vector2(0f, Mathf.Sign(mv.y));
    }

    private void UpdateConversation()
    {
        if (CancelKey()) { CloseShop(); return; }

        int step = 0;
        if (GamepadInput.DpadUpPressed) step = -1;
        else if (GamepadInput.DpadDownPressed) step = 1;
        else { Vector2 s = StickStep(); if (s.y != 0f) step = s.y > 0f ? -1 : 1; }
        if (step != 0)
            ConversationIndex = Mathf.Clamp(ConversationIndex + step, 0, ConversationOptionCount - 1);

        if (ConfirmKey())
        {
            if (ConversationIndex == 0) OpenFishingWall();
            else if (ConversationIndex == 1) OpenSellMenu();
        }
    }

    private void UpdateSelling()
    {
        if (AwaitingSellConfirm)
        {
            if (ConfirmKey()) ConfirmSell();
            else if (CancelKey()) CancelSell();
            return;
        }

        if (CancelKey()) { BackOut(); return; }

        int count = SellableCount;
        if (count == 0) return;

        int step = 0;
        if (GamepadInput.DpadUpPressed) step = -1;
        else if (GamepadInput.DpadDownPressed) step = 1;
        else { Vector2 s = StickStep(); if (s.y != 0f) step = s.y > 0f ? -1 : 1; }
        if (step != 0) SellIndex = Mathf.Clamp(SellIndex + step, 0, count - 1);

        if (GamepadInput.ConfirmPressed) RequestSellAt(SellIndex);
        // Mouse clicking a row calls RequestSellAt(index) directly (ShopMenuUI).
    }

    // --- Sell flow (also called by ShopMenuUI's row / Yes / No buttons) ------

    public void RequestSellAt(int index)
    {
        if (Screen != ShopScreen.Selling || AwaitingSellConfirm) return;
        if (index < 0 || index >= SellableCount) return;
        SellIndex = index;
        AwaitingSellConfirm = true;
    }

    public void ConfirmSell()
    {
        AwaitingSellConfirm = false;
        if (PlayerInventory.Instance == null || activeVendor == null) return;

        System.Collections.Generic.List<CaughtFish> fishes = PlayerInventory.Instance.caughtFishes;
        if (SellIndex < 0 || SellIndex >= fishes.Count) return;

        CaughtFish fish = fishes[SellIndex];
        activeVendor.SellFishToVendor(fish);   // adds currency + plays sell particles
        PlayerInventory.Instance.RemoveFish(fish);
        SellIndex = Mathf.Clamp(SellIndex, 0, Mathf.Max(0, SellableCount - 1));
    }

    public void CancelSell() => AwaitingSellConfirm = false;

    private void UpdateBrowsing()
    {
        if (CancelKey()) { BackOut(); return; }

        // A mouse click always buys whatever is under the cursor — independent of the "active device",
        // so a connected/last-used gamepad can never leave the mouse unable to buy.
        if (Input.GetMouseButtonDown(0))
        {
            if (MouseOverItem(out ShopItem clicked)) { SetHighlight(clicked); TryBuy(clicked); }
            else Debug.Log("[Shop] Click registered, but no shop item was under the cursor.");
            return;
        }

        if (GamepadInput.IsGamepadActive)
        {
            // Gamepad: raster nav with the d-pad / left stick; the highlight persists between presses.
            if (GamepadInput.DpadUpPressed) NavigateDirection(Vector2.up);
            else if (GamepadInput.DpadDownPressed) NavigateDirection(Vector2.down);
            else if (GamepadInput.DpadLeftPressed) NavigateDirection(Vector2.left);
            else if (GamepadInput.DpadRightPressed) NavigateDirection(Vector2.right);
            else { Vector2 s = StickStep(); if (s != Vector2.zero) NavigateDirection(s); }

            if (GamepadInput.ConfirmPressed) TryBuy(Highlighted);
        }
        else
        {
            // Mouse: the highlight strictly follows the cursor — clear it when off all items so the
            // item drops back down (no "stuck lifted" item).
            SetHighlight(MouseOverItem(out ShopItem hovered) ? hovered : null);
        }
    }

    // ---- Selection ----------------------------------------------------------

    private void CacheBrowseItems(ShopCategory category)
    {
        browseItems.Clear();
        ShopItem[] all = FindObjectsByType<ShopItem>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && all[i].Category == category) browseItems.Add(all[i]);
    }

    private bool MouseOverItem(out ShopItem item)
    {
        item = null;
        if (shopCamera == null) return false;
        Ray ray = shopCamera.ScreenPointToRay(Input.mousePosition);

        // Test ONLY the cached shop-item colliders (a handful) rather than a whole-scene physics query.
        // No broadphase over the interior's colliders, and no dedicated layer required.
        float best = float.MaxValue;
        for (int i = 0; i < browseItems.Count; i++)
        {
            ShopItem it = browseItems[i];
            if (it == null) continue;
            if (it.RaycastHover(ray, mouseRayDistance, out float dist) && dist < best)
            {
                best = dist;
                item = it;
            }
        }
        return item != null;
    }

    // Pick the closest item that lies in the pressed direction (screen space).
    private void NavigateDirection(Vector2 dir)
    {
        if (browseItems.Count == 0 || shopCamera == null) return;
        if (Highlighted == null) { SetHighlight(browseItems[0]); return; }

        Vector3 curWp = shopCamera.WorldToScreenPoint(Highlighted.transform.position);
        Vector2 cur = new Vector2(curWp.x, curWp.y);

        ShopItem best = null;
        float bestScore = float.MaxValue;
        for (int i = 0; i < browseItems.Count; i++)
        {
            ShopItem it = browseItems[i];
            if (it == null || it == Highlighted) continue;
            Vector3 wp = shopCamera.WorldToScreenPoint(it.transform.position);
            if (wp.z <= 0f) continue; // behind the camera
            Vector2 delta = new Vector2(wp.x, wp.y) - cur;

            float along = Vector2.Dot(delta, dir);
            if (along <= 1f) continue; // not meaningfully in the pressed direction
            float perp = Mathf.Abs(delta.x * dir.y - delta.y * dir.x); // |cross| = off-axis spread
            float score = perp * 2f + along;     // reward aligned + nearby
            if (score < bestScore) { bestScore = score; best = it; }
        }

        if (best != null) SetHighlight(best);
    }

    private void SetHighlight(ShopItem item)
    {
        if (item == Highlighted) return;
        if (Highlighted != null) Highlighted.SetHighlighted(false);
        Highlighted = item;
        if (item != null) item.SetHighlighted(true);
        OnHighlightChanged?.Invoke(item);
    }

    private void ClearHighlight() => SetHighlight(null);

    private void TryBuy(ShopItem item)
    {
        if (item == null || item.Offer == null) return;
        ShopOffer offer = item.Offer;
        string name = offer.DisplayName;

        if (offer.IsSoldOut()) { Feedback($"{name} — already owned", false); OnPurchaseDenied?.Invoke(); return; }

        int coins = PlayerInventory.Instance != null ? PlayerInventory.Instance.currentCurrency : 0;
        if (coins < offer.Price)
        {
            Feedback($"Not enough coins — {name} costs {offer.Price}", false);
            OnPurchaseDenied?.Invoke();
            return;
        }

        if (offer.TryPurchase())
        {
            if (activeVendor != null && activeVendor.buyParticles != null) activeVendor.buyParticles.Play();
            Feedback($"Bought {name}", true);
            OnPurchase?.Invoke(item);
        }
        else
        {
            Feedback($"Couldn't buy {name}", false);
            OnPurchaseDenied?.Invoke();
        }
    }

    // Logs every purchase attempt to the console AND fires OnPurchaseFeedback for on-screen toast.
    private void Feedback(string message, bool success)
    {
        Debug.Log($"[Shop] {(success ? "OK" : "DENIED")}: {message}");
        OnPurchaseFeedback?.Invoke(message, success);
    }

    // ---- Camera -------------------------------------------------------------

    private void SetScreen(ShopScreen screen)
    {
        Screen = screen;
        OnStateChanged?.Invoke();
    }

    private void StartTransition(Vector3 toPos, Quaternion toRot)
    {
        if (transitionCo != null) StopCoroutine(transitionCo);
        transitionCo = StartCoroutine(LerpCamera(toPos, toRot));
    }

    private IEnumerator LerpCamera(Vector3 toPos, Quaternion toRot)
    {
        isTransitioning = true;

        if (shopCamera != null)
        {
            Vector3 fromPos = shopCamera.transform.position;
            Quaternion fromRot = shopCamera.transform.rotation;

            if (cameraLerpDuration <= 0f)
            {
                shopCamera.transform.SetPositionAndRotation(toPos, toRot);
            }
            else
            {
                float t = 0f;
                while (t < cameraLerpDuration)
                {
                    t += Time.unscaledDeltaTime;
                    float k = cameraEase.Evaluate(Mathf.Clamp01(t / cameraLerpDuration));
                    shopCamera.transform.SetPositionAndRotation(
                        Vector3.Lerp(fromPos, toPos, k),
                        Quaternion.Slerp(fromRot, toRot, k));
                    yield return null;
                }
                shopCamera.transform.SetPositionAndRotation(toPos, toRot);
            }
        }

        isTransitioning = false;
        transitionCo = null;
    }

    private static void EnableCamera(Camera cam, bool on)
    {
        if (cam == null) return;
        cam.gameObject.SetActive(on);
        if (on) cam.tag = "MainCamera";
        AudioListener al = cam.GetComponent<AudioListener>();
        if (al != null) al.enabled = on;
    }
}
