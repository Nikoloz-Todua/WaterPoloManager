using UnityEngine;

// Represents one team: its members, and which goals it attacks/defends.
public class TeamSide : MonoBehaviour
{
    public string teamName = "Team";
    public Transform attackGoal;   // the goal this team shoots at
    public Transform defendGoal;   // the goal this team protects

    // all field players on this team (not the keeper)
    public Transform[] members;

    // returns the member closest to a given point (e.g. the ball)
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
}