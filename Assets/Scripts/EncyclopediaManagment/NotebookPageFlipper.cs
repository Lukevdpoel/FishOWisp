using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives a notebook page-flip with live world-space UI at rest and a bone-rigged mesh
/// during the flip animation. Bakes the two relevant page canvases to RenderTextures
/// at the moment the flip starts, then swaps to the rigged mesh, runs the bone animation,
/// and restores live UI on the new spread.
/// </summary>
public class NotebookPageFlipper : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("Used to gate input — flips only happen while the notebook is open.")]
    public NoteMenu noteMenu;

    [Header("Pages (linear list)")]
    [Tooltip("All page canvases, in reading order. Index 0 = first left page, index 1 = first right page, " +
             "index 2 = second left page, etc. Leave null entries for blank pages.")]
    public List<Canvas> pages = new List<Canvas>();

    [Tooltip("All page canvases that should be hidden during a bake to isolate the target. " +
             "Usually the same as 'pages' but you can include extras (e.g. static covers).")]
    public List<Canvas> bakeableCanvases = new List<Canvas>();

    [Header("Bake")]
    [Tooltip("Camera framed over the LEFT page stack. Used to bake any even-indexed page (Page_0, Page_2, ...).")]
    public Camera leftBakeCamera;
    [Tooltip("Camera framed over the RIGHT page stack. Used to bake any odd-indexed page (Page_1, Page_3, ...).")]
    public Camera rightBakeCamera;
    public RenderTexture frontFaceRT;
    public RenderTexture backFaceRT;

    [Header("Flip Page")]
    [Tooltip("The GameObject that contains the flipping mesh (placeholder quad now, rigged skinned mesh later). " +
             "Has its own Animator with FlipForward/FlipBackward triggers.")]
    public GameObject flipPageObject;
    public Animator flipAnimator;
    [Tooltip("Renderer for the TOP (front) face. Receives frontFaceRT.")]
    public Renderer topFaceRenderer;
    [Tooltip("Renderer for the BOTTOM (back) face. Receives backFaceRT.")]
    public Renderer bottomFaceRenderer;
    [Tooltip("Texture property name on the page materials. URP/Unlit and URP/Lit use _BaseMap.")]
    public string textureProperty = "_BaseMap";

    [Header("Animation")]
    [Tooltip("Name of the Animator state (NOT trigger) to play for a forward flip. " +
             "Must match a state in the controller's Base Layer.")]
    public string flipForwardTrigger = "FlipForward";
    [Tooltip("Name of the Animator state (NOT trigger) to play for a backward flip. " +
             "Must match a state in the controller's Base Layer.")]
    public string flipBackwardTrigger = "FlipBackward";
    [Tooltip("Total real-time duration of one flip, in seconds. Single source of truth: " +
             "the animator's playback speed is auto-scaled at runtime so the clip finishes " +
             "exactly when this timer does. Lower = faster flip. No need to touch the .anim files.")]
    public float flipDuration = 0.6f;
    [Tooltip("Short delay after the flip starts before the new page (revealed under the lifting page) becomes visible. " +
             "Prevents the new page from being briefly seen before the flip mesh covers it. 0.04-0.08s is usually enough.")]
    public float revealDelay = 0.05f;
    [Tooltip("Short delay after the animation ends, during which the flip mesh stays visible while the new live page " +
             "has time to render underneath. Prevents the new page from being briefly seen before the mesh fully covers it. " +
             "Symmetric to revealDelay but applies at the end of the flip.")]
    public float endBuffer = 0.05f;
    [Tooltip("Do the landing-side page swap this many seconds BEFORE the animation reaches its end pose. " +
             "Reduces the chance of a one-frame clip at the exact moment the animation transitions out of its " +
             "playing state. The mesh continues animating to its end pose while the new live page is already underneath.")]
    public float swapAdvance = 0.05f;

    [Header("Input")]
    public bool useKeyboard = true;
    public bool wrapAround = false;

    private int currentSpread = 0;
    private bool isFlipping = false;
    private MaterialPropertyBlock mpb;
    // One-slot input queue: 0 = none, +1 = next, -1 = prev.
    // Captures at most one press during an in-flight flip so the player can chain
    // exactly one extra flip, but cannot stack a long backlog by mashing the key.
    private int queuedDirection = 0;

    public int CurrentSpread => currentSpread;
    public int SpreadCount => Mathf.Max(1, Mathf.CeilToInt(pages.Count / 2f));
    public bool IsFlipping => isFlipping;

    void Awake()
    {
        mpb = new MaterialPropertyBlock();

        // Flips only happen while the notebook is open, and the open notebook sets
        // Time.timeScale to 0 — the flip animation must run on unscaled time or the
        // page mesh freezes mid-air while the realtime waits below keep counting.
        if (flipAnimator != null) flipAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
    }

    void Start()
    {
        if (flipPageObject != null) flipPageObject.SetActive(false);
        ShowSpread(currentSpread);
    }

    void Update()
    {
        if (noteMenu == null || !noteMenu.IsNoteOpen) return;

        bool next = false;
        bool prev = false;

        if (useKeyboard)
        {
            // The arrow bindings come from the InputBindings asset; E/Q stay as fixed alternates
            // (they've always flipped pages while the notebook is open).
            next = KeyInput.PageNextPressed || Input.GetKeyDown(KeyCode.E);
            prev = KeyInput.PagePrevPressed || Input.GetKeyDown(KeyCode.Q);
        }

        // RB flips forward, LB flips backward while the notebook is open.
        next |= GamepadInput.PageNextPressed;
        prev |= GamepadInput.PagePrevPressed;

        if (next) FlipNext();
        else if (prev) FlipPrev();
    }

    public void FlipNext()
    {
        if (isFlipping)
        {
            if (queuedDirection == 0) queuedDirection = 1;
            return;
        }
        int target = currentSpread + 1;
        if (target >= SpreadCount)
        {
            if (!wrapAround) return;
            target = 0;
        }
        StartCoroutine(FlipRoutine(target, forward: true));
    }

    public void FlipPrev()
    {
        if (isFlipping)
        {
            if (queuedDirection == 0) queuedDirection = -1;
            return;
        }
        int target = currentSpread - 1;
        if (target < 0)
        {
            if (!wrapAround) return;
            target = SpreadCount - 1;
        }
        StartCoroutine(FlipRoutine(target, forward: false));
    }

    private IEnumerator FlipRoutine(int targetSpread, bool forward)
    {
        isFlipping = true;

        Canvas currentRight = GetPage(2 * currentSpread + 1);
        Canvas currentLeft  = GetPage(2 * currentSpread);
        Canvas targetLeft   = GetPage(2 * targetSpread);
        Canvas targetRight  = GetPage(2 * targetSpread + 1);

        // Forward (FlipPage rotates 0→180):
        //   TopFace visible at start → currentRight (page lifting off the right)
        //   BottomFace visible at end → targetLeft (page landing on the left)
        // Backward (FlipPage rotates 180→0):
        //   BottomFace visible at start → currentLeft (page lifting off the left)
        //   TopFace visible at end → targetRight (page landing on the right)
        Canvas frontFaceSource = forward ? currentRight : targetRight;
        Canvas backFaceSource  = forward ? targetLeft   : currentLeft;

        BakeCanvas(frontFaceSource, frontFaceRT);
        BakeCanvas(backFaceSource,  backFaceRT);
        ApplyRTsToMaterials();

        // Hide every page that isn't part of the current or target spread,
        // so leftover state from earlier multi-flips can't bleed in.
        for (int i = 0; i < pages.Count; i++)
        {
            Canvas c = pages[i];
            if (c == null) continue;
            if (c == currentLeft || c == currentRight || c == targetLeft || c == targetRight) continue;
            SetCanvasActive(c, false);
        }

        // Phase 1: hide the lifting page, keep the non-lifting side visible.
        // Do NOT yet reveal the page underneath — that happens after revealDelay,
        // once the flip mesh is in position to cover it.
        if (forward)
        {
            SetCanvasActive(currentRight, false); // lifting off
            SetCanvasActive(currentLeft, true);   // stays visible
            SetCanvasActive(targetLeft, false);   // not yet (back face of flip mesh)
            SetCanvasActive(targetRight, false);  // not yet — wait for the mesh to render in front of it
        }
        else
        {
            SetCanvasActive(currentLeft, false);
            SetCanvasActive(currentRight, true);
            SetCanvasActive(targetRight, false);
            SetCanvasActive(targetLeft, false);
        }

        // Show the flip mesh and force the animator into the start of the flip state
        // synchronously. Using Play(...) + Update(0f) instead of SetTrigger avoids the
        // one-frame delay where the animator is still in Idle (bind-pose) before the
        // transition is processed — that delay was visible as a flicker at flip start.
        if (flipPageObject != null) flipPageObject.SetActive(true);
        if (flipAnimator != null)
        {
            string stateName = forward ? flipForwardTrigger : flipBackwardTrigger;
            if (!string.IsNullOrEmpty(stateName))
            {
                flipAnimator.Play(stateName, 0, 0f);
                flipAnimator.Update(0f);
                // Scale playback so the clip finishes in exactly flipDuration seconds,
                // regardless of the clip's authored length. Lets flipDuration be the
                // single knob for "how fast does the flip feel".
                var clipInfo = flipAnimator.GetCurrentAnimatorClipInfo(0);
                if (clipInfo.Length > 0 && clipInfo[0].clip != null && flipDuration > 0f)
                {
                    flipAnimator.speed = clipInfo[0].clip.length / flipDuration;
                }
            }
        }

        // Brief delay so the flip mesh actually renders in its starting pose
        // before we reveal the page underneath. Without this you can see a flash
        // of the new page during the first frame.
        yield return new WaitForSecondsRealtime(revealDelay);

        // Phase 2: reveal the page underneath the flip mesh.
        if (forward) SetCanvasActive(targetRight, true);
        else         SetCanvasActive(targetLeft, true);

        // Wait until shortly before the animation reaches its end pose,
        // so we can do the landing-side swap while the mesh is still mid-animation
        // and steadily covering the landing area (instead of swapping exactly at
        // the precision-sensitive moment the animation finishes).
        float beforeSwap = Mathf.Max(0f, flipDuration - revealDelay - swapAdvance);
        yield return new WaitForSecondsRealtime(beforeSwap);

        // Landing-side swap. The mesh is still finishing its rotation and is
        // covering the landing side, so the new live page becomes active
        // underneath the mesh.
        if (forward)
        {
            SetCanvasActive(currentLeft, false);
            SetCanvasActive(targetLeft, true);
        }
        else
        {
            SetCanvasActive(currentRight, false);
            SetCanvasActive(targetRight, true);
        }

        // Let the animation finish reaching its end pose, then hold for endBuffer
        // so the live canvas underneath is fully rendered before the mesh hides.
        float afterSwap = swapAdvance + endBuffer;
        if (afterSwap > 0f)
            yield return new WaitForSecondsRealtime(afterSwap);

        if (flipPageObject != null) flipPageObject.SetActive(false);
        currentSpread = targetSpread;
        // Final safety: make sure exactly the target spread's pages are showing.
        ShowSpread(currentSpread);

        isFlipping = false;

        // Consume one queued press, if any, and start the chained flip.
        if (queuedDirection != 0)
        {
            int dir = queuedDirection;
            queuedDirection = 0;
            if (dir > 0) FlipNext();
            else FlipPrev();
        }
    }

    private void ShowSpread(int spread)
    {
        // Set each page to its desired final state directly.
        // Pages already in the correct state are skipped by SetCanvasActive's check,
        // so we never toggle a canvas off and on within the same frame.
        int leftIdx = 2 * spread;
        int rightIdx = 2 * spread + 1;
        for (int i = 0; i < pages.Count; i++)
        {
            bool shouldBeActive = (i == leftIdx || i == rightIdx);
            SetCanvasActive(pages[i], shouldBeActive);
        }
    }

    private Canvas GetPage(int index)
    {
        if (index < 0 || index >= pages.Count) return null;
        return pages[index];
    }

    private static void SetCanvasActive(Canvas c, bool active)
    {
        if (c == null) return;
        if (c.gameObject.activeSelf != active) c.gameObject.SetActive(active);
    }

    /// <summary>
    /// Renders a single canvas into the given RT by temporarily disabling all
    /// other bakeable canvases. Restores their previous active state afterward.
    /// Picks the appropriate bake camera based on whether the target is a left
    /// or right page (determined by its index in the `pages` list).
    /// </summary>
    private void BakeCanvas(Canvas target, RenderTexture rt)
    {
        if (rt == null) return;
        if (target == null)
        {
            ClearRT(rt);
            return;
        }

        Camera cam = GetBakeCameraFor(target);
        if (cam == null) return;

        // Snapshot current state and isolate the target
        int n = bakeableCanvases.Count;
        bool[] prev = new bool[n];
        for (int i = 0; i < n; i++)
        {
            Canvas c = bakeableCanvases[i];
            if (c == null) continue;
            prev[i] = c.gameObject.activeSelf;
            c.gameObject.SetActive(c == target);
        }

        if (!target.gameObject.activeSelf) target.gameObject.SetActive(true);

        Canvas.ForceUpdateCanvases();

        RenderTexture prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prevTarget;

        // Restore
        for (int i = 0; i < n; i++)
        {
            Canvas c = bakeableCanvases[i];
            if (c == null) continue;
            c.gameObject.SetActive(prev[i]);
        }
    }

    private Camera GetBakeCameraFor(Canvas target)
    {
        int idx = pages.IndexOf(target);
        if (idx < 0)
        {
            // Fallback: prefer right camera if available, otherwise left
            return rightBakeCamera != null ? rightBakeCamera : leftBakeCamera;
        }
        // Even index = left page, odd = right page
        bool isLeft = (idx % 2 == 0);
        return isLeft ? leftBakeCamera : rightBakeCamera;
    }

    private void ClearRT(RenderTexture rt)
    {
        if (rt == null) return;
        RenderTexture active = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, new Color(0, 0, 0, 0));
        RenderTexture.active = active;
    }

    private void ApplyRTsToMaterials()
    {
        if (topFaceRenderer != null)
        {
            topFaceRenderer.GetPropertyBlock(mpb);
            mpb.SetTexture(textureProperty, frontFaceRT);
            topFaceRenderer.SetPropertyBlock(mpb);
        }
        if (bottomFaceRenderer != null)
        {
            bottomFaceRenderer.GetPropertyBlock(mpb);
            mpb.SetTexture(textureProperty, backFaceRT);
            bottomFaceRenderer.SetPropertyBlock(mpb);
        }
    }
}
