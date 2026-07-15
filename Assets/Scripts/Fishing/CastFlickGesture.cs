using System;
using UnityEngine;

// The "About Fishing"-style whip gesture that replaces the old hold-to-charge cast. While the
// player holds the aim button, this watches the mouse (and, on gamepad, the right stick) for a
// PULL in any direction — left, right, back or forward — followed by a fast PUSH back through
// center the opposite way. The throw power (0..1) is the DEPTH of the wind-up — how far back
// the pull reached before the flick — not the reversal speed: smoothed velocity is capped at
// roughly responsiveness × distance travelled, so it can't distinguish a hard flick from a soft
// one and reads near-constant on a stick (fixed engage→release geometry). Speed only gates
// validity: a reversal slower than minCastSpeed simply does not cast — the pull relaxes and the
// player can wind up again, with no failure state.
//
// Pure input math, no scene access: RodCasting feeds it deltas every frame and reads the pull
// pose back out for the procedural arm/rod animation (ProceduralCastArms).
//
// Units: the pull lives in a normalized 2D space (x = right, y = away from the player) whose
// magnitude is clamped to 1 at full pull. Speeds are in that space, per second — so a speed of
// 7 means "the hand crossed a whole full-pull distance in about 1/7th of a second".
public class CastFlickGesture
{
    [Serializable]
    public class Settings
    {
        [Header("Shared")]
        [Tooltip("Pull distance (0..1) at which a wind-up arms the CAST detector, and the zero-power point of the depth→power map (a full pull to 1 is full power). The pose follows the pull freely at any distance.")]
        [Range(0.1f, 0.9f)] public float pullEngageDistance = 0.35f;
        [Tooltip("Reversal speed (normalized pull units/sec) below which releasing the pull does NOT cast. Too slow = no throw, keep aiming. Power comes from the wind-up depth, not this speed.")]
        public float minCastSpeed = 3f;
        [Tooltip("How fast the mouse pull relaxes toward center (units/sec) once the hand is idle. Never fights live movement — any motion, however small, pauses it. (The stick self-centers on its own, so this is mouse-only.)")]
        public float idleReturnRate = 3f;
        [Tooltip("Mouse speed (normalized units/sec) below which the hand counts as idle. Movement above this — however slow — is read 1:1 and never eaten by the relaxation.")]
        public float idleSpeedThreshold = 0.25f;
        [Tooltip("Seconds the mouse must stay idle before the pull starts relaxing home, so tiny pauses mid-gesture don't tug the rod back.")]
        public float idleDelay = 0.12f;
        [Tooltip("Responsiveness of the smoothed gesture velocity (higher = twitchier speed reads). ~25 keeps single-frame mouse spikes from casting.")]
        public float velocityResponsiveness = 25f;

        [Header("Mouse")]
        [Tooltip("Mouse axis units → normalized pull. Higher = a shorter physical mouse drag reaches full pull.")]
        public float mouseSensitivity = 0.05f;

        [Header("Right Stick")]
        [Tooltip("Stick deflection at which the wind-up arms the CAST detector, and the zero-power point of the depth→power map (rim yank = full power, so keep this low enough to leave a soft-cast tilt range). The pose follows any deflection regardless. The marker lives on the LEFT stick, so the right stick belongs entirely to the whip.")]
        [Range(0.4f, 1f)] public float stickEngageMagnitude = 0.45f;
        [Tooltip("Stick reversal speed (deflection/sec) multiplier before the minCastSpeed validity gate, so pad flicks clear the same bar as the mouse.")]
        public float stickVelocityScale = 2.0f;
    }

    // --- mouse pull state ---
    private Vector2 mouseOffset;
    private Vector2 mouseVel;
    private bool mousePulled;
    private Vector2 mousePullDir;
    private float mouseIdleTime;
    private float mouseApex;   // deepest wind-up (along pull dir) this pull — the flick distance

    // --- stick pull state ---
    private Vector2 prevStick;
    private Vector2 stickVel;
    private bool stickEngaged;
    private Vector2 stickPullDir;
    private Vector2 lastStick;
    private float stickApex;   // deepest deflection (along pull dir) this wind-up

    /// <summary>Current combined pull (x = right, y = away), magnitude ≤ 1. Drives the rod/arm pose.</summary>
    public Vector2 Pull { get; private set; }

    /// <summary>True while either input is wound up past the engage ring.</summary>
    public bool IsPulled => mousePulled || stickEngaged;

    /// <summary>True while the right stick is captured by a wind-up, so the aim marker must ignore it.</summary>
    public bool StickEngaged => stickEngaged;

    public void Reset()
    {
        mouseOffset = Vector2.zero;
        mouseVel = Vector2.zero;
        mousePulled = false;
        mouseIdleTime = 0f;
        mouseApex = 0f;
        prevStick = Vector2.zero;
        stickVel = Vector2.zero;
        stickEngaged = false;
        lastStick = Vector2.zero;
        stickApex = 0f;
        Pull = Vector2.zero;
    }

    /// <summary>
    /// Feed one frame of input. Returns true when a valid whip fired this frame; power is the
    /// normalized throw strength and pushDir the (normalized) direction the push travelled in
    /// gesture space — the arms swing along it.
    /// </summary>
    public bool Update(float dt, Vector2 mouseDelta, Vector2 stick, Settings s, out float power, out Vector2 pushDir)
    {
        power = 0f;
        pushDir = Vector2.up;
        if (dt <= 0f) return false;

        bool threw = UpdateMouse(dt, mouseDelta, s, ref power, ref pushDir);
        if (UpdateStick(dt, stick, s, ref power, ref pushDir)) threw = true;

        // The pose pull follows ANY stick deflection — the engage yank only gates whether a
        // reversal can CAST. So the rod visibly winds with the lightest pressure, and springs
        // home when the stick re-centers (the stick returning to zero is the release).
        Pull = Vector2.ClampMagnitude(mouseOffset + lastStick, 1f);
        if (threw)
        {
            // Consume the wind-up so a canceled/next aim starts clean.
            mouseOffset = Vector2.zero;
            mousePulled = false;
            stickEngaged = false;
            Pull = Vector2.zero;
        }
        return threw;
    }

    private bool UpdateMouse(float dt, Vector2 mouseDelta, Settings s, ref float power, ref Vector2 pushDir)
    {
        Vector2 scaled = mouseDelta * s.mouseSensitivity;
        mouseOffset = Vector2.ClampMagnitude(mouseOffset + scaled, 1f);

        // Exponential smoothing keeps a single-frame spike from reading as a full-speed whip.
        Vector2 instantVel = scaled / dt;
        mouseVel = Vector2.Lerp(mouseVel, instantVel, 1f - Mathf.Exp(-dt * s.velocityResponsiveness));

        // Relax toward center ONLY while the hand is idle: any live movement — however small —
        // is read 1:1 and never fought by the decay; once the mouse has been still for idleDelay
        // the pull slides smoothly home. The decay itself can never register as a cast: cast
        // speed is read from mouseVel, which only reflects real mouse motion.
        if (mouseVel.magnitude > s.idleSpeedThreshold) mouseIdleTime = 0f;
        else mouseIdleTime += dt;
        if (mouseIdleTime >= s.idleDelay)
            mouseOffset = Vector2.MoveTowards(mouseOffset, Vector2.zero, s.idleReturnRate * dt);

        if (!mousePulled)
        {
            if (mouseOffset.magnitude >= s.pullEngageDistance)
            {
                mousePulled = true;
                mousePullDir = mouseOffset.normalized;
                mouseApex = mouseOffset.magnitude;
            }
            return false;
        }

        // The apex is the flick distance: it rises with active pulling, and follows the idle
        // decay back down so a deep pull that relaxed home can't inflate a tiny late flick.
        float depth = Vector2.Dot(mouseOffset, mousePullDir);
        mouseApex = mouseIdleTime >= s.idleDelay ? Mathf.Min(mouseApex, depth)
                                                 : Mathf.Max(mouseApex, depth);

        // Still wound up on the pull side: let a slow drag re-aim the wind-up direction.
        if (mouseOffset.magnitude >= s.pullEngageDistance && Vector2.Dot(mouseOffset, mousePullDir) > 0f)
        {
            mousePullDir = mouseOffset.normalized;
            return false;
        }

        // Crossed back inside the engage ring (or flipped straight through center in one fast
        // frame): this is the moment of truth — the push speed against the pull direction decides
        // WHETHER this was a flick; the wind-up depth decides how HARD the throw is.
        float speed = Vector2.Dot(mouseVel, -mousePullDir);
        mousePulled = false;
        if (speed < s.minCastSpeed) return false;

        power = Mathf.Clamp01(Mathf.InverseLerp(s.pullEngageDistance, 1f, mouseApex));
        pushDir = -mousePullDir;
        return true;
    }

    private bool UpdateStick(float dt, Vector2 stick, Settings s, ref float power, ref Vector2 pushDir)
    {
        lastStick = stick;
        Vector2 instantVel = (stick - prevStick) / dt;
        stickVel = Vector2.Lerp(stickVel, instantVel, 1f - Mathf.Exp(-dt * s.velocityResponsiveness));
        float mag = stick.magnitude;
        float prevMag = prevStick.magnitude;
        prevStick = stick;

        if (!stickEngaged)
        {
            // Any push out to the rim arms the wind-up (mirrors the mouse's engage distance);
            // whether a cast fires is decided purely by the reversal speed on the way back.
            if (mag >= s.stickEngageMagnitude)
            {
                stickEngaged = true;
                stickPullDir = stick.normalized;
                stickApex = mag;
            }
            return false;
        }

        // The deepest deflection reached is the flick distance (sticks self-center, so unlike the
        // mouse there is no idle decay to follow back down).
        stickApex = Mathf.Max(stickApex, Vector2.Dot(stick, stickPullDir));

        // Track the pull direction while the stick stays out on the pull side.
        if (mag >= s.stickEngageMagnitude * 0.8f && Vector2.Dot(stick, stickPullDir) > 0f)
        {
            stickPullDir = stick.normalized;
            return false;
        }

        bool released = mag < s.stickEngageMagnitude * 0.6f;
        bool flipped = Vector2.Dot(stick, stickPullDir) < 0f;
        if (!released && !flipped) return false;

        // Reversal speed decides WHETHER this was a flick; the wind-up depth decides the power —
        // half-tilt flick = soft cast, rim yank = full cast.
        float speed = Vector2.Dot(stickVel, -stickPullDir) * s.stickVelocityScale;
        stickEngaged = false;
        if (speed < s.minCastSpeed) return false;

        power = Mathf.Clamp01(Mathf.InverseLerp(s.stickEngageMagnitude, 1f, stickApex));
        pushDir = -stickPullDir;
        return true;
    }
}
