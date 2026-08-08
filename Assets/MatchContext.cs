using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The single shared "truth" about the match that every AI reads.
// Knows where the ball is and which team currently has possession.
public class MatchContext : MonoBehaviour
{
    public static MatchContext Instance { get; private set; }

    [SerializeField] private Rigidbody2D ball;
    [SerializeField] private TeamSide playerTeam; // your side
    [SerializeField] private TeamSide botTeam;    // the bots' side

    [Header("Ball handling")]
    [Tooltip("After a shot/pass/drop the ball can't be re-grabbed for this long, so it has time to travel.")]
    [SerializeField] private float releaseGrabDelay = 0.5f;
    [Tooltip("On an ordinary foul, an AI free-throw carrier holds the ball this long, then takes its normal decision (pass/shoot/dribble).")]
    [SerializeField] private float freeThrowAIHoldSeconds = 3f;
    [Tooltip("Swimmers can't cross the goal line: their x is clamped to ±this during live play (ball/keepers excluded).")]
    [SerializeField] private float playerLimitX = 6.9f;
    [Tooltip("Counterattack window: winning the ball in your own half starts a fast-break for this long.")]
    [SerializeField] private float counterWindowSeconds = 4f;
    [Tooltip("Exact-arrival distance used by the designated keeper's special OOB fetch branch. Field swimmers use the shared front pickup point below.")]
    [SerializeField, Range(0.01f, 0.2f)] private float outOfBoundsPickupDistance = 0.05f;
    [Tooltip("World-space radius around a field swimmer's front pickup point that counts as genuine loose-ball contact.")]
    [SerializeField, Range(0.05f, 0.35f)] private float looseBallPickupRadius = 0.18f;
    [Tooltip("World-space distance from a field swimmer's centre to the front/head/hand pickup point.")]
    [SerializeField, Range(0.1f, 0.8f)] private float pickupFrontOffset = 0.42f;
    [Tooltip("Seconds used to ease a contacted loose ball from its exact world pose into the existing hold pose.")]
    [SerializeField, Range(0.05f, 0.25f)] private float pickupTransitionDuration = 0.11f;

    // who currently holds the ball: null = loose
    public TeamSide PossessingTeam { get; private set; }

    // the team that last touched the ball (grab / steal / shoot / pass release) —
    // used by the out-of-bounds rule to award possession to the OTHER team.
    public TeamSide LastTouchTeam { get; private set; }

    // Non-null only when the MOST RECENT loose-ball physical touch was a goalkeeper. The
    // goal-line rule uses this distinction for a true keeper-deflection corner; ordinary
    // possession/releases and later field-player touches clear it.
    public TeamSide LastKeeperTouchTeam { get; private set; }

    // the last SWIMMER to release the ball (shot / pass / drop) — lets ScoreManager
    // credit a goal to a specific player (Centre-goal tracking for the bot's adaptive D).
    public Transform LastReleaser { get; private set; }
    public float LastReleaseUnscaledTime { get; private set; } = -10f;
    public void NoteRelease(Transform t)
    {
        if (t == null) return;
        LastReleaser = t;
        LastReleaseUnscaledTime = Time.unscaledTime;
    }

    // last time the ball was released (shot/passed/dropped); used for the grab cooldown
    private float lastReleaseTime = -10f;

    // One field swimmer may reserve a genuinely contacted loose ball while it eases into the
    // existing hand pose. Possession is assigned only when this very short secure transition
    // completes, but the reservation makes two same-frame pickups atomic.
    private Transform looseBallPickupClaimant;
    private Coroutine looseBallPickupRoutine;
    private const float FastLooseBallSpeed = 2.5f;
    private const float FastLooseBallRadiusMultiplier = 0.65f;

    // While true, all swimmers freeze (sprint-duel line-up/race, goal settle, etc).
    public bool PlayFrozen { get; private set; }

    // A team banned from grabbing the loose ball until the OTHER team touches it
    // (shot-clock turnover). null = no ban.
    public TeamSide GrabBannedTeam { get; private set; }

    // A live out-of-bounds restart is a loose ball reserved for the awarded team. Play is not
    // frozen: its selected fetcher swims to the placement while the offending team is kept away.
    public bool OutOfBoundsRestartActive { get; private set; }
    public TeamSide OutOfBoundsRestartTeam { get; private set; }
    public TeamSide OutOfBoundsOffendingTeam { get; private set; }
    public Vector2 OutOfBoundsRestartPoint { get; private set; }
    public Transform OutOfBoundsFetcher { get; private set; }
    public bool OutOfBoundsRestartReady { get; private set; }
    private Collider2D[] outOfBoundsBallColliders;
    private readonly List<Collider2D> outOfBoundsIgnoredColliders = new List<Collider2D>();

    // After a kickoff (duel win / goal restart) the carrying team's AI center makes one
    // pass back to its deepest teammate before normal play. Cleared on possession change.
    public bool KickoffPassPending { get; private set; }
    public TeamSide KickoffPassTeam { get; private set; }
    public float KickoffPassTime { get; private set; } // when the kickoff possession began

    // Free throw (ordinary foul): the fouled carrier is protected from steals and the
    // shot clock pauses until they act / move / the AI hold elapses.
    public bool FreeThrowActive { get; private set; }
    public Transform FreeThrowCarrier { get; private set; }
    public float FreeThrowStartTime { get; private set; }
    public float FreeThrowAIHoldSeconds => freeThrowAIHoldSeconds;

    // Keeper hold (Part 1): a keeper collecting the ball is NOT a possession change — the
    // shot clock keeps ticking for the holding team until the keeper distributes.
    public bool KeeperHolding { get; private set; }
    public TeamSide KeeperHoldTeam { get; private set; }

    // Counterattack window (Part 2): a team that just won the ball in its own half.
    public TeamSide CounterTeam { get; private set; }
    public float CounterUntilTime { get; private set; }

    // Has anyone taken possession since the last reset (game start / goal / sprint duel)?
    // Flips false on a reset and true on the first grab; CameraFollow holds the wide pool
    // overview shot until it turns true, then resumes following the active player (Task 1).
    public bool BallTouchedSinceReset { get; private set; }
    public void ResetBallTouch() { BallTouchedSinceReset = false; }

    // Force the camera out of the wide overview when play resumes after a GOAL restart:
    // there the conceding team is given the ball WHILE play is frozen (so the normal
    // first-grab trigger already fired and was reset for the pause), so on un-freeze we
    // re-arm the flag by hand. Pure camera cue — does not touch possession (Task 3).
    public void MarkBallTouched() { BallTouchedSinceReset = true; }

    // Where the ball ACTUALLY is. While the ball is held it is parented and NOT simulated —
    // and a non-simulated Rigidbody2D's pose FREEZES at its last physics position instead of
    // tracking the transform. Reading rb.position then anchors every follower (the camera's
    // keeper-carry anchor, AI defensive shapes, steal reach) to the stale CATCH point — the
    // "camera stops following the advancing keeper" bug. The transform is authoritative while
    // held; the rigidbody is authoritative while simulated (loose/flying).
    public Vector2 BallPosition => ball == null ? Vector2.zero
        : ball.simulated ? ball.position : (Vector2)ball.transform.position;
    public Rigidbody2D Ball => ball;
    public TeamSide PlayerTeam => playerTeam;
    public TeamSide BotTeam => botTeam;
    public float PlayerLimitX => playerLimitX;

    void Awake()
    {
        Instance = this;
        lastReleaseTime = -10f; // allow an immediate grab at kickoff
        LastReleaseUnscaledTime = -10f;
    }

    // called by a player/bot when it grabs (team) or releases (null) the ball
    public void SetPossession(TeamSide team)
    {
        // A rules system may deliberately assign/release the ball while a field pickup is in its
        // short presentation transition. That assignment owns the ball and cancels the claim.
        // Normal pickup completion clears its claim immediately before calling SetPossession.
        if (looseBallPickupClaimant != null)
            CancelLooseBallPickup(team == null);

        TeamSide prev = PossessingTeam;
        TeamSide prevTouch = LastTouchTeam; // who last touched it BEFORE this grab/release

        // remember the last toucher: a grab/steal = the new team; a release (null) = the
        // team that just let go (read the OLD possessor before overwriting it).
        if (team != null) LastTouchTeam = team;
        else if (prev != null) LastTouchTeam = prev;
        if (team != null || prev != null) LastKeeperTouchTeam = null;

        PossessingTeam = team;
        if (team != null) BallTouchedSinceReset = true;      // first grab → camera leaves the overview shot
        if (team == null) lastReleaseTime = Time.time;       // ball was just released → start the cooldown
        else if (OutOfBoundsRestartActive || team != GrabBannedTeam)
            ClearGrabBan(); // the awarded/other team got it → lift the restart/turnover ban

        // a pending kickoff pass is void once possession leaves the kicking team
        if (KickoffPassPending && team != KickoffPassTeam) ClearKickoffPass();

        // any possession change / release ends a free throw (carrier passed/shot/dropped)
        ClearFreeThrow();

        // counterattack: a real WIN (ball last touched by the OTHER team) inside our own
        // half starts a fast break — NOT a same-team pass reception.
        if (team != null && prevTouch != null && prevTouch != team && !PlayFrozen && !KeeperHolding &&
            ball != null && team.defendGoal != null)
        {
            float sign = Mathf.Sign(team.defendGoal.position.x);
            if (sign * ball.position.x > 0f) StartCounter(team);
        }
    }

    // ---- match-flow gates ----

    public void FreezeAll() { PlayFrozen = true; }
    public void Unfreeze()  { PlayFrozen = false; }

    public void SetGrabBan(TeamSide team)
    {
        EndOutOfBoundsRestart();
        GrabBannedTeam = team;
    }

    public void ClearGrabBan()
    {
        EndOutOfBoundsRestart();
        GrabBannedTeam = null;
    }

    // A team may grab unless it is the one serving a turnover ban.
    public bool CanGrab(TeamSide team) => GrabBannedTeam == null || team != GrabBannedTeam;

    public void BeginOutOfBoundsRestart(TeamSide awardedTeam, TeamSide offendingTeam,
                                        Vector2 restartPoint, Transform preferredFetcher)
    {
        EndOutOfBoundsRestart();
        OutOfBoundsRestartActive = awardedTeam != null;
        OutOfBoundsRestartTeam = awardedTeam;
        OutOfBoundsOffendingTeam = offendingTeam;
        OutOfBoundsRestartPoint = restartPoint;
        OutOfBoundsFetcher = preferredFetcher;
        OutOfBoundsRestartReady = false;
        GrabBannedTeam = offendingTeam;

        if (!OutOfBoundsRestartActive || ball == null || offendingTeam == null) return;
        outOfBoundsBallColliders = ball.GetComponentsInChildren<Collider2D>(true);
        IgnoreTeamBallCollisions(offendingTeam);
    }

    // Used by field AI and goalkeeper AI so only one teammate spends stamina fetching the restart.
    public bool IsOutOfBoundsFetcher(Transform swimmer)
        => OutOfBoundsRestartActive && swimmer != null && swimmer == OutOfBoundsFetcher;

    public bool IsAtOutOfBoundsBall(Vector2 swimmerPosition)
        => !OutOfBoundsRestartActive ||
           (OutOfBoundsRestartReady &&
            Vector2.Distance(swimmerPosition, OutOfBoundsRestartPoint) <= outOfBoundsPickupDistance);

    public void MarkOutOfBoundsRestartReady()
    {
        if (OutOfBoundsRestartActive) OutOfBoundsRestartReady = true;
    }

    void IgnoreTeamBallCollisions(TeamSide team)
    {
        if (team != null && team.members != null)
            foreach (Transform member in team.members) IgnoreSwimmerBallCollisions(member);

        foreach (Goalkeeper keeper in Object.FindObjectsByType<Goalkeeper>())
            if (keeper != null && keeper.DefendingTeam == team)
                IgnoreSwimmerBallCollisions(keeper.transform);
    }

    void IgnoreSwimmerBallCollisions(Transform swimmer)
    {
        if (swimmer == null || outOfBoundsBallColliders == null) return;
        foreach (Collider2D swimmerCollider in swimmer.GetComponentsInChildren<Collider2D>())
        {
            if (swimmerCollider == null || swimmerCollider.attachedRigidbody == ball ||
                outOfBoundsIgnoredColliders.Contains(swimmerCollider)) continue;
            outOfBoundsIgnoredColliders.Add(swimmerCollider);
            foreach (Collider2D ballCollider in outOfBoundsBallColliders)
                if (ballCollider != null && ballCollider != swimmerCollider)
                    Physics2D.IgnoreCollision(ballCollider, swimmerCollider, true);
        }
    }

    void EndOutOfBoundsRestart()
    {
        if (outOfBoundsBallColliders != null)
            foreach (Collider2D ballCollider in outOfBoundsBallColliders)
                foreach (Collider2D swimmerCollider in outOfBoundsIgnoredColliders)
                    if (ballCollider != null && swimmerCollider != null && ballCollider != swimmerCollider)
                        Physics2D.IgnoreCollision(ballCollider, swimmerCollider, false);

        outOfBoundsIgnoredColliders.Clear();
        outOfBoundsBallColliders = null;
        OutOfBoundsRestartActive = false;
        OutOfBoundsRestartTeam = null;
        OutOfBoundsOffendingTeam = null;
        OutOfBoundsFetcher = null;
        OutOfBoundsRestartReady = false;
        GrabBannedTeam = null;
    }

    bool RestartIgnores(Collider2D swimmerCollider)
        => OutOfBoundsRestartActive && swimmerCollider != null &&
           outOfBoundsIgnoredColliders.Contains(swimmerCollider);

    public void SetKickoffPass(TeamSide team)
    {
        KickoffPassPending = true;
        KickoffPassTeam = team;
        KickoffPassTime = Time.time;
    }

    public void ClearKickoffPass()
    {
        KickoffPassPending = false;
        KickoffPassTeam = null;
    }

    public void StartFreeThrow(Transform carrier)
    {
        FreeThrowActive = true;
        FreeThrowCarrier = carrier;
        FreeThrowStartTime = Time.time;
    }

    public void ClearFreeThrow()
    {
        FreeThrowActive = false;
        FreeThrowCarrier = null;
    }

    // ---- post-foul protection (2026-07-09f) ----
    // After an ordinary foul the fouled carrier gets a real-time window in which NOBODY may
    // steal from them (real water polo's uncontested free-throw beat). Deliberately separate
    // from FreeThrowActive, which ends the instant the carrier acts/moves — this window
    // persists so the fouled player genuinely gets time to hold or pass. It lapses early the
    // moment they release the ball: IsFoulProtected requires them to STILL be the carrier,
    // so protection never transfers to a receiver and never shields a re-stolen ball.
    public Transform FoulProtectedCarrier { get; private set; }
    public float FoulProtectionUntil { get; private set; }
    public float FoulProtectionIdleUntil { get; private set; }
    private Vector2 foulProtectionStartPosition;
    private bool foulProtectionMovementSeen;
    private const float FoulProtectionMovementThreshold = 0.2f;

    public void StartFoulProtection(Transform carrier, float seconds, float idleSeconds)
    {
        FoulProtectedCarrier = carrier;
        FoulProtectionUntil = Time.time + Mathf.Max(0f, seconds);
        FoulProtectionIdleUntil = Time.time + Mathf.Clamp(idleSeconds, 0f, Mathf.Max(0f, seconds));
        foulProtectionStartPosition = carrier != null ? (Vector2)carrier.position : Vector2.zero;
        foulProtectionMovementSeen = false;
    }

    public bool IsFoulProtected(Transform carrier)
    {
        RefreshFoulProtection();
        return carrier != null && carrier == FoulProtectedCarrier;
    }

    void Update()
    {
        RefreshFoulProtection();
    }

    private void RefreshFoulProtection()
    {
        if (FoulProtectedCarrier == null) return;

        if (ball == null || ball.transform.parent != FoulProtectedCarrier ||
            Time.time >= FoulProtectionUntil)
        {
            ClearFoulProtection();
            return;
        }

        if (!foulProtectionMovementSeen &&
            Vector2.Distance(FoulProtectedCarrier.position, foulProtectionStartPosition) >=
            FoulProtectionMovementThreshold)
            foulProtectionMovementSeen = true;

        if (!foulProtectionMovementSeen && Time.time >= FoulProtectionIdleUntil)
            ClearFoulProtection();
    }

    private void ClearFoulProtection()
    {
        FoulProtectedCarrier = null;
        FoulProtectionUntil = 0f;
        FoulProtectionIdleUntil = 0f;
        foulProtectionMovementSeen = false;
    }

    // ---- keeper hold (Part 1) ----
    public void SetKeeperHold(TeamSide team) { KeeperHolding = true; KeeperHoldTeam = team; }
    public void ClearKeeperHold() { KeeperHolding = false; KeeperHoldTeam = null; }

    // Steal rule (Task 5): a goalkeeper carrying the ball can't be robbed WHILE it stays in its
    // safe zone (within 1.5u of its goal line). The moment it carries the ball out of that zone it
    // becomes fair game for the rest of the possession (Goalkeeper.LeftSafeZone). Returns true only
    // while `carrier` is a keeper that is STILL protected — the three steal paths skip those.
    public bool IsProtectedKeeper(Transform carrier)
    {
        if (carrier == null) return false;
        Goalkeeper gk = carrier.GetComponent<Goalkeeper>();
        return gk != null && !gk.LeftSafeZone;
    }

    // ---- release self-collision window ----
    // At the instant a held ball is un-parented and re-simulated it sits at the hand offset —
    // inside or touching the releaser's own collider for many release angles (worst case: a
    // pass aimed back across the body). Left alone, the physics depenetration deflects or
    // weakens the throw. Every release path (shot / pass / drop, human / bot / keeper) calls
    // this to ignore ball↔releaser contacts briefly; re-enabling additionally waits until the
    // two have actually separated, so a ball dropped at the feet never gets popped away.
    private const float ReleaseSelfCollisionSeconds = 0.3f;  // clears a slow lob across the body
    private const float ReleaseSelfCollisionMaxExtra = 1.5f; // still-touching extension cap

    public void IgnoreReleaseCollision(Transform releaser)
    {
        if (releaser == null || ball == null) return;
        Collider2D ballCol = ball.GetComponent<Collider2D>();
        if (ballCol == null) return;
        // Every release path calls this BEFORE un-parenting, so the held BALL is still a child
        // of the releaser here — GetComponentsInChildren returns the ball's own collider too.
        // It must be filtered out: comparing the ball against itself threw ArgumentException in
        // Physics2D.Distance ("Cannot calculate the distance between the same collider").
        Collider2D[] found = releaser.GetComponentsInChildren<Collider2D>();
        int n = 0;
        for (int i = 0; i < found.Length; i++)
            if (found[i] != null && found[i] != ballCol && found[i].attachedRigidbody != ball)
                found[n++] = found[i];
        if (n == 0) return;
        Collider2D[] own = new Collider2D[n];
        System.Array.Copy(found, own, n);
        StartCoroutine(ReleaseCollisionWindow(ballCol, own));
    }

    IEnumerator ReleaseCollisionWindow(Collider2D ballCol, Collider2D[] own)
    {
        foreach (Collider2D c in own)
            if (c != null && c != ballCol) Physics2D.IgnoreCollision(ballCol, c, true);

        yield return new WaitForSeconds(ReleaseSelfCollisionSeconds);

        // Still overlapping (a drop at the feet, a fully-blocked throw)? Extend briefly rather
        // than re-enabling mid-overlap — that would fire the very depenetration impulse this
        // window exists to prevent.
        float deadline = Time.time + ReleaseSelfCollisionMaxExtra;
        bool touching = true;
        while (touching && Time.time < deadline && ballCol != null)
        {
            touching = false;
            // enabled checks: a high-ball launch (BallFlight) disables the ball's colliders
            // mid-flight — Physics2D.Distance throws ArgumentException on a disabled collider.
            foreach (Collider2D c in own)
                if (c != null && c != ballCol && c.enabled && ballCol.enabled &&
                    Physics2D.Distance(ballCol, c).distance < 0.05f)
                { touching = true; break; }
            if (touching) yield return null;
        }

        foreach (Collider2D c in own)
            if (c != null && ballCol != null && c != ballCol && !RestartIgnores(c))
                Physics2D.IgnoreCollision(ballCol, c, false);
    }

    // ---- counterattack window (Part 2) ----
    public void StartCounter(TeamSide team) { CounterTeam = team; CounterUntilTime = Time.time + counterWindowSeconds; }
    public bool CounterActiveFor(TeamSide team) => team != null && team == CounterTeam && Time.time < CounterUntilTime;

    // Physical-touch attribution (used by the out-of-bounds rules so a deflection off an
    // opponent is credited to them). Does NOT change possession.
    public void NoteTouch(TeamSide team)
    {
        if (team == null) return;
        LastTouchTeam = team;
        LastKeeperTouchTeam = null;
    }

    // A physical goalkeeper contact while the ball remains loose. This is deliberately
    // separate from a keeper catch/distribution so only a real deflection can create a corner.
    public void NoteKeeperTouch(TeamSide team)
    {
        if (team == null) return;
        LastTouchTeam = team;
        LastKeeperTouchTeam = team;
    }

    public bool TeamHasBall(TeamSide team) => PossessingTeam == team;
    public bool BallIsLoose => PossessingTeam == null;

    // Hard ceiling on the post-release no-grab window, regardless of how releaseGrabDelay is tuned
    // in the Inspector — so a mis-set (multi-second) value can never "permanently" lock the releaser
    // out of their own loose ball. The window is purely elapsed-time and always expires.
    private const float MaxReleaseGrabDelay = 1f;

    // Loose AND past the post-release cooldown AND not flying overhead → safe for ANYONE
    // (including the releaser) to collect. The cooldown is what stops a shooter/teammate from
    // instantly snatching back a shot or pass; it is time-based, so at most MaxReleaseGrabDelay
    // seconds after release it expires and the same player can pick their own loose ball back up.
    // A HIGH BALL (arcing overhead, BallFlight) is untouchable for its whole flight — grabs,
    // steals, keeper saves and the goal-line loose rule all read this and wait for it to land.
    public bool BallGrabbable =>
        PossessingTeam == null &&
        looseBallPickupClaimant == null &&
        (!OutOfBoundsRestartActive || OutOfBoundsRestartReady) &&
        (Time.time - lastReleaseTime) >= Mathf.Min(releaseGrabDelay, MaxReleaseGrabDelay) &&
        (BallFlight.Instance == null || !BallFlight.Instance.HighBallActive);

    // Geometry + legality shared by human control, both field-AI wrappers, landed receptions,
    // live field-player restarts and the sprint duel. The point is in FRONT of the swimmer, not
    // at its root, so the swimmer reaches the ball rather than collecting from a large centre
    // circle. A fast loose ball gets an even smaller intersection zone.
    public bool CanBeginLooseBallPickup(Transform holder, TeamSide team, Vector2 facing)
    {
        if (ball == null || holder == null || team == null || !holder.gameObject.activeInHierarchy)
            return false;
        if (!BallGrabbable || !CanGrab(team) || ball.transform.parent != null || !ball.simulated)
            return false;
        BallFlight flight = BallFlight.Instance;
        if (flight != null && flight.SkipActive && !flight.SkipBounced)
            return false; // the low skip remains untouchable by field swimmers until its bounce

        float radius = looseBallPickupRadius;
        if (ball.linearVelocity.magnitude > FastLooseBallSpeed)
            radius *= FastLooseBallRadiusMultiplier;
        return LooseBallPickupContactDistance(holder, facing) <= radius ||
               HasPhysicalLooseBallContact(holder);
    }

    // A pass may land exactly under a swimmer while BallFlight temporarily ignores collision
    // response. The front point should remain the normal catch, but genuine collider overlap must
    // also count so an already-touching ball cannot become stranded at the swimmer's centre.
    bool HasPhysicalLooseBallContact(Transform holder)
    {
        Collider2D ballCollider = ball != null ? ball.GetComponent<Collider2D>() : null;
        Collider2D swimmerCollider = holder != null ? holder.GetComponent<Collider2D>() : null;
        if (ballCollider == null || swimmerCollider == null ||
            !ballCollider.enabled || !swimmerCollider.enabled)
            return false;

        ColliderDistance2D separation = Physics2D.Distance(ballCollider, swimmerCollider);
        return separation.isOverlapped || separation.distance <= 0.01f;
    }

    public float LooseBallPickupContactDistance(Transform holder, Vector2 facing)
    {
        if (ball == null || holder == null) return float.PositiveInfinity;
        Vector2 direction = facing.sqrMagnitude > 0.0001f
            ? facing.normalized
            : BallPosition - (Vector2)holder.position;
        if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;
        Vector2 pickupPoint = (Vector2)holder.position + direction.normalized * pickupFrontOffset;
        return Vector2.Distance(pickupPoint, BallPosition);
    }

    // Atomically reserve a contacted field ball, stop it at its exact current pose, and ease it
    // into the caller's LIVE existing hand/hold pose. The completion callback then invokes the
    // established human/AI possession method; forced GiveBallTo paths intentionally bypass this.
    public bool TryBeginLooseBallPickup(Transform holder, TeamSide team, Vector2 facing,
                                        System.Func<Vector2> liveHoldWorldPosition,
                                        System.Action completeExistingGrab)
    {
        if (liveHoldWorldPosition == null || completeExistingGrab == null ||
            !CanBeginLooseBallPickup(holder, team, facing))
            return false;

        looseBallPickupClaimant = holder;
        looseBallPickupRoutine = StartCoroutine(SecureLooseBall(
            holder, liveHoldWorldPosition, completeExistingGrab));
        return true;
    }

    IEnumerator SecureLooseBall(Transform holder, System.Func<Vector2> liveHoldWorldPosition,
                                 System.Action completeExistingGrab)
    {
        Vector3 start = ball.transform.position;
        float z = start.z;
        ball.transform.SetParent(null);
        ball.simulated = false;
        ball.linearVelocity = Vector2.zero;
        ball.angularVelocity = 0f;

        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, pickupTransitionDuration);
        while (elapsed < duration)
        {
            if (holder == null || ball == null || looseBallPickupClaimant != holder)
            {
                if (looseBallPickupClaimant == holder) looseBallPickupClaimant = null;
                looseBallPickupRoutine = null;
                if (ball != null && ball.transform.parent == null && PossessingTeam == null)
                    ball.simulated = true;
                yield break;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t); // smooth acceleration and deceleration
            Vector2 target = liveHoldWorldPosition();
            Vector2 world = Vector2.Lerp((Vector2)start, target, eased);
            ball.transform.position = new Vector3(world.x, world.y, z);
            yield return null;
        }

        if (holder == null || ball == null || looseBallPickupClaimant != holder)
        {
            if (looseBallPickupClaimant == holder) looseBallPickupClaimant = null;
            looseBallPickupRoutine = null;
            if (ball != null && ball.transform.parent == null && PossessingTeam == null)
                ball.simulated = true;
            yield break;
        }

        Vector2 finalTarget = liveHoldWorldPosition();
        ball.transform.position = new Vector3(finalTarget.x, finalTarget.y, z);
        looseBallPickupClaimant = null;
        looseBallPickupRoutine = null;
        completeExistingGrab();
    }

    void CancelLooseBallPickup(bool restoreLoosePhysics)
    {
        Coroutine running = looseBallPickupRoutine;
        looseBallPickupRoutine = null;
        looseBallPickupClaimant = null;
        if (running != null) StopCoroutine(running);

        if (restoreLoosePhysics && ball != null && ball.transform.parent == null &&
            PossessingTeam == null)
            ball.simulated = true;
    }

    // given a team, returns the other team
    public TeamSide EnemyOf(TeamSide team)
    {
        if (team == playerTeam) return botTeam;
        if (team == botTeam) return playerTeam;
        return null;
    }

    // Force whoever currently holds the ball to drop it in place (shot-clock turnover,
    // exclusion, etc.). Reuses the same release path the player/AI use so there's one
    // consistent way the ball comes loose.
    public void ForceDropHeldBall()
    {
        if (ball == null) return;

        Transform carrier = ball.transform.parent;
        if (carrier == null) { SetPossession(null); return; }

        IAgentBody body = carrier.GetComponent<IAgentBody>();
        if (body != null) body.IsHolding = false;

        PlayerMovement pm = carrier.GetComponent<PlayerMovement>();
        if (pm != null) { pm.ReleaseBall(); return; } // detaches the ball + clears possession

        // pure AI body: detach manually
        ball.transform.SetParent(null);
        ball.simulated = true;
        ball.linearVelocity = Vector2.zero;
        SetPossession(null);
    }

    // Hand the ball to a specific holder (sprint-duel winner, kickoff centre). Reuses the
    // player/AI hold mechanics so the ball is parented and possession set consistently.
    public void GiveBallTo(Transform holder, TeamSide team)
    {
        if (ball == null || holder == null) return;

        // Rules assignments (penalty / goal restart) stay immediate and supersede any incidental
        // in-progress field pickup. The sprint duel now reaches this only after its contact ease.
        if (looseBallPickupClaimant != null) CancelLooseBallPickup(false);

        PlayerMovement pm = holder.GetComponent<PlayerMovement>();
        if (pm != null) { pm.TakeOverHeldBall(); return; } // parents ball + sets PlayerTeam possession + isHolding

        IAgentBody body = holder.GetComponent<IAgentBody>();
        ball.simulated = false;
        ball.linearVelocity = Vector2.zero;
        ball.transform.SetParent(holder);
        Vector2 dir = body != null ? body.LastDirection : Vector2.right;
        float off = body != null ? body.HoldOffset : 0.6f;
        ball.transform.localPosition = (Vector3)(dir * off);
        if (body != null) body.IsHolding = true;
        SetPossession(team);
    }

    // Halftime: swap both teams' attack/defend goals so they attack the opposite ends.
    // Keepers (bound to their own physical goal transform) are unaffected.
    public void SwapEnds()
    {
        SwapGoals(playerTeam);
        SwapGoals(botTeam);
        if (ExclusionManager.Instance != null) ExclusionManager.Instance.OnEndsSwapped();
    }

    static void SwapGoals(TeamSide t)
    {
        if (t == null) return;
        Transform tmp = t.attackGoal;
        t.attackGoal = t.defendGoal;
        t.defendGoal = tmp;
    }
}
