using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// Part of BobberController (partial class). Serialized fields live in BobberController.cs.
public partial class BobberController
{
    // ------------------------------------------------------------------------
    // FISHING LOGIC
    // ------------------------------------------------------------------------
    public void StartNibbleSequence(FishPreset preset)
    {
        if (nibbleCoroutine != null) StopCoroutine(nibbleCoroutine);
        nibbleCoroutine = StartCoroutine(NibbleRoutine(preset));
    }

    public void CancelNibbleSequence()
    {
        if (nibbleCoroutine != null)
        {
            StopCoroutine(nibbleCoroutine);
            nibbleCoroutine = null;
        }
    }

    // Consume one unit of the equipped bait — the fish has taken it, whether on a successful hook
    // or when it gets away with it on a missed bobber bite. No-op for lures (no bait selected).
    public void ConsumeEquippedBait()
    {
        if (BaitInventory.Instance == null) return;
        BaitItem equipped = BaitInventory.Instance.SelectedBait;
        if (equipped == null) return;
        if (!BaitInventory.Instance.TryConsume(equipped, 1))
            Debug.LogWarning($"[BobberController] Tried to consume {equipped.displayName} but the player had none.");
    }

    public void HookFish(FishPreset fishPreset)
    {
        if (hookedFish != null) return;

        ConsumeEquippedBait();

        hookedFish = new CaughtFish(fishPreset);
        Debug.Log($"{hookedFish.GetDisplayName()} is on the line!");

        // From the bite onward the line holds a fish, not a float: the bobber/lure stops
        // rendering and the hooked silhouette (chain + sway) thrashes at the hook point.
        AttachHookedFishVisual();

        FishingEvents.OnFishBite?.Invoke(this);

        struggleAnchor = transform.position;
        initialStruggleAnchor = struggleAnchor;
        hasStruggleAnchor = true;

        SpawnEffect(bitePrefab, biteLifetime);

        isSubmerged = true;
    }

    public void PlayAttractJiggle()
    {
        if (rb != null && isInWater && !rb.isKinematic)
        {
            rb.AddForce(Vector3.down * nibbleForce * 0.5f, ForceMode.Impulse);
        }
        SpawnEffect(nibblePrefab, nibbleLifetime);
    }

    // A fish brushed the bobber while darting past on a nibble: a single small downward tap so the
    // bobber dips and settles quickly — a slight, short wobble, not the old in-place jab.
    // fishForward adds a slight lateral nudge matching the fish's swim direction so the dip reads
    // as a physical brush rather than a straight-down jab.
    public void PlayNibbleWobble(Vector3 fishForward)
    {
        if (rb != null && isInWater && !rb.isKinematic)
        {
            Vector3 impulse = Vector3.down * nibbleWobbleForce + fishForward * (nibbleWobbleForce * 0.25f);
            rb.AddForce(impulse, ForceMode.Impulse);
        }
        // Crisp visible tilt on top of the dip — reads clearly on a stiffly-floating lure too.
        nibbleWobbleAmp = nibbleWobbleDeg;
        nibbleWobblePhase = 0f;
        SpawnEffect(nibblePrefab, nibbleLifetime);
        FishingEvents.OnFishNibble?.Invoke(this);
    }

    // Spawns one splash at the popper's nose (the LineAttachPoint, i.e. the chattering front),
    // riding the water surface. Called on a cadence by LureReelHandler while a Popper lure is being
    // tugged/reeled. Falls back to the nibble splash when no dedicated popper splash is assigned.
    public void PlayPopperSplash()
    {
        if (!isInWater) return;
        GameObject prefab = popperSplashPrefab != null ? popperSplashPrefab : nibblePrefab;
        if (prefab == null) return;

        Transform nose = LineAttachPoint;
        Vector3 spawnPos = new Vector3(nose.position.x, waterSurfaceY, nose.position.z);
        GameObject instance = Instantiate(prefab, spawnPos, prefab.transform.rotation);
        Destroy(instance, popperSplashLifetime);
    }

    public void SetStruggleActive(bool active)
    {
        if (isStruggling == active) return;

        isStruggling = active;
        if (active) struggleTimer = 0;
    }

    public void SwapBobberForFishModel()
    {
        if (hookedFish == null) return;

        // The hooked visual normally attaches at the bite (HookFish); this is a safety net
        // for any path that reaches the reel-in without it, and keeps the OnFishHooked
        // event timing unchanged for listeners.
        AttachHookedFishVisual();
        FishingEvents.OnFishHooked?.Invoke(hookedFish);
    }

    // The fish got away (missed hook window or lost fight): hand the hooked visual its
    // freedom so it swims off and fades instead of vanishing with the destroyed bobber.
    public void ReleaseHookedFishToEscape()
    {
        if (activeFishModel == null) return;
        HookedFishController hooked = activeFishModel.GetComponent<HookedFishController>();
        if (hooked != null) hooked.BeginEscape();
        activeFishModel = null;
        if (bobberVisuals != null) bobberVisuals.SetActive(true);
    }

    // Hides the bobber/lure visuals and spawns the chain+sway hooked fish with its mouth
    // pinned to the hook point. Registered as activeFishModel so the existing reset path
    // (restore visuals, destroy model) cleans it up on catch, escape and cancel alike.
    private void AttachHookedFishVisual()
    {
        if (activeFishModel != null || hookedFish == null) return;

        if (bobberVisuals != null) bobberVisuals.SetActive(false);

        Material mat = hookedFishSilhouetteMaterial;
        if (mat == null)
        {
            if (sharedHookedSilhouette == null)
            {
                Shader swayShader = Shader.Find("FishOWisp/Fish Silhouette Sway");
                if (swayShader != null)
                    sharedHookedSilhouette = new Material(swayShader) { name = "HookedFishSilhouette (runtime)" };
            }
            mat = sharedHookedSilhouette;
        }

        activeFishModel = HookedFishController.Attach(this, hookedFish.preset, mat).gameObject;
    }

    // ------------------------------------------------------------------------
    // EFFECT ROUTINES
    // ------------------------------------------------------------------------

    private void SpawnEffect(GameObject prefab, float lifetime, bool parentToBobber = false)
    {
        if (prefab == null || !isInWater) return;

        Vector3 spawnPos = new Vector3(transform.position.x, waterSurfaceY, transform.position.z);
        Quaternion spawnRot = prefab.transform.rotation;

        GameObject instance = Instantiate(prefab, spawnPos, spawnRot);

        if (parentToBobber)
        {
            instance.transform.SetParent(this.transform);
        }

        Destroy(instance, lifetime);
    }

    private IEnumerator NibbleRoutine(FishPreset fishPreset)
    {
        yield return new WaitForSeconds(nibbleInterval);
        int nibbleCount = Random.Range(minNibbles, maxNibbles + 1);

        for (int i = 0; i < nibbleCount; i++)
        {
            if (rb != null) rb.AddForce(Vector3.down * nibbleForce, ForceMode.Impulse);
            SpawnEffect(nibblePrefab, nibbleLifetime);
            FishingEvents.OnFishNibble?.Invoke(this);
            yield return new WaitForSeconds(nibbleInterval);
        }

        // Signal that the bite is about to happen, then do the final nibble
        FishingEvents.OnBiteImminent?.Invoke(this);
        if (rb != null) rb.AddForce(Vector3.down * nibbleForce, ForceMode.Impulse);
        SpawnEffect(nibblePrefab, nibbleLifetime);
        FishingEvents.OnFishNibble?.Invoke(this);
        yield return new WaitForSeconds(biteDelay);

        HookFish(fishPreset);
    }

    public void StopBiteEffects()
    {
        isSubmerged = false;
    }

}
