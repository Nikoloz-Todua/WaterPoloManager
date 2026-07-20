using UnityEngine;

// Sits on the BALL. When the loose ball physically touches a field player, record that
// player's team as the last toucher — so a shot/pass that deflects off an opponent and
// goes out is awarded correctly. A physical keeper touch is recorded distinctly so
// GoalLineOut can award a corner; held-ball contacts are ignored.
[RequireComponent(typeof(Rigidbody2D))]
public class BallTouchTracker : MonoBehaviour
{
    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null || collision.collider == null) return;

        MatchContext ctx = MatchContext.Instance; // may be null on scene-load order → bail
        if (ctx == null || ctx.Ball == null) return;

        // ignore touches while the ball is held/parented (only loose-ball deflections count)
        if (ctx.Ball.transform.parent != null) return;

        GameObject other = collision.collider.gameObject;
        if (other == null) return;

        Goalkeeper keeper = other.GetComponentInParent<Goalkeeper>();
        if (keeper != null)
        {
            ctx.NoteKeeperTouch(keeper.DefendingTeam);
            return;
        }

        IAgentBody body = other.GetComponent<IAgentBody>();
        if (body != null) ctx.NoteTouch(body.Team); // bot or (player-team) teammate
    }
}
