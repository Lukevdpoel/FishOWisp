using UnityEngine;

// Watches the player's distance from the cast origin and their facing direction relative
// to the bobber, and reports when an auto-reel should trigger. Pure check — no side effects,
// no coroutines. FishingRodController calls Check() each Update during WaitingForBite.
public static class FishingAutoReel
{
    public enum Reason { None, WalkedTooFar, TurnedAway }

    public static Reason Check(
        Vector3 playerPos,
        Vector3 castOriginPos,
        Transform bobberTransform,
        Transform playerModel,
        float castStartTime,
        float maxDistanceFromCast,
        float turnAwayDotThreshold,
        float turnAwayGracePeriod)
    {
        float distSqr = (playerPos - castOriginPos).sqrMagnitude;
        if (distSqr > maxDistanceFromCast * maxDistanceFromCast) return Reason.WalkedTooFar;

        if (bobberTransform == null || playerModel == null) return Reason.None;
        if (Time.time - castStartTime < turnAwayGracePeriod) return Reason.None;

        Vector3 toBobber = bobberTransform.position - playerModel.position;
        toBobber.y = 0f;
        Vector3 facing = playerModel.forward;
        facing.y = 0f;
        if (toBobber.sqrMagnitude < 0.01f || facing.sqrMagnitude < 0.01f) return Reason.None;

        float dot = Vector3.Dot(facing.normalized, toBobber.normalized);
        return dot < turnAwayDotThreshold ? Reason.TurnedAway : Reason.None;
    }
}
