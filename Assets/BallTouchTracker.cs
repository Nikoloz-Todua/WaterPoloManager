using UnityEngine;

// Sits on the BALL. When the loose ball physically touches a field player, record that
// player's team as the last toucher — so a shot/pass that deflects off an opponent and
// goes out is awarded correctly. Keeper touches and held-ball contacts are ignored
// (a keeper deflection must NOT flip the out-of-bounds award).
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
        if (other.GetComponent<Goalkeeper>() != null) return; // keeper deflection: leave the award alone

        IAgentBody body = other.GetComponent<IAgentBody>();
        if (body != null) ctx.NoteTouch(body.Team); // bot or (player-team) teammate
    }
}
