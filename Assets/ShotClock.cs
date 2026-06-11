using UnityEngine;
using TMPro;

// 30-second shot clock (plan B16.5). Separate from MatchTimer because it has its own
// per-possession lifecycle and must be reset by several systems (goal, possession
// change, defensive exclusion). Singleton so those systems can call ResetClock().
//
// Resets to full when: possession changes to a different team, a goal is scored
// (ScoreManager), or a defending-team exclusion happens (ExclusionManager).
// At zero: the possessing team loses the ball (forced drop = turnover). Pauses while
// the ball is loose, while the match is over, and between kickoff resets (ball loose).
public class ShotClock : MonoBehaviour
{
    public static ShotClock Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private float shotClockSeconds = 30f;
    [SerializeField] private float warningThreshold = 5f; // text turns red at/below this

    [Header("References")]
    [SerializeField] private MatchTimer matchTimer;   // to pause when the match is over
    [SerializeField] private TMP_Text shotClockText;  // whole seconds, e.g. "24"

    [Header("Colors")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color warningColor = Color.red;

    private float timeLeft;
    private TeamSide lastTeam; // last team that held possession (persists through loose periods)

    void Awake()
    {
        Instance = this;
        timeLeft = shotClockSeconds;
    }

    void Start() { UpdateDisplay(); }

    void Update()
    {
        MatchContext ctx = MatchContext.Instance;
        if (ctx == null) return;

        // Paused when the match is over, while play is frozen (sprint duel / goal settle /
        // penalty), or during a free throw.
        if ((matchTimer != null && matchTimer.MatchOver) || ctx.PlayFrozen || ctx.FreeThrowActive)
        { UpdateDisplay(); return; }

        TeamSide cur = ctx.PossessingTeam;

        // Possession changed to a DIFFERENT team → fresh clock. (A pass within the same
        // team goes team→loose→same team and does NOT reset.) A keeper collecting the ball
        // is NOT a reset — the clock keeps ticking for the holding team (Part 1); the reset
        // happens when the keeper distributes (Goalkeeper calls ResetClock()).
        if (cur != null && cur != lastTeam)
        {
            if (!ctx.KeeperHolding) ResetClock();
            lastTeam = cur;
        }

        // Loose ball → pause (no tick); keep lastTeam so a same-team recovery resumes.
        if (cur == null) { UpdateDisplay(); return; }

        // Possessing team → tick down.
        timeLeft -= Time.deltaTime;
        if (timeLeft <= 0f)
            Turnover(ctx);

        UpdateDisplay();
    }

    // Reset to full. Called on possession change (internally), goal, and defensive exclusion.
    public void ResetClock()
    {
        timeLeft = shotClockSeconds;
        UpdateDisplay();
    }

    // Shot-clock violation: the possessing team loses the ball and can't re-grab it
    // until the other team has had it.
    void Turnover(MatchContext ctx)
    {
        TeamSide violator = ctx.PossessingTeam;
        ctx.ClearKeeperHold();               // a violation ends any keeper hold too
        ctx.ForceDropHeldBall();             // reuse the shared drop/release path; ball goes loose
        if (violator != null) ctx.SetGrabBan(violator);
        if (EventFeed.Instance != null) EventFeed.Instance.AddEvent("Shot clock - turnover");
        timeLeft = shotClockSeconds;         // ball is now loose → next frame pauses until re-grabbed
    }

    void UpdateDisplay()
    {
        if (shotClockText == null) return;
        int secs = Mathf.CeilToInt(Mathf.Max(0f, timeLeft));
        shotClockText.text = secs.ToString();
        shotClockText.color = (timeLeft <= warningThreshold) ? warningColor : normalColor;
    }
}
