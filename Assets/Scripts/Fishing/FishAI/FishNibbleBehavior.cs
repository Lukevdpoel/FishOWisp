using UnityEngine;

// The "nibbling" sub-state of a fish: it has decided to bite the bobber but the player still
// has time to react. The fish hovers near the bobber, occasionally jabbing in (driven by
// FishingEvents.OnFishNibble), and stops pulling back once the bite is imminent.
//
// Owned by FishRipple. FishRipple calls Start() when entering Nibbling, Tick() each frame,
// and Stop() when leaving (scared, caught, or bite). Subscribes to fishing events internally
// so callers don't have to manage them.
public class FishNibbleBehavior
{
    private readonly Transform host;
    private float waterSurfaceY;

    private Transform bobberTransform;
    private bool isJabbing;
    private float jabTimer;
    private bool isBiteJabbing;
    private Vector3 nibbleHoverPos;

    private const float JabDuration = 0.15f;
    private const float JabReturnSpeed = 6f;

    public bool IsBiteJabbing => isBiteJabbing;

    public FishNibbleBehavior(Transform host)
    {
        this.host = host;
    }

    public void Begin(Transform bobber, float surfaceY, FishPreset preset)
    {
        bobberTransform = bobber;
        waterSurfaceY = surfaceY;
        isJabbing = false;
        isBiteJabbing = false;

        nibbleHoverPos = host.position;
        nibbleHoverPos.y = waterSurfaceY;

        BobberController bc = bobber != null ? bobber.GetComponent<BobberController>() : null;
        if (bc != null) bc.StartNibbleSequence(preset);

        FishingEvents.OnFishBite += OnFishBite;
        FishingEvents.OnFishNibble += OnNibbleJab;
        FishingEvents.OnBiteImminent += OnBiteImminentEvent;
    }

    // Stop is idempotent — safe to call multiple times. Unsubscribes regardless of whether
    // Begin had been called (no harm in unsubscribing an unsubscribed delegate).
    public void Stop()
    {
        FishingEvents.OnFishBite -= OnFishBite;
        FishingEvents.OnFishNibble -= OnNibbleJab;
        FishingEvents.OnBiteImminent -= OnBiteImminentEvent;
        isBiteJabbing = false;
        isJabbing = false;
    }

    // Called by FishRipple in its OnDisable as belt-and-suspenders cleanup.
    public void UnsubscribeAll() => Stop();

    public void SetWaterSurfaceY(float y) => waterSurfaceY = y;

    public void Tick(float scareSpeed)
    {
        if (bobberTransform == null) return;

        Vector3 bobberPos = bobberTransform.position;
        bobberPos.y = waterSurfaceY;

        if (isJabbing)
        {
            // Dart quickly toward the bobber.
            host.position = Vector3.MoveTowards(host.position, bobberPos, scareSpeed * Time.deltaTime);
            FishMovementHelpers.FaceMovementDirection(host, bobberPos);

            jabTimer -= Time.deltaTime;
            if (jabTimer <= 0f) isJabbing = false;
        }
        else if (!isBiteJabbing)
        {
            // Drift back to hover position (but not if the bite is imminent).
            nibbleHoverPos.y = waterSurfaceY;
            host.position = Vector3.MoveTowards(host.position, nibbleHoverPos, JabReturnSpeed * Time.deltaTime);
            FishMovementHelpers.FaceMovementDirection(host, bobberPos);
        }
    }

    public void MarkBiteImminent() => isBiteJabbing = true;

    private void OnFishBite(BobberController bobber)
    {
        if (bobberTransform != null && bobber.transform == bobberTransform)
        {
            // Caller (FishRipple) will hear OnFishBite separately and hide visuals; we just
            // unsubscribe so this instance doesn't keep responding.
            FishingEvents.OnFishBite -= OnFishBite;
            FishingEvents.OnFishNibble -= OnNibbleJab;
        }
    }

    private void OnNibbleJab(BobberController bobber)
    {
        if (bobberTransform == null || bobber.transform != bobberTransform) return;
        isJabbing = true;
        jabTimer = JabDuration;
    }

    private void OnBiteImminentEvent(BobberController bobber)
    {
        if (bobberTransform != null && bobber.transform == bobberTransform)
        {
            MarkBiteImminent();
            FishingEvents.OnBiteImminent -= OnBiteImminentEvent;
        }
    }
}
