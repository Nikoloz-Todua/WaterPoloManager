using UnityEngine;

public class Goal : MonoBehaviour
{
    [SerializeField] private string goalSide = "Right"; // "Right" or "Left"
    [SerializeField] private ScoreManager scoreManager;

    void Awake()
    {
        // The scoring trigger may live on a GoalLine child while this component stays on the
        // visible net root. Unity sends a trigger callback to the collider GameObject (and to an
        // attached Rigidbody2D), not to an arbitrary parent, so install a tiny runtime relay on
        // child triggers. Keeping the notifier here preserves the established score/reaction path.
        Collider2D[] colliders = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider == null || !collider.isTrigger || collider.gameObject == gameObject) continue;

            GoalTriggerRelay relay = collider.GetComponent<GoalTriggerRelay>();
            if (relay == null) relay = collider.gameObject.AddComponent<GoalTriggerRelay>();
            relay.Configure(this);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        NotifyBallEntered(other);
    }

    internal void NotifyBallEntered(Collider2D other)
    {
        // Pass the visible root transform so ScoreManager reacts on THIS net, even when the
        // scoring trigger itself is a child GoalLine collider.
        if (other.CompareTag("Ball") && scoreManager != null)
            scoreManager.BallEnteredGoal(goalSide, transform);
    }

    // Physical outer-net contacts use ScoreManager's one existing visual-reaction owner without
    // entering the scoring path. Posts intentionally do not call this: only netting deforms.
    internal void NotifyNetHit(Vector2 impactWorld, float impactSpeed)
    {
        if (scoreManager != null)
            scoreManager.BallHitPhysicalNet(transform, impactWorld, impactSpeed);
    }
}

// Runtime-only bridge for a child GoalLine trigger. It owns no scoring logic; the existing
// Goal -> ScoreManager architecture remains the single path that can award a goal.
[DisallowMultipleComponent]
sealed class GoalTriggerRelay : MonoBehaviour
{
    private Goal owner;

    public void Configure(Goal goal) { owner = goal; }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (owner != null) owner.NotifyBallEntered(other);
    }
}
