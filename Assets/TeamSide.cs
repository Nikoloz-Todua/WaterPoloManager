using UnityEngine;

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

    // computes where a role should stand, given whether we're attacking and where the ball is
    public Vector2 FormationSpot(int roleIndex, bool attacking, Vector2 ballPos)
    {
        if (attackGoal == null || defendGoal == null) return Vector2.zero;

        // direction from our goal toward the enemy goal (the "forward" axis)
        Vector2 forward = ((Vector2)attackGoal.position - (Vector2)defendGoal.position).normalized;

        // base anchor: midpoint between the two goals, shifted forward (attack) or back (defend)
        Vector2 midfield = ((Vector2)attackGoal.position + (Vector2)defendGoal.position) * 0.5f;
        float push = attacking ? attackPush : -defendPull;
        Vector2 anchor = midfield + forward * push;

        // spread players vertically by role index so they never stack
        int count = members != null && members.Length > 0 ? members.Length : 1;
        // spread roles across [-1, 1] then scale by laneHeight
        float t = count > 1 ? ((float)roleIndex / (count - 1)) * 2f - 1f : 0f;

        // perpendicular axis (across the pool) to offset each role
        Vector2 across = new Vector2(-forward.y, forward.x);
        Vector2 spot = anchor + across * (t * laneHeight);

        // nudge the whole shape slightly toward the ball's vertical position so they react to play
        spot.y = Mathf.Lerp(spot.y, ballPos.y, 0.25f);

        return spot;
    }
}