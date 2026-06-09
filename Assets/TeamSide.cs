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

    // Fixed tactical roles, assigned by slot index in `members` (0..5).
    public enum Role { Center, CenterBack, LeftWing, RightWing, LeftFlat, RightFlat }

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

    // ---- attacking: fixed role-based spot (distinct depth + lateral per role) ----
    // The carrier is handled by the brain's Carry(); only non-carriers use this.
    // Each role gets its own depth toward the enemy goal and its own lane, so the
    // team holds a real shape instead of converging on the ball.
    public Vector2 AttackPositionFor(Transform me, Vector2 ballPos)
    {
        if (attackGoal == null || defendGoal == null) return ballPos;

        Vector2 fwd = Forward();                              // own goal → enemy goal
        Vector2 across = new Vector2(-fwd.y, fwd.x);
        Vector2 ownGoal = defendGoal.position;
        float span = Vector2.Distance(attackGoal.position, defendGoal.position);

        float depthFrac;   // 0 = our goal, 1 = enemy goal
        float lateral;     // signed offset along `across`, in laneHeight units
        switch (RoleOf(me))
        {
            case Role.Center:     depthFrac = 0.80f; lateral =  0.0f; break; // deep, ~2-meter
            case Role.LeftWing:   depthFrac = 0.68f; lateral =  0.9f; break; // forward + wide
            case Role.RightWing:  depthFrac = 0.68f; lateral = -0.9f; break;
            case Role.LeftFlat:   depthFrac = 0.52f; lateral =  0.5f; break; // mid-depth ~5-meter
            case Role.RightFlat:  depthFrac = 0.52f; lateral = -0.5f; break;
            case Role.CenterBack: depthFrac = 0.30f; lateral =  0.0f; break; // stays back
            default:              depthFrac = 0.50f; lateral =  0.0f; break;
        }

        Vector2 spot = ownGoal + fwd * (depthFrac * span) + across * (lateral * laneHeight);
        return ClampToField(spot);
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

    // Aim at the goal corner away from the shooter. The keeper tracks the ball's
    // y, so the corner opposite the shooter is the open one.
    public Vector2 ShotAimPoint(Vector2 from)
    {
        if (attackGoal == null) return from;
        Vector2 c = attackGoal.position;
        float aimY = c.y + (from.y >= c.y ? -aimCornerOffset : aimCornerOffset);
        return new Vector2(c.x, aimY);
    }

    // Best open teammate to pass to. Prefers forward progress; when the carrier is
    // pressured it will accept any open mate (incl. lateral/back) to keep possession.
    // Returns null when keeping the ball is better.
    public Transform BestPassTarget(Transform carrier, TeamSide enemy, bool pressured)
    {
        if (members == null || attackGoal == null || carrier == null) return null;

        Vector2 goal = attackGoal.position;
        float carrierGoalDist = Vector2.Distance(carrier.position, goal);

        Transform best = null;
        float bestScore = float.NegativeInfinity;

        foreach (Transform mate in members)
        {
            if (mate == null || mate == carrier) continue;

            // must be open (not tightly marked)
            float openness = NearestDistance(mate.position, enemy);
            if (openness < openRadius) continue;

            // the passing lane must be clear of defenders
            if (!LaneClear(carrier.position, mate.position, enemy, passLaneRadius)) continue;

            float mateGoalDist = Vector2.Distance(mate.position, goal);
            float forwardGain = carrierGoalDist - mateGoalDist;

            // when not under pressure, only pass if it actually advances the ball
            if (!pressured && forwardGain < forwardPassMin) continue;

            // prefer forward + open + closer to goal
            float score = forwardGain * 1.5f + openness - mateGoalDist * 0.1f;
            if (score > bestScore) { bestScore = score; best = mate; }
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
        p.x = Mathf.Clamp(p.x, minX + 0.5f, maxX - 0.5f);
        p.y = Mathf.Clamp(p.y, midY - laneHeight, midY + laneHeight);
        return p;
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
