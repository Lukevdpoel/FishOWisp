using UnityEngine;

/// <summary>
/// Frame-stamped "an interactable is in range" signal for HUD prompts. Every interactable that
/// would accept the interact button this frame calls <see cref="Ping"/> from its Update (each of
/// them already computes exactly that condition to read the press), and <see cref="Available"/>
/// answers whether ANY of them did so this frame or last. The one-frame grace covers script
/// execution order — a reader that Updates before the interactables would otherwise flicker.
///
/// A frame stamp instead of enter/exit registration means there is nothing to unregister: an
/// interactable that gets disabled, destroyed, or scene-unloaded mid-zone simply stops pinging
/// and the hint clears itself next frame.
/// </summary>
public static class InteractHint
{
    private static int lastPingFrame = int.MinValue;

    /// <summary>Call once per frame while this interactable would accept the interact press.</summary>
    public static void Ping() => lastPingFrame = Time.frameCount;

    /// <summary>True while at least one interactable pinged this frame or the previous one.</summary>
    public static bool Available => Time.frameCount - lastPingFrame <= 1;

    // Required with Reload Domain disabled, so a stale stamp can't leak into the next play session.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => lastPingFrame = int.MinValue;
}
