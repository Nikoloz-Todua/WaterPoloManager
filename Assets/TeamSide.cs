using UnityEngine;

// One per team. Owns the team's goals + roster and all the spatial reasoning
// the AI needs (where to support, where to defend, who's open to pass to).
// Everything here is roster-size agnostic, so it scales from 2v2 to 6v6.
public class TeamSide : MonoBehaviour
{
    public string teamName = "Team";
    public Transform attackGoal;
    public Transform defendGoal;
    public Transform[] members;

    [Header("Formation (auto-spreads players)")]
    public float halfWidth = 6f;     // how wide the pool half is (x spread)
    public float laneHeight = 3.5f;  // how tall the spread is (y spread)
    public float attackPush = 3f;    // how far forward players push when attacking
    public float defendPull = 2.5f;  // how far back they sit when defending

    [Header("AI tuning")]
    public float supportLead = 3f;       // how far ahead of the carrier support players push
    public float supportWidth = 2.5f;    // lateral spread of supporting players
    public float defendDepth = 2.5f;     // how far off our own goal defenders sit
    public float defendWidth = 2.5f;     // lateral spread of defenders
    public float pressDistance = 1.8f;   // the carrier feels "pressured" with an enemy this close
    public float openRadius = 1.6f;      // a teammate is "open" if no enemy is within this
    public float passLaneRadius = 0.7f;  // a pass lane is blocked if an enemy is this close to it
    public float shotLaneRadius = 0.6f;  // a shot lane is blocked if an enemy is this close to it
    public float aimCornerOffset = 1.1f; // aim this far off goal-centre to beat the keeper
    public float forwardPassMin = 0.5f;  // min forward progress required to pass when not pressured
    public float freeThrowClearance = 2.2f; // enemies must back this far off a free-throw taker

    [Header("Attacking spacing")]
    public float teammateSpacing = 2.0f;     // attackers closer than this to a nearer-ball mate get pushed to their lane
    public float supportPassRange = 5.0f;    // an off-ball attacker only offers a support outlet within this of the carrier
    public float supportBlend = 0.5f;        // how far an open, in-range attacker blends role-spot → support (0..1)
    public float passOpennessWeight = 1.5f;  // how strongly the carrier prefers WIDE-open pass targets (e.g. wings)
    public float formationAnchorStrength = 0.7f; // how strongly support players are pulled back to their role spot (0 = free drift, 1 = glued)
    public float formationAnchorRadius = 1.5f;   // hard cap: a support player's target never strays further than this from its role spot

    [Header("Tactics")]
    public float centerFeedWeight = 3f;      // extra pass score for feeding an open, deep Centre (inside)
    public float counterRunners = 2f;        // how many advanced players sprint on a counterattack
    public float dropSag = 0.5f;             // help defender's sag blend toward the centre, 0..1 (Drop mode)
    public float shotQualityThreshold = 0.3f; // min shot-quality (0..1) to take the shot, else pass (lower = bots shoot more)
    public float passShotQualityWeight = 1.2f; // pass-score bonus per unit of the RECEIVER's shot quality
    public float spacingPushMult = 1.4f;       // how hard crowded attackers get pushed apart
    public float wideLateralMult = 1.2f;       // width multiplier on non-Centre attack lanes (full pool width)
    public float wingWideBias = 0.2f;          // extra lateral drift for the weak-side wing

    [Header("Adaptive defense (AI team only)")]
    public float defenseReevalInterval = 4f;      // seconds between defense-mode re-evaluations
    public float defenseHysteresisSeconds = 1.5f; // min time between unforced mode changes

    // Runtime adaptive-defense state — no Inspector wiring needed.
    [System.NonSerialized] public bool isAI;                   // true for the bot team (auto-set in Start)
    [System.NonSerialized] public int goalsConcededFromCenter; // goals WE conceded to the enemy Centre
    [System.NonSerialized] private float nextDefenseReevalTime;
    [System.NonSerialized] private float lastDefenseModeChangeTime;

    // Cached on first use (keepers persist for the match). Picked by CURRENT defendGoal side
    // each call so it stays correct across a halftime ends-swap. No Inspector wiring needed.
    [System.NonSerialized] private Goalkeeper[] keepersCache;

    // Fixed tactical roles, assigned by slot index in `members` (0..5).
    public enum Role { Center, CenterBack, LeftWing, RightWing, LeftFlat, RightFlat }

    // Defensive scheme. Press = 1-to-1 marking; Zone = goal-side spread; Drop = help
    // defense (front the centre, sag a helper); MPress = press with one centre dropper.
    public enum DefenseMode { Press, Zone, Drop, MPress }
    [System.NonSerialized] public DefenseMode defenseMode = DefenseMode.Press;

    void Start()
    {
        // The bot team evaluates its own defense; auto-detected so no Inspector wiring.
        isAI = MatchContext.Instance != null && MatchContext.Instance.BotTeam == this;
    }

    public Transform ClosestMemberTo(Vector2 point)
    {
        Transform best = null;
        float bestDist = Mathf.Infinity;
        if (members == null) return null;
        foreach (Transform m in members)
        {
            if (m == null) continue;
            float d = Vector2.Distance(point, m.position);
            if (d < bestDist) { bestDist = d; best = m; }
        }
        return best;
    }

    // true if `t` currently sits in this team's roster (excluded slots are null → false)
    public bool Contains(Transform t)
    {
        if (t == null || members == null) return false;
        foreach (Transform m in members) if (m == t) return true;
        return false;
    }

    // Inside the attacking 2m zone = close to the goal `attackingTeam` is attacking.
    public static bool IsInsideTwoMeter(Transform player, TeamSide attackingTeam)
    {
        return player != null && attackingTeam != null && attackingTeam.attackGoal != null &&
               Vector2.Distance(player.position, attackingTeam.attackGoal.position) < 2.5f;
    }

    // returns the role index of a given member (its slot in the formation)
    public int RoleIndexOf(Transform member)
    {
        if (members == null) return 0;
        for (int i = 0; i < members.Length; i++)
            if (members[i] == member) return i;
        return 0;
    }

    // Maps a member's slot index to its tactical role. Rosters smaller than 6 just
    // use the low indices (no crash on 2 or 4 players); extras clamp to the last role.
    public Role RoleOf(Transform member)
    {
        int idx = RoleIndexOf(member);
        return (Role)Mathf.Clamp(idx, 0, 5);
    }

    // role spread in [-1, 1] so players fan out across the pool by their slot
    float RoleSpread(Transform member)
    {
        int count = (members != null && members.Length > 0) ? members.Length : 1;
        int idx = RoleIndexOf(member);
        return count > 1 ? ((float)idx / (count - 1)) * 2f - 1f : 0f;
    }

    // "forward" axis: from our own goal toward the enemy goal
    Vector2 Forward()
    {
        if (attackGoal == null || defendGoal == null) return Vector2.right;
        return ((Vector2)attackGoal.position - (Vector2)defendGoal.position).normalized;
    }

    // ---- attacking support: get open, ahead of the carrier, in space ----
    public Vector2 SupportSpot(Transform me, Vector2 ballPos, TeamSide enemy)
    {
        if (attackGoal == null || defendGoal == null) return ballPos;

        Vector2 fwd = Forward();
        Vector2 across = new Vector2(-fwd.y, fwd.x);

        Transform carrierT = ClosestMemberTo(ballPos);     // the ball sits on the carrier
        Vector2 carrier = carrierT != null ? (Vector2)carrierT.position : ballPos;

        Vector2 basePos = carrier + fwd * supportLead;      // push ahead toward goal
        float t = RoleSpread(me);
        if (Mathf.Abs(t) < 0.01f) t = 1f;                   // 2-player teams: pick a side

        // offer two lanes and take whichever is more open
        Vector2 spot = basePos + across * (t * supportWidth);
        Vector2 alt = basePos - across * (t * supportWidth);
        if (NearestDistance(alt, enemy) > NearestDistance(spot, enemy) + 0.5f) spot = alt;

        return ClampToField(spot);
    }

    // ---- defending: sit goal-side of the ball in a spread line ----
    public Vector2 DefendSpot(Transform me, Vector2 ballPos)
    {
        if (defendGoal == null) return ballPos;

        Vector2 g = defendGoal.position;
        Vector2 toBall = ballPos - g;
        Vector2 dir = toBall.sqrMagnitude > 1e-3f ? toBall.normalized : Forward();
        Vector2 basePos = g + dir * defendDepth;            // between our goal and the ball

        Vector2 across = new Vector2(-Forward().y, Forward().x);
        float t = RoleSpread(me);

        return ClampToField(basePos + across * (t * defendWidth));
    }

    // Snap every member to a spread "home" shape on our own side (reuses DefendSpot
    // with the ball at centre), fanned out by role so nobody overlaps. Also zeroes
    // each member's velocity. Works for any roster size. Called on goal/kickoff.
    public void SnapToKickoffFormation()
    {
        if (members == null) return;
        foreach (Transform m in members)
        {
            if (m == null) continue;

            Vector2 spot = DefendSpot(m, Vector2.zero); // ball is at centre at kickoff
            m.position = new Vector3(spot.x, spot.y, m.position.z);

            Rigidbody2D body = m.GetComponent<Rigidbody2D>();
            if (body != null) body.linearVelocity = Vector2.zero;
        }
    }

    // A NATURAL restart spot inside our OWN half — used by the goal restart (Task 2) and the
    // quarter-start sprint duel (Task 1). Each role takes a DISTINCT depth + lane so the team
    // looks spread out, never a rigid line across the goal. Two shapes:
    //   hasBall = true  : the attacking restart — Centre pushed up near centre court (the taker;
    //                     ScoreManager pulls it onto exact centre), mates spread behind in our half.
    //   hasBall = false : a sat-back DEFENSIVE spread (goal-side, varied depth) — the conceding
    //                     team's opponents, and both teams' off-sprinters during the duel.
    // Always stays in our half (depth <= ~0.92 of the half toward centre).
    public Vector2 RestartFormationSpot(Transform member, bool hasBall)
    {
        if (attackGoal == null || defendGoal == null)
            return member != null ? (Vector2)member.position : Vector2.zero;

        Vector2 fwd = Forward();                            // own goal -> enemy goal (toward centre)
        Vector2 across = new Vector2(-fwd.y, fwd.x);
        Vector2 ownGoal = defendGoal.position;
        Vector2 centre = ((Vector2)attackGoal.position + ownGoal) * 0.5f;
        float halfSpan = Vector2.Distance(centre, ownGoal); // depth of our half (~7)

        float depthFrac;  // 0 = own goal, 1 = the centre line
        float lateral;    // signed lane, in laneHeight units
        if (hasBall)
        {
            switch (RoleOf(member)) // attacking restart: spread up into our half behind the taker
            {
                case Role.Center:     depthFrac = 0.92f; lateral =  0.0f; break; // up near centre (the taker)
                case Role.LeftWing:   depthFrac = 0.70f; lateral =  0.9f; break;
                case Role.RightWing:  depthFrac = 0.70f; lateral = -0.9f; break;
                case Role.LeftFlat:   depthFrac = 0.55f; lateral =  0.5f; break;
                case Role.RightFlat:  depthFrac = 0.55f; lateral = -0.5f; break;
                case Role.CenterBack: depthFrac = 0.35f; lateral =  0.0f; break; // anchors at the back
                default:              depthFrac = 0.55f; lateral =  0.0f; break;
            }
        }
        else
        {
            switch (RoleOf(member)) // defensive spread: sat back goal-side, varied depth (not a line)
            {
                case Role.Center:     depthFrac = 0.45f; lateral =  0.0f; break; // top defender fronts the hole
                case Role.LeftWing:   depthFrac = 0.40f; lateral =  0.85f; break;
                case Role.RightWing:  depthFrac = 0.40f; lateral = -0.85f; break;
                case Role.LeftFlat:   depthFrac = 0.28f; lateral =  0.45f; break;
                case Role.RightFlat:  depthFrac = 0.28f; lateral = -0.45f; break;
                case Role.CenterBack: depthFrac = 0.18f; lateral =  0.0f; break; // deep anchor near goal
                default:              depthFrac = 0.30f; lateral =  0.0f; break;
            }
        }

        Vector2 spot = ownGoal + fwd * (depthFrac * halfSpan) + across * (lateral * laneHeight);
        return ClampToField(spot);
    }

    // Snap every member into the natural restart spread (RestartFormationSpot) and zero its
    // velocity. Used by the goal restart so the pool looks like play is about to resume, not
    // like a whole new match (Task 2). Roster-size agnostic; excluded (null) slots skipped.
    public void SnapToRestartFormation(bool hasBall)
    {
        if (members == null) return;
        foreach (Transform m in members)
        {
            if (m == null) continue;
            Vector2 spot = RestartFormationSpot(m, hasBall);
            m.position = new Vector3(spot.x, spot.y, m.position.z);
            Rigidbody2D body = m.GetComponent<Rigidbody2D>();
            if (body != null) body.linearVelocity = Vector2.zero;
        }
    }

    // ---- attacking: role-based spot (distinct depth + lateral per role) ----
    // The carrier is handled by the brain's Carry(); only non-carriers use this.
    // The CENTRE is dynamic (fights for inside water at 2m, goal-side of its guard);
    // every other role holds a wide fixed lane so the team uses the full pool width.
    public Vector2 AttackPositionFor(Transform me, Vector2 ballPos, TeamSide enemy = null)
    {
        if (attackGoal == null || defendGoal == null) return ballPos;

        Role role = RoleOf(me);
        if (role == Role.Center) return CenterInsideSpot(enemy); // dynamic inside fight (Feature 4)

        Vector2 fwd = Forward();                              // own goal → enemy goal
        Vector2 across = new Vector2(-fwd.y, fwd.x);
        Vector2 ownGoal = defendGoal.position;
        float span = Vector2.Distance(attackGoal.position, defendGoal.position);

        float depthFrac;   // 0 = our goal, 1 = enemy goal
        float lateral;     // signed offset along `across`, in laneHeight units
        switch (role)
        {
            case Role.LeftWing:   depthFrac = 0.68f; lateral =  0.9f; break; // forward + wide
            case Role.RightWing:  depthFrac = 0.68f; lateral = -0.9f; break;
            case Role.LeftFlat:   depthFrac = 0.52f; lateral =  0.5f; break; // mid-depth ~5-meter
            case Role.RightFlat:  depthFrac = 0.52f; lateral = -0.5f; break;
            case Role.CenterBack: depthFrac = 0.30f; lateral =  0.0f; break; // stays back
            default:              depthFrac = 0.50f; lateral =  0.0f; break;
        }

        lateral *= wideLateralMult; // push the lanes wider — use the full pool width

        // ball on one side → the WEAK-SIDE wing drifts even wider (a far-post outlet)
        if (role == Role.LeftWing || role == Role.RightWing)
        {
            float ballAcross = Vector2.Dot(ballPos - ownGoal, across);
            if (Mathf.Abs(ballAcross) > 0.5f && Mathf.Sign(ballAcross) != Mathf.Sign(lateral))
                lateral += Mathf.Sign(lateral) * wingWideBias;
        }

        Vector2 spot = ownGoal + fwd * (depthFrac * span) + across * (lateral * laneHeight);
        return ClampToField(spot);
    }

    // Dynamic Centre positioning (Feature 4): fight for inside water at the 2m point.
    // If a defender guards that point, take the GOAL SIDE of him; otherwise sit on the
    // point itself. Y stays near the goal mouth.
    public Vector2 CenterInsideSpot(TeamSide enemy)
    {
        if (attackGoal == null || defendGoal == null) return Vector2.zero;
        Vector2 goalPos = attackGoal.position;
        Vector2 goalDir = ((Vector2)goalPos - (Vector2)defendGoal.position).normalized;
        Vector2 twoM = goalPos - goalDir * 2.0f; // 2 meters in front of the enemy goal

        // the guard = the enemy defender actually near the 2m point (usually the centre-back)
        Transform guard = null;
        float best = 3f;
        if (enemy != null && enemy.members != null)
        {
            foreach (Transform e in enemy.members)
            {
                if (e == null) continue;
                float d = Vector2.Distance(e.position, twoM);
                if (d < best) { best = d; guard = e; }
            }
        }

        Vector2 target;
        if (guard != null)
        {
            Vector2 fromGoal = (Vector2)guard.position - goalPos;
            Vector2 dir = fromGoal.sqrMagnitude > 1e-4f ? fromGoal.normalized : -goalDir;
            target = goalPos + dir * 1.2f; // inside water: goal-side of the guard
        }
        else target = twoM;

        target.y = Mathf.Clamp(target.y, goalPos.y - 2.5f, goalPos.y + 2.5f); // near the mouth
        return ClampToField(target);
    }

    // ---- attacking off-ball target: hold the role shape (width), only drift toward a
    // support outlet when genuinely useful, then enforce minimum spacing so attackers
    // don't bunch on the ball. Used by the brain's attacking branch (carrier excluded).
    public Vector2 AttackTarget(Transform me, Vector2 ballPos, TeamSide enemy)
    {
        if (me == null) return ballPos;
        Vector2 roleSpot = AttackPositionFor(me, ballPos, enemy); // role shape = primary
        Vector2 spot = roleSpot;

        // the Centre (hole set) fights for inside water regardless — its role spot IS
        // dynamic and ball-independent, so no support-blend / spacing / anchor dilution
        if (RoleOf(me) == Role.Center) return spot;

        // blend toward support ONLY if this attacker is genuinely useful: open AND within
        // a reasonable pass range of the carrier (otherwise everyone would crowd the ball).
        Transform carrier = ClosestMemberTo(ballPos); // the ball sits on the carrier
        if (carrier != null && carrier != me)
        {
            bool open = NearestDistance(me.position, enemy) >= openRadius;
            bool inRange = Vector2.Distance(me.position, carrier.position) <= supportPassRange;
            if (open && inRange)
                spot = Vector2.Lerp(spot, SupportSpot(me, ballPos, enemy), Mathf.Clamp01(supportBlend));
        }

        spot = ApplyTeammateSpacing(me, spot, ballPos);

        // FORMATION ANCHOR (anti-cluster): pull the drifted target back toward the role
        // spot, then hard-cap the stray so support players HOLD the formation instead of
        // collapsing onto the ball. 0 strength = old free drift, 1 = glued to the spot.
        spot = Vector2.Lerp(spot, roleSpot, Mathf.Clamp01(formationAnchorStrength));
        Vector2 stray = spot - roleSpot;
        if (stray.magnitude > formationAnchorRadius)
            spot = roleSpot + stray.normalized * formationAnchorRadius;

        return ClampToField(spot);
    }

    // If a teammate that is CLOSER to the ball crowds me (within teammateSpacing), slide
    // my target laterally away from them (toward my own lane) to keep the team wide.
    Vector2 ApplyTeammateSpacing(Transform me, Vector2 target, Vector2 ballPos)
    {
        if (members == null) return target;

        Vector2 across = new Vector2(-Forward().y, Forward().x);
        float laneSign = RoleLateralSign(me);
        float myBallDist = Vector2.Distance(me.position, ballPos);

        foreach (Transform t in members)
        {
            if (t == null || t == me) continue;
            if (Vector2.Distance(t.position, ballPos) >= myBallDist) continue; // only nearer-the-ball mates
            float d = Vector2.Distance(t.position, me.position);
            if (d >= teammateSpacing) continue;

            float side = Mathf.Sign(Vector2.Dot((Vector2)me.position - (Vector2)t.position, across));
            if (Mathf.Abs(side) < 0.01f) side = laneSign == 0f ? 1f : laneSign; // tie → my lane
            target += across * (side * (teammateSpacing - d) * spacingPushMult);
        }
        return target;
    }

    // Sign (+1 / -1 / 0) of a role's lateral lane, for spacing tie-breaks.
    float RoleLateralSign(Transform me)
    {
        switch (RoleOf(me))
        {
            case Role.LeftWing:
            case Role.LeftFlat:  return 1f;
            case Role.RightWing:
            case Role.RightFlat: return -1f;
            default:             return 0f; // Center / CenterBack
        }
    }

    // ---- 1-to-1 marking ----
    // Pair my slot to the SAME slot on the enemy team (role-vs-role). null if none.
    public Transform MarkAssignmentFor(Transform me, TeamSide enemy)
    {
        if (enemy == null || enemy.members == null) return null;
        int i = RoleIndexOf(me);
        if (i < 0 || i >= enemy.members.Length) return null;
        return enemy.members[i]; // may itself be null → caller falls back to DefendSpot
    }

    // A goal-side spot on the line from our defendGoal to the marked man: sit
    // ~defendDepth goal-side of him, never inside our net nor past the man.
    public Vector2 MarkSpot(Transform me, Transform target)
    {
        if (defendGoal == null || target == null)
            return DefendSpot(me, target != null ? (Vector2)target.position : Vector2.zero);

        Vector2 g = defendGoal.position;
        Vector2 toTarget = (Vector2)target.position - g;
        float dist = toTarget.magnitude;
        Vector2 dir = dist > 1e-3f ? toTarget / dist : Forward();

        float fromGoal = dist - defendDepth;                 // defendDepth goal-side of the man
        if (fromGoal < defendDepth) fromGoal = Mathf.Min(defendDepth, dist); // floor, never past him

        return ClampToField(g + dir * fromGoal);
    }

    // How dangerous `attacker` (a member of enemyOfDefender) is to OUR defendGoal.
    // Higher = mark first. `this` is the DEFENDING team.
    public float ThreatScore(Transform attacker, Vector2 ballPos, TeamSide enemyOfDefender)
    {
        if (attacker == null || defendGoal == null) return 0f;

        // base: the closer to our goal, the more dangerous
        float goalDist = Vector2.Distance(attacker.position, defendGoal.position);
        float score = 10f / (goalDist + 1f);

        // the ball carrier is the prime threat
        if (enemyOfDefender != null && enemyOfDefender.ClosestMemberTo(ballPos) == attacker)
            score += 5f;

        // an open attacker (no defender of ours nearby) is a bigger threat
        if (NearestDistance(attacker.position, this) > openRadius)
            score += 2f;

        return score;
    }

    // Aim at the goal corner away from the shooter. The keeper tracks the ball's
    // y, so the corner opposite the shooter is the open one.
    public Vector2 ShotAimPoint(Vector2 from)
    {
        if (attackGoal == null) return from;
        Vector2 c = attackGoal.position;
        float aimY = c.y + (from.y >= c.y ? -aimCornerOffset : aimCornerOffset);
        return new Vector2(c.x, aimY);
    }

    // This team's OWN goalkeeper (the keeper defending OUR goal), or null. Resolved by the
    // current defendGoal side so it stays right after a halftime swap. Used ONLY as a
    // heavily-penalised last-resort pass outlet (Task 4).
    Transform OwnKeeper()
    {
        if (defendGoal == null) return null;
        if (keepersCache == null) keepersCache = FindObjectsByType<Goalkeeper>(FindObjectsSortMode.None);
        float side = Mathf.Sign(defendGoal.position.x);
        foreach (Goalkeeper gk in keepersCache)
            if (gk != null && Mathf.Sign(gk.transform.position.x) == side) return gk.transform;
        return null;
    }

    // Best open teammate to pass to. Prefers forward progress; when the carrier is
    // pressured it will accept any open mate (incl. lateral/back) to keep possession.
    // Returns null when keeping the ball is better.
    public Transform BestPassTarget(Transform carrier, TeamSide enemy, bool pressured)
    {
        if (members == null || attackGoal == null || carrier == null) return null;

        const float KeeperLastResortOpenness = 0.2f; // a teammate this un-open counts as "covered" (Task 4)

        Vector2 goal = attackGoal.position;
        float carrierGoalDist = Vector2.Distance(carrier.position, goal);
        float span = defendGoal != null ? Vector2.Distance(goal, defendGoal.position) : 14f;

        Transform best = null;
        float bestScore = float.NegativeInfinity;
        bool allOthersCovered = true; // is EVERY field teammate smothered? (gates the keeper outlet)

        foreach (Transform mate in members)
        {
            if (mate == null || mate == carrier) continue;

            // must be open (not tightly marked)
            float openness = NearestDistance(mate.position, enemy);
            if (openness >= KeeperLastResortOpenness) allOthersCovered = false;
            if (openness < openRadius) continue;

            // PASS RISK: longer passes give defenders more time to step into the lane, so
            // the danger radius grows with the pass distance.
            float passDist = Vector2.Distance(carrier.position, mate.position);
            float laneR = passLaneRadius + passDist * 0.05f;
            if (!LaneClear(carrier.position, mate.position, enemy, laneR)) continue;

            float mateGoalDist = Vector2.Distance(mate.position, goal);
            float forwardGain = carrierGoalDist - mateGoalDist;

            // when not under pressure, only pass if it actually advances the ball
            if (!pressured && forwardGain < forwardPassMin) continue;

            // prefer forward + (heavily) open + closer to goal → favours wide-open wings;
            // the receiver's own look at goal rewards passes that IMPROVE shot quality
            float score = forwardGain * 1.5f + openness * passOpennessWeight - mateGoalDist * 0.1f
                        + ShotQuality(mate.position, enemy) * passShotQualityWeight;

            // CENTER FEED: strongly prefer an open, deep Centre (the inside pass to 2m)
            if (RoleOf(mate) == Role.Center && mateGoalDist < span * 0.32f)
                score += centerFeedWeight;

            // Centre with INSIDE WATER (deep + open) = the prime feed (Feature 4)
            if (RoleOf(mate) == Role.Center && mateGoalDist < 2.5f && openness > openRadius)
                score += centerFeedWeight * 2f;

            if (score > bestScore) { bestScore = score; best = mate; }
        }

        // GOALKEEPER as a heavily-penalised LAST RESORT (Task 4): only when EVERY field
        // teammate is smothered (openness < 0.2) — i.e. no real outlet exists — and even then
        // scored at 10% so it can never outrank a genuine pass. Net effect: the keeper almost
        // never receives a pass. Applies to BOTH teams' AI (this is the shared pass selector;
        // the keeper's own PassOut excludes itself via the keeper != carrier check below).
        if (allOthersCovered)
        {
            Transform keeper = OwnKeeper();
            if (keeper != null && keeper != carrier)
            {
                float kOpen = NearestDistance(keeper.position, enemy);
                float kDist = Vector2.Distance(carrier.position, keeper.position);
                if (LaneClear(carrier.position, keeper.position, enemy, passLaneRadius + kDist * 0.05f))
                {
                    float kGoalDist = Vector2.Distance(keeper.position, goal);
                    float kForward = carrierGoalDist - kGoalDist; // negative — the keeper is behind us
                    float kScore = (kForward * 1.5f + kOpen * passOpennessWeight - kGoalDist * 0.1f) * 0.1f;
                    if (kScore > bestScore) { bestScore = kScore; best = keeper; }
                }
            }
        }

        return best;
    }

    // true if no enemy sits within `radius` of the segment a→b (line of sight)
    public bool LaneClear(Vector2 a, Vector2 b, TeamSide enemy, float radius)
    {
        if (enemy == null || enemy.members == null) return true;
        foreach (Transform e in enemy.members)
        {
            if (e == null) continue;
            if (DistancePointToSegment(e.position, a, b) < radius) return false;
        }
        return true;
    }

    // The available teammate (excluding `carrier`) nearest OUR defended goal — the
    // "deepest" outlet for a kickoff pass. Excluded members are null here so they're
    // skipped automatically (picks the next deepest). null if no valid teammate.
    public Transform DeepestMember(Transform carrier)
    {
        if (members == null || defendGoal == null) return null;
        Vector2 g = defendGoal.position;
        Transform best = null;
        float bestDist = Mathf.Infinity;
        foreach (Transform m in members)
        {
            if (m == null || m == carrier) continue;
            float d = Vector2.Distance(m.position, g);
            if (d < bestDist) { bestDist = d; best = m; }
        }
        return best;
    }

    // distance from the nearest member of `team` to a point (Infinity if none)
    public static float NearestDistance(Vector2 point, TeamSide team)
    {
        if (team == null || team.members == null) return Mathf.Infinity;
        float best = Mathf.Infinity;
        foreach (Transform m in team.members)
        {
            if (m == null) continue;
            float d = Vector2.Distance(point, m.position);
            if (d < best) best = d;
        }
        return best;
    }

    static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f);
        t = Mathf.Clamp01(t);
        return Vector2.Distance(p, a + ab * t);
    }

    // keep a target spot inside the pool and out of the goals
    Vector2 ClampToField(Vector2 p)
    {
        if (attackGoal == null || defendGoal == null) return p;
        float minX = Mathf.Min(attackGoal.position.x, defendGoal.position.x);
        float maxX = Mathf.Max(attackGoal.position.x, defendGoal.position.x);
        float midY = (attackGoal.position.y + defendGoal.position.y) * 0.5f;
        float halfY = Mathf.Min(laneHeight + 0.5f, 4.0f); // let the wings use the full width
        p.x = Mathf.Clamp(p.x, minX + 0.5f, maxX - 0.5f);
        p.y = Mathf.Clamp(p.y, midY - halfY, midY + halfY);
        return p;
    }

    // ================= Drives & picks (Features 1–2) =================

    // Where a drive aims: the 2m point in front of the enemy goal, bent no more than
    // ±1.5 off the driver's current y so the path is a diagonal cut, not a U-turn.
    public Vector2 DrivePoint(Vector2 from)
    {
        if (attackGoal == null || defendGoal == null) return from;
        Vector2 p = (Vector2)attackGoal.position - Forward() * 2.0f;
        p.y = Mathf.Clamp(p.y, from.y - 1.5f, from.y + 1.5f);
        return ClampToField(p);
    }

    // Where a screener plants: just to the side of the carrier's marker, on the side
    // the carrier wants to drive toward (the goal side).
    public Vector2 GetScreenSpot(Vector2 markerPos, Vector2 carrierDir, float screenDistance = 1.2f)
    {
        Vector2 dir = carrierDir.sqrMagnitude > 1e-4f ? carrierDir.normalized : Forward();
        Vector2 perp = new Vector2(-dir.y, dir.x);
        Vector2 sideA = markerPos + perp * (screenDistance * 0.7f);
        Vector2 sideB = markerPos - perp * (screenDistance * 0.7f);
        if (attackGoal == null) return ClampToField(sideA);
        Vector2 g = attackGoal.position;
        return ClampToField(Vector2.Distance(sideA, g) <= Vector2.Distance(sideB, g) ? sideA : sideB);
    }

    // The teammate nominated to screen for the carrier: the one closest to the
    // carrier's marker (within 3), wings/flats preferred — their lanes naturally sit
    // near a perimeter carrier. Null when the carrier isn't marked or nobody is near.
    public Transform FindScreenerForCarrier(Transform carrier, TeamSide enemy)
    {
        if (carrier == null || enemy == null || members == null) return null;

        Transform marker = enemy.ClosestMemberTo(carrier.position);
        if (marker == null || Vector2.Distance(marker.position, carrier.position) > 2f) return null;

        Transform best = null;
        float bestScore = float.PositiveInfinity;
        foreach (Transform m in members)
        {
            if (m == null || m == carrier) continue;
            float d = Vector2.Distance(m.position, marker.position);
            if (d > 3f) continue;
            Role r = RoleOf(m);
            bool natural = r == Role.LeftWing || r == Role.RightWing ||
                           r == Role.LeftFlat || r == Role.RightFlat;
            float score = d - (natural ? 1f : 0f); // wings/flats get priority
            if (score < bestScore) { bestScore = score; best = m; }
        }
        return best;
    }

    // ================= Adaptive defense (Feature 3, AI team only) =================

    // Re-pick the defense mode situationally. Only the AI team runs this (the human
    // cycles modes with Z). Gated by a re-eval interval + hysteresis so it can't flap;
    // man-up/man-down switches are forced through immediately.
    public void EvaluateDefenseMode()
    {
        if (!isAI) return;
        if (Time.time < nextDefenseReevalTime) return;

        MatchContext ctx = MatchContext.Instance;
        ExclusionManager ex = ExclusionManager.Instance;
        TeamSide enemy = ctx != null ? ctx.EnemyOf(this) : null;

        bool manDown = ex != null && ex.ExcludedCount(this) > 0;
        bool manUp = ex != null && enemy != null && ex.ExcludedCount(enemy) > 0;

        int myScore = 0, theirScore = 0;
        if (ScoreManager.Instance != null && ctx != null)
        {
            bool weArePlayer = this == ctx.PlayerTeam;
            myScore = weArePlayer ? ScoreManager.Instance.HomeScore : ScoreManager.Instance.AwayScore;
            theirScore = weArePlayer ? ScoreManager.Instance.AwayScore : ScoreManager.Instance.HomeScore;
        }
        float remaining = MatchTimer.Instance != null ? MatchTimer.Instance.RemainingSeconds()
                                                      : float.MaxValue;

        DefenseMode desired;
        if (manDown) desired = DefenseMode.Drop;                                      // protect the cage
        else if (remaining < 30f && myScore > theirScore) desired = DefenseMode.Drop; // sit on the lead
        else if (goalsConcededFromCenter >= 2) desired = DefenseMode.Drop;            // their Centre hurts us
        else if (manUp) desired = DefenseMode.Press;                                  // squeeze the extra man
        else if (myScore < theirScore && remaining > 30f) desired = DefenseMode.Press;// chase the game
        else desired = DefenseMode.Press;                                             // default

        nextDefenseReevalTime = Time.time + defenseReevalInterval;
        if (desired == defenseMode) return;

        bool forced = manDown || manUp;
        if (!forced && Time.time - lastDefenseModeChangeTime < defenseHysteresisSeconds) return;

        defenseMode = desired;
        lastDefenseModeChangeTime = Time.time;
        if (EventFeed.Instance != null)
            EventFeed.Instance.AddEvent("Bot defense - " + desired.ToString().ToUpper());
    }

    // ================= Tactical shapes & decisions =================

    // Shot quality (Part 4): distance + angle + lane + pressure, in [0,1].
    public float ShotQuality(Vector2 from, TeamSide enemy)
    {
        if (attackGoal == null) return 0f;
        Vector2 goal = attackGoal.position;
        float dist = Vector2.Distance(from, goal);
        float distScore = Mathf.Clamp01(1f - (dist - 2f) / 6f);      // best ~2m out, fades by ~8m
        float angleScore = Mathf.Clamp01(1f - Mathf.Abs(from.y - goal.y) / 4f); // off-centre = worse angle
        float laneMult = LaneClear(from, ShotAimPoint(from), enemy, shotLaneRadius) ? 1f : 0.3f;
        float pressScore = Mathf.Clamp01(NearestDistance(from, enemy) / Mathf.Max(pressDistance, 0.01f));
        // angle and a clear lane GATE the shot (multiply); distance + pressure scale it.
        return angleScore * laneMult * (distScore * 0.7f + pressScore * 0.3f);
    }

    // Central help spot just in front of our own goal (Drop/MPress dropper sits here).
    public Vector2 CenterZoneSpot(Vector2 ballPos)
    {
        if (defendGoal == null) return ballPos;
        Vector2 g = defendGoal.position;
        Vector2 spot = g + Forward() * (defendDepth * 1.3f);
        float midY = attackGoal != null ? (g.y + attackGoal.position.y) * 0.5f : g.y;
        spot.y = Mathf.Lerp(midY, ballPos.y, 0.3f);
        return ClampToField(spot);
    }

    // Stand on the BALL side of a target to deny the entry feed (front the centre).
    public Vector2 FrontSpot(Transform target, Vector2 ballPos)
    {
        if (target == null) return ballPos;
        Vector2 tp = target.position;
        Vector2 toBall = ballPos - tp;
        Vector2 dir = toBall.sqrMagnitude > 1e-3f ? toBall.normalized : Forward();
        return ClampToField(tp + dir * 0.8f);
    }

    // Compact zone tight to our goal (man-down 5-on-6: protect the cage, concede outside).
    public Vector2 ManDownSpot(Transform me, Vector2 ballPos)
    {
        if (defendGoal == null) return ballPos;
        Vector2 g = defendGoal.position;
        Vector2 toBall = ballPos - g;
        Vector2 dir = toBall.sqrMagnitude > 1e-3f ? toBall.normalized : Forward();
        Vector2 basePos = g + dir * (defendDepth * 0.7f);
        Vector2 across = new Vector2(-Forward().y, Forward().x);
        return ClampToField(basePos + across * (RoleSpread(me) * defendWidth * 0.7f));
    }

    // 4-2 umbrella for man-up 6-on-5: point top-centre, two posts deep, the rest wide.
    public Vector2 ManUpSpot(Transform me, Vector2 ballPos)
    {
        if (attackGoal == null || defendGoal == null) return ballPos;
        Vector2 fwd = Forward();
        Vector2 across = new Vector2(-fwd.y, fwd.x);
        Vector2 ownGoal = defendGoal.position;
        float span = Vector2.Distance(attackGoal.position, ownGoal);

        float depthFrac, lateral;
        switch (RoleOf(me))
        {
            case Role.Center:     depthFrac = 0.62f; lateral =  0.0f;  break; // point (top centre)
            case Role.CenterBack: depthFrac = 0.86f; lateral =  0.45f; break; // post
            case Role.LeftWing:   depthFrac = 0.86f; lateral = -0.45f; break; // post
            case Role.RightWing:  depthFrac = 0.72f; lateral =  1.05f; break; // wide
            case Role.LeftFlat:   depthFrac = 0.72f; lateral = -1.05f; break; // wide
            default:              depthFrac = 0.55f; lateral =  0.7f;  break;
        }
        Vector2 spot = ownGoal + fwd * (depthFrac * span) + across * (lateral * laneHeight);
        return ClampToField(spot);
    }

    // Is `me` among the top-N most ADVANCED members (nearest the enemy goal), excluding the
    // carrier — i.e. a counterattack sprinter.
    public bool IsCounterRunner(Transform me, Transform carrier)
    {
        if (me == null || attackGoal == null || members == null) return false;
        Vector2 goal = attackGoal.position;
        float myDist = Vector2.Distance(me.position, goal);
        int ahead = 0;
        foreach (Transform m in members)
        {
            if (m == null || m == me || m == carrier) continue;
            if (Vector2.Distance(m.position, goal) < myDist) ahead++;
        }
        return ahead < Mathf.RoundToInt(counterRunners);
    }

    // A deep sprint target toward the enemy goal on my own lane (counter runners).
    public Vector2 CounterRunTarget(Transform me)
    {
        if (attackGoal == null || defendGoal == null || me == null)
            return me != null ? (Vector2)me.position : Vector2.zero;
        Vector2 fwd = Forward();
        Vector2 across = new Vector2(-fwd.y, fwd.x);
        float span = Vector2.Distance(attackGoal.position, defendGoal.position);
        Vector2 spot = (Vector2)defendGoal.position + fwd * (0.82f * span) + across * (RoleLateralSign(me) * laneHeight * 0.8f);
        return ClampToField(spot);
    }

    // The member furthest UP (nearest the enemy goal) — sprints back on counter-prevention.
    public Transform MostAdvancedMember()
    {
        if (members == null || attackGoal == null) return null;
        Vector2 goal = attackGoal.position;
        Transform best = null;
        float bestDist = Mathf.Infinity;
        foreach (Transform m in members)
        {
            if (m == null) continue;
            float d = Vector2.Distance(m.position, goal);
            if (d < bestDist) { bestDist = d; best = m; }
        }
        return best;
    }

    // Kept for compatibility / future use (static formation anchor).
    public Vector2 FormationSpot(int roleIndex, bool attacking, Vector2 ballPos)
    {
        if (attackGoal == null || defendGoal == null) return Vector2.zero;

        Vector2 forward = Forward();
        Vector2 midfield = ((Vector2)attackGoal.position + (Vector2)defendGoal.position) * 0.5f;
        float push = attacking ? attackPush : -defendPull;
        Vector2 anchor = midfield + forward * push;

        int count = members != null && members.Length > 0 ? members.Length : 1;
        float t = count > 1 ? ((float)roleIndex / (count - 1)) * 2f - 1f : 0f;

        Vector2 across = new Vector2(-forward.y, forward.x);
        Vector2 spot = anchor + across * (t * laneHeight);
        spot.y = Mathf.Lerp(spot.y, ballPos.y, 0.25f);
        return spot;
    }
}
