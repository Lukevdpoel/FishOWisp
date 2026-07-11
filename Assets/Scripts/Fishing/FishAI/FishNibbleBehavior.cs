using UnityEngine;

// The lead fish "working" a bobber. Instead of hovering in place and jabbing on a fixed timer, the
// fish circles the bobber and, on a relaxed random cadence, darts THROUGH and PAST it — a "nibble"
// that brushes the bobber for a quick little wobble, then carries on past and resumes circling.
// After a few passes it's ready to commit: it flags ReadyToBite and stops, and FishingZone hands it
// off to the same dash-and-grab the lure uses (a visible grab on the bobber + reaction window),
// rather than this helper teleporting a bite. The difference from a bite is a nibble swims past
// instead of clamping on.
//
// Owned by FishRipple: Begin() on entering Nibbling, Tick() each frame, Stop() when leaving
// (scared, caught, handed off to the bite). Self-driven — it moves the host transform directly.
public class FishNibbleBehavior
{
    public struct Settings
    {
        public float circleRadius;        // how far out the fish orbits the bobber
        public float circleSpeed;         // orbit cruise speed (m/s)
        public float dashSpeed;           // speed of a nibble dash (m/s)
        public float touchRadius;         // distance to the bobber that counts as a brush (wobble)
        public float passDistance;        // how far past the bobber a nibble dash carries before circling resumes
        public Vector2 nibbleGapRange;    // random seconds of circling between nibble dashes — keep it spaced/natural
        public Vector2 nibblesBeforeBite; // random count of nibble passes before the fish commits to the bite
        public float turnRate;            // deg/sec cap on how fast the heading can swing (anti-fold + natural arcs)
        public float radiusJitter;        // 0..1 fraction the orbit radius breathes in/out over time
        public float wanderStrength;      // how erratic the circling path is (0 = clean ring, >0 = milling)
        public float dashRise;            // floor for the dash's peak rise (meters above surface); the dash actually peaks at the bobber's height so it matches the bite
        public float snoutDepth;          // how far the silhouette's snout sits BELOW the host, so the dash can peak with the snout on the bobber exactly like the bite leap
        public LayerMask obstacleLayers;  // terrain/rock layers the circling/dashing fish must not swim into
        public float obstacleRayHeight;   // how high above the water surface the obstacle raycast starts
    }

    private readonly Transform host;
    private float waterSurfaceY;
    private Transform bobberTransform;
    private BobberController bobberCtrl;
    private FishPreset preset;
    private Settings settings;

    private enum Phase { Circling, Aiming, Dashing }
    private Phase phase;

    // While Circling the fish loiters: it alternates between RESTING (a near-still hover-drift) and
    // ARCING (a short slow lap), on a relaxed random cadence, so the circling reads as slow and
    // sporadic rather than a constant orbit.
    private enum Loiter { Resting, Arcing }
    private Loiter loiterState;
    private float loiterTimer;  // seconds left in the current rest/arc before the state flips

    private float orbitAngle;   // current angle around the bobber (radians)
    private float orbitDir;     // +1 / -1 — which way it laps
    private float gapTimer;     // seconds left circling before the next dash
    private int nibblesLeft;    // nibble passes left before the fish is ready to bite
    private float noiseSeed;    // per-fish offset into the Perlin field so each fish mills differently

    private Vector3 dashTarget; // the point past the bobber the current pass drives to
    private bool dashActed;     // the wobble already fired for this dash
    private bool finished;      // handed off / stopped — quit ticking
    private bool readyToBite;   // enough passes done: the zone should start the grab strike

    private float currentY;     // integrated swim height — eased toward the surface, or up toward the bobber mid-dash
    private float dashTimer;    // safety cap so a dash whose overshoot can't be reached drops back to circling instead of orbiting forever
    private Vector3 dashDir;    // the fish->bobber heading the dash drives along (used to detect when it has cleared the bobber)
    private float dashStartDist;// horizontal distance to the bobber at dash launch, so the rise can ramp by dash PROGRESS not proximity
    private float coastSpeed;   // post-dash glide speed; decays exponentially down to the loiter cruise so the dart eases off instead of braking
    private float aimTimer;     // safety cap on the line-up phase so a fish that can't align still eventually commits
    private float alignThresholdDeg; // how close the heading must point at the bobber before a dash is allowed to launch
    private bool aimingForBite; // true when the current line-up is the FINAL commit (flag the bite) rather than another tease dash
    private bool reachedLaunch; // true once the set-up has swung the fish out to its run-up distance, so it can turn in and commit

    // How fast (m/s) the swim height eases toward its target. Fast enough to complete the up-and-over
    // arc within a short dash, slow enough that the circle<->dash transitions don't pop.
    private const float RiseSpeed = 4f;

    // Exponential rate (per second) the post-dash glide bleeds its speed off — higher brakes sooner.
    private const float CoastDecay = 2f;

    // True while mid-dash, so FishRipple can crank the body sway for a visible lunge.
    public bool IsDashing => phase == Phase.Dashing;
    // The zone polls this on the nibbling lead to know when to launch the dash-and-grab bite.
    public bool ReadyToBite => readyToBite;

    public FishNibbleBehavior(Transform host)
    {
        this.host = host;
    }

    public void Begin(Transform bobber, float surfaceY, FishPreset preset, Settings settings)
    {
        bobberTransform = bobber;
        waterSurfaceY = surfaceY;
        this.preset = preset;
        this.settings = settings;
        bobberCtrl = bobber != null ? bobber.GetComponent<BobberController>() : null;

        // Only launch a dash once the heading points TIGHTLY at the bobber, so the speed-up is a
        // straight committed shot — never a fast turn-and-accelerate. Derived from the geometry: at
        // the orbit radius a heading off by asin(touchRadius/circleRadius) just grazes the touch
        // ring — take half of that so the dash is well inside it, clamped to a sane range.
        float graze = Mathf.Asin(Mathf.Clamp01(settings.touchRadius / Mathf.Max(0.2f, settings.circleRadius))) * Mathf.Rad2Deg;
        alignThresholdDeg = Mathf.Clamp(graze * 0.5f, 6f, 18f);

        finished = false;
        readyToBite = false;
        phase = Phase.Circling;
        currentY = host.position.y;
        orbitDir = Random.value < 0.5f ? 1f : -1f;
        nibblesLeft = Mathf.RoundToInt(Random.Range(settings.nibblesBeforeBite.x, settings.nibblesBeforeBite.y));
        gapTimer = Random.Range(settings.nibbleGapRange.x, settings.nibbleGapRange.y);
        noiseSeed = Random.value * 100f;
        loiterState = Loiter.Resting;
        loiterTimer = Random.Range(1f, 3f);
        coastSpeed = 0f;

        // Seed the orbit angle from where the fish currently sits relative to the bobber.
        if (bobber != null)
        {
            Vector3 fromBobber = host.position - bobber.position;
            orbitAngle = Mathf.Atan2(fromBobber.x, fromBobber.z);
        }
    }

    // Stop is idempotent — safe to call multiple times (scare, bite hand-off, despawn).
    public void Stop()
    {
        phase = Phase.Circling;
        dashActed = false;
        finished = true;
    }

    public void UnsubscribeAll() => Stop();

    public void SetWaterSurfaceY(float y) => waterSurfaceY = y;

    public void Tick()
    {
        if (finished || bobberTransform == null) return;

        Vector3 bobberFlat = bobberTransform.position;
        bobberFlat.y = waterSurfaceY;

        if (phase == Phase.Circling)
            TickCircle(bobberFlat);
        else if (phase == Phase.Aiming)
            TickAim(bobberFlat);
        else
            TickDash(bobberFlat);
    }

    // Loiter near the bobber: SLOW and SPORADIC. The fish alternates between resting (a bare
    // hover-drift) and short slow arcs around the bobber, on a relaxed random cadence. The orbit
    // radius is a LOOSE outward band, not a fixed ring, and any correction back toward it is gentle —
    // so the fish is never yanked back hard (the constant full-speed ring-chase is what made the old
    // circling look frantic). When the gap timer runs out it lines up and dashes; the dash is the
    // only quick movement.
    private void TickCircle(Vector3 bobberFlat)
    {
        // Coast out of the dash first: bleed the dart's speed off exponentially (dashSpeed -> loiter
        // cruise) in a straight forward glide, so the fish eases out of the dash instead of braking to
        // a near-stop the instant it ends. The glide owns the motion until it's spent.
        if (coastSpeed > settings.circleSpeed * 0.25f)
        {
            coastSpeed *= Mathf.Exp(-CoastDecay * Time.deltaTime);
            Vector3 glideFwd = host.forward; glideFwd.y = 0f;
            if (glideFwd.sqrMagnitude < 1e-4f) glideFwd = Vector3.forward;
            glideFwd.Normalize();
            MoveToward(host.position + glideFwd * 2f, coastSpeed, settings.turnRate * 0.5f, waterSurfaceY);

            gapTimer -= Time.deltaTime; // the glide counts toward the rest between passes
            return;                     // don't loiter or arm the next dash until the glide is spent
        }

        float baseRadius = Mathf.Max(0.2f, settings.circleRadius);
        float n = Time.time * 0.25f + noiseSeed;

        // Flip between rest and arc on a relaxed cadence: long rests, short arcs — the source of the
        // sporadic, stop-start feel.
        loiterTimer -= Time.deltaTime;
        if (loiterTimer <= 0f)
        {
            if (loiterState == Loiter.Resting)
            {
                loiterState = Loiter.Arcing;
                loiterTimer = Random.Range(0.7f, 1.8f);
                if (Random.value < 0.4f) orbitDir = -orbitDir;
            }
            else
            {
                loiterState = Loiter.Resting;
                loiterTimer = Random.Range(1.5f, 4f);
            }
        }

        float dist = FishMovementHelpers.GetHorizontalDistance(host.position, bobberFlat);
        Vector3 fwd = host.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();

        if (loiterState == Loiter.Resting)
        {
            // Hover: barely drift forward (fish never freeze). Only steer if it has wandered outside
            // the loiter band, and then only a soft nudge back toward it — no hard pull.
            Vector3 driftTarget = host.position + fwd * 1.5f;
            if (dist > baseRadius * 2f || dist < baseRadius * 0.7f)
            {
                Vector3 toward = bobberFlat - host.position; toward.y = 0f;
                if (toward.sqrMagnitude > 1e-4f) toward.Normalize();
                if (dist < baseRadius * 0.7f) toward = -toward; // too close: ease back outward
                driftTarget = host.position + (fwd + toward).normalized * 1.5f;
            }
            MoveToward(driftTarget, settings.circleSpeed * 0.18f, settings.turnRate * 0.4f, waterSurfaceY);
        }
        else
        {
            // A short, slow lazy arc around the bobber, within a loose OUTWARD band (~1.0..1.7x the
            // base radius) so the fish tends to hang out toward the far side of the band, not pinned in.
            float radius = baseRadius * (1f + Mathf.PerlinNoise(n, 0.3f) * 0.7f);
            float speedScale = 0.35f + 0.25f * Mathf.PerlinNoise(0.7f, n); // ~0.35..0.6 of cruise — slow
            float arcSpeed = settings.circleSpeed * speedScale;
            float angularSpeed = arcSpeed / Mathf.Max(0.2f, radius);
            orbitAngle += orbitDir * angularSpeed * Time.deltaTime;

            Vector3 radial = new Vector3(Mathf.Sin(orbitAngle), 0f, Mathf.Cos(orbitAngle));
            Vector3 ringPoint = bobberFlat + radial * radius;
            MoveToward(ringPoint, arcSpeed, settings.turnRate * 0.7f, waterSurfaceY);
        }

        gapTimer -= Time.deltaTime;
        if (gapTimer > 0f) return;

        // Whether this is the last pass (the actual bite commit) or another tease, LINE UP FIRST:
        // bank to face the bobber, then either dash through it or — on the final pass — flag the
        // bite. Committing straight from the tangential orbit heading is what made a dash skim wide,
        // and what made the bite LEAP with its back to the bobber and wheel around in mid-air.
        aimingForBite = nibblesLeft <= 0;
        phase = Phase.Aiming;
        aimTimer = 5f;          // the slower (loiter-paced) set-up needs more time to swing out + turn in
        reachedLaunch = false;
    }

    // Set-up phase, held at the LOITER'S cruising pace (NOT full cruise) so the fish doesn't visibly
    // speed up and telegraph the dash — the only acceleration is the dash itself. The fish first
    // swings OUT to a launch distance for a run-up ("take some distance"), then turns IN to face the
    // bobber and dashes/bites once lined up. A gentle turn keeps the swing-out and turn-in as moving
    // banks at this slow speed, not in-place pivots. A safety timer commits anyway so it can't stall.
    private void TickAim(Vector3 bobberFlat)
    {
        aimTimer -= Time.deltaTime;

        // Match the loiter cruise (~0.5x) so the whole set-up holds one consistent speed up to the dash.
        float setupSpeed = settings.circleSpeed * 0.5f;
        float setupTurn = settings.turnRate * 0.5f;

        // A real run-up — well beyond the loiter band. Clamped so even a tiny fish backs off a
        // meaningful distance and a huge fish doesn't swing halfway across the pond.
        float launchDistance = Mathf.Clamp(settings.circleRadius * 3f, 1.5f, 5f);
        float dist = FishMovementHelpers.GetHorizontalDistance(host.position, bobberFlat);

        // Swing out to the launch ring. The launch point sits outward AND a little further along the
        // lap, so the fish arcs out smoothly rather than reversing in place. Reserve the tail of the
        // timer for the turn-in so a fish still far out always gets to commit.
        if (!reachedLaunch)
        {
            if (dist < launchDistance && aimTimer > 1.2f)
            {
                Vector3 outDir = host.position - bobberFlat; outDir.y = 0f;
                if (outDir.sqrMagnitude < 1e-4f) outDir = host.forward;
                outDir.Normalize();
                Vector3 launchDir = Quaternion.AngleAxis(35f * orbitDir, Vector3.up) * outDir;
                MoveToward(bobberFlat + launchDir * launchDistance, setupSpeed, setupTurn, waterSurfaceY);
                return;
            }
            reachedLaunch = true;
        }

        // Turn in to face the bobber at the same set-up speed, then commit once the heading is on it.
        MoveToward(bobberFlat, setupSpeed, setupTurn, waterSurfaceY);

        Vector3 forward = host.forward; forward.y = 0f;
        Vector3 toBobber = bobberFlat - host.position; toBobber.y = 0f;
        if (forward.sqrMagnitude < 1e-4f || toBobber.sqrMagnitude < 1e-4f) return;

        if (Vector3.Angle(forward, toBobber) <= alignThresholdDeg || aimTimer <= 0f)
        {
            if (aimingForBite)
            {
                // Lined up on the bobber — hand the bite off NOW (the zone reads ReadyToBite and
                // launches the strike), so the leap drives straight at the bobber instead of
                // starting turned-away and wheeling around in the air.
                readyToBite = true;
                finished = true;
            }
            else
            {
                StartDash(bobberFlat);
            }
        }
    }

    // Aim the pass: overshoot to a point beyond the bobber so the fish swims clean through and out
    // the far side (the line from the fish toward the bobber passes through it, so it brushes it).
    private void StartDash(Vector3 bobberFlat)
    {
        phase = Phase.Dashing;
        dashActed = false;

        Vector3 toBobber = bobberFlat - host.position;
        toBobber.y = 0f;
        if (toBobber.sqrMagnitude < 0.0001f) toBobber = Vector3.forward;
        toBobber.Normalize();

        dashDir = toBobber;
        dashStartDist = FishMovementHelpers.GetHorizontalDistance(host.position, bobberFlat);
        dashTarget = bobberFlat + toBobber * Mathf.Max(0.2f, settings.passDistance);
        dashTarget.y = waterSurfaceY;

        // Safety cap: generous multiple of the straight-line dash time. If the overshoot point lands
        // inside the turning radius (or the fish just can't reach it), this ends the pass rather than
        // letting it orbit the bobber forever.
        float dashDist = FishMovementHelpers.GetHorizontalDistance(host.position, dashTarget);
        dashTimer = dashDist / Mathf.Max(0.1f, settings.dashSpeed) * 2.5f + 0.5f;
    }

    private void TickDash(Vector3 bobberFlat)
    {
        dashTimer -= Time.deltaTime;

        // Rise tied to dash PROGRESS, not proximity. Matched to the bite leap so the BUILDUP is
        // visually identical (the player can't read a tease early): reach full height by ~60% of the
        // run-up and HOLD it into the bobber on the approach, then — only AFTER the bobber, past the
        // tell — sink back to the surface by the time the dash ends (the whole down-arc happens in the
        // forward motion, never a drop after stopping). cleared<0 = approaching; cleared>0 = past it.
        float cleared = Vector3.Dot(host.position - bobberFlat, dashDir);
        float riseT = cleared < 0f
            ? (dashStartDist > 0.01f ? 1f - Mathf.Clamp01(-cleared / (dashStartDist * 0.6f)) : 1f)
            : 1f - Mathf.Clamp01(cleared / Mathf.Max(0.2f, settings.passDistance * 0.75f));
        // Peak the silhouette with its snout right at the bobber's real height — exactly what the bite
        // leap does (HostYToPlaceSnoutAt) — instead of a fixed offset, so the two read the same.
        float bobberRise = bobberTransform != null ? bobberTransform.position.y - waterSurfaceY : 0f;
        float peakRise = Mathf.Max(settings.dashRise, bobberRise + settings.snoutDepth);
        float desiredY = waterSurfaceY + peakRise * riseT;

        // The fish was already lined up before the dash launched, so this is a straight committed
        // shot — only the normal turn cap, just enough to track the overshoot point, never a whip.
        MoveToward(dashTarget, settings.dashSpeed, settings.turnRate, desiredY);

        // Brush: fire a slight wobble once, when the fish first clips the bobber on its way past.
        float toBobberDist = FishMovementHelpers.GetHorizontalDistance(host.position, bobberFlat);
        if (!dashActed && toBobberDist <= Mathf.Max(0.05f, settings.touchRadius))
        {
            dashActed = true;
            bobberCtrl?.PlayNibbleWobble(host.forward);
        }

        // Pass complete once the fish has driven clear past the bobber (projecting its travel onto
        // the dash heading — robust to any small lateral offset), OR the safety cap lapsed — either
        // way drop back into circling and re-arm.
        float clearedBobber = Vector3.Dot(host.position - bobberFlat, dashDir);
        if (clearedBobber >= settings.passDistance * 0.75f || dashTimer <= 0f)
        {
            nibblesLeft = Mathf.Max(0, nibblesLeft - 1);
            gapTimer = Random.Range(settings.nibbleGapRange.x, settings.nibbleGapRange.y);
            phase = Phase.Circling;

            // Settle after the dart: glide out bleeding speed off (coastSpeed), then rest, then resume
            // the slow loiter — dart, coast, rest, loiter, dart. Re-seed the orbit angle from the new
            // position so any arc resumes smoothly.
            coastSpeed = settings.dashSpeed;
            loiterState = Loiter.Resting;
            loiterTimer = Random.Range(1.5f, 4f);
            Vector3 fromBobber = host.position - bobberFlat;
            orbitAngle = Mathf.Atan2(fromBobber.x, fromBobber.z);
        }
    }

    // Head-first steering: turn the heading toward the target at a capped rate, then swim ALONG
    // that heading — never snap to face the target. A hard heading flip (which the old
    // MoveTowards + instant face caused on tight circling and the circle→dash→circle hand-offs)
    // makes the trailing body chain fold back through itself. Capping the turn keeps the
    // silhouette's body smooth through the orbit and the wheel-in on a dash.
    private void MoveToward(Vector3 target, float speed, float turnRate, float desiredY)
    {
        target.y = host.position.y;

        Vector3 forward = host.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 toTarget = target - host.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude > 1e-4f)
        {
            float signed = Vector3.SignedAngle(forward, toTarget.normalized, Vector3.up);
            float maxStep = Mathf.Max(turnRate, 1f) * Time.deltaTime;
            float step = Mathf.Clamp(signed, -maxStep, maxStep);
            forward = (Quaternion.AngleAxis(step, Vector3.up) * forward).normalized;
        }

        // Ease the swim height toward its target (surface while circling, up toward the bobber
        // mid-dash) so the vertical motion arcs smoothly instead of snapping.
        currentY = Mathf.MoveTowards(currentY, desiredY, RiseSpeed * Time.deltaTime);

        Vector3 next = host.position + forward * (speed * Time.deltaTime);
        next.y = currentY;

        // Unlike FishRipple.SteerToward (which every other state routes through), this loop had no
        // obstacle check at all — a bobber cast near shore/rocks could see the circling/dashing fish
        // driven straight into terrain. Hold position when the step is blocked; rotation still
        // updates above so the circle/aim/dash timers keep the fish turning until a clear step opens.
        if (settings.obstacleLayers == 0
            || !FishMovementHelpers.IsObstacleAt(next, settings.obstacleLayers, waterSurfaceY, settings.obstacleRayHeight))
        {
            host.position = next;
        }
        host.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }
}
