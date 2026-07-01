using UnityEngine;
using TMPro;

// Drives match time: `totalQuarters` quarters of `quarterLength` seconds each.
// At the final whistle it freezes play (Time.timeScale = 0) and shows the result.
// Reads the score from ScoreManager to decide the winner.
public class MatchTimer : MonoBehaviour
{
    public static MatchTimer Instance { get; private set; }

    [Header("References")]
    [SerializeField] private ScoreManager scoreManager;
    [SerializeField] private TMP_Text timerText;   // time left in the quarter, e.g. "0:23"
    [SerializeField] private TMP_Text quarterText; // current quarter, e.g. "Q1"
    [SerializeField] private TMP_Text resultText;  // hidden until match ends, then shows winner

    [Header("Match settings")]
    [SerializeField] private float quarterLength = 90f; // seconds per quarter
    [SerializeField] private int totalQuarters = 4;

    private float timeLeft;     // seconds remaining in the current quarter
    private int currentQuarter; // 1..totalQuarters
    private bool matchOver;
    private bool awaitingResume; // a quarter-break panel is up; the clock waits for RESUME

    // read-only access for other systems (shot clock pauses on this; event feed stamps with it)
    public bool MatchOver => matchOver;

    // Whole-match seconds remaining (rest of this quarter + all quarters still to play).
    // Used by the bot's adaptive defense ("protect a late lead").
    public float RemainingSeconds()
        => matchOver ? 0f
                     : Mathf.Max(0f, timeLeft) + Mathf.Max(0, totalQuarters - currentQuarter) * quarterLength;

    void Awake() { Instance = this; }

    void Start()
    {
        Time.timeScale = 1f;    // un-freeze in case a previous match ended frozen
        currentQuarter = 1;
        timeLeft = quarterLength;
        matchOver = false;

        if (resultText != null) resultText.gameObject.SetActive(false);
        UpdateQuarterText();
        UpdateTimerText();

        // Q1 begins with a sprint duel (the duel freezes play; the clock waits below).
        if (SprintDuel.Instance != null) SprintDuel.Instance.StartDuel();
    }

    void Update()
    {
        if (matchOver) return;
        if (awaitingResume) return; // quarter-break panel up — frozen, waiting for RESUME

        // The quarter clock is paused while play is frozen (sprint duel line-up/race,
        // post-goal settle), so the timer only drains during live play.
        MatchContext ctx = MatchContext.Instance;
        if (ctx != null && ctx.PlayFrozen) { UpdateTimerText(); return; }

        timeLeft -= Time.deltaTime;

        if (timeLeft <= 0f)
        {
            timeLeft = 0f;
            UpdateTimerText();

            // last quarter just ran out → final whistle
            if (currentQuarter >= totalQuarters) { EndMatch(); return; }

            // otherwise the quarter is over → show the between-quarters pause screen and
            // wait for RESUME (Feature 2). The next quarter's sprint duel starts from there.
            ShowQuarterBreak();
            return;
        }

        UpdateTimerText();
    }

    // Freeze play and raise the quarter-break panel. RESUME → OnResume → next quarter duel.
    void ShowQuarterBreak()
    {
        awaitingResume = true;
        MatchContext ctx = MatchContext.Instance;
        if (ctx != null) ctx.FreezeAll();
        if (TouchControls.Instance != null) TouchControls.Instance.SetGameplayVisible(false);

        int you = scoreManager != null ? scoreManager.HomeScore : 0;
        int bot = scoreManager != null ? scoreManager.AwayScore : 0;
        QuarterBreakUI.Get().Show(currentQuarter, you, bot, OnResume);
    }

    // RESUME pressed on the quarter-break panel: roll into the next quarter via the duel.
    void OnResume()
    {
        awaitingResume = false;
        AdvanceToNextQuarter();
    }

    void AdvanceToNextQuarter()
    {
        currentQuarter++;
        timeLeft = quarterLength;
        UpdateQuarterText();

        // halftime (after the middle quarter): swap ends so both attack the other way
        MatchContext ctx = MatchContext.Instance;
        if (ctx != null && currentQuarter == totalQuarters / 2 + 1)
        {
            ctx.SwapEnds();
            if (EventFeed.Instance != null) EventFeed.Instance.AddEvent("Halftime - ends switched");
        }

        // every quarter restarts through the sprint duel (the duel re-freezes for its countdown,
        // then unfreezes when a sprinter wins; it also restores the touch UI on the way out)
        if (SprintDuel.Instance != null) SprintDuel.Instance.StartDuel();
        else if (ctx != null) ctx.Unfreeze(); // no duel in the scene → just resume
    }

    void EndMatch()
    {
        matchOver = true;
        Time.timeScale = 0f; // freeze all movement + AI

        int you = scoreManager != null ? scoreManager.HomeScore : 0;
        int bot = scoreManager != null ? scoreManager.AwayScore : 0;
        int outcome = you > bot ? 1 : (bot > you ? -1 : 0);

        // Completing a match (full time only — not forfeits) earns a reward-slot card pack,
        // shown on the hub's bottom bar.
        PostMatchRewardManager.Instance.AddRewardForMatch();

        // Full result screen (overlay + buttons); the bare text is only a fallback
        // for a scene without a MatchResultUI component.
        if (MatchResultUI.Instance != null)
        {
            MatchResultUI.Instance.Show("FULL TIME", outcome);
            return;
        }

        if (resultText == null) return;
        string outcomeStr = outcome > 0 ? "YOU WIN" : (outcome < 0 ? "YOU LOSE" : "DRAW");
        resultText.gameObject.SetActive(true);
        resultText.text = "FULL TIME\n" + outcomeStr + "\n" + you + " - " + bot;
    }

    // Force an immediate end (e.g. a forfeit from too many exclusions). Reuses the same
    // freeze + result-reveal as the final whistle, but with a forced winner instead of
    // the score-based decision.
    public void ForfeitMatch(bool playerWins)
    {
        if (matchOver) return;
        matchOver = true;
        Time.timeScale = 0f;

        // Forfeit outcome is FORCED (not score-based — a team can forfeit while level).
        if (MatchResultUI.Instance != null)
        {
            MatchResultUI.Instance.Show("FORFEIT", playerWins ? 1 : -1);
            return;
        }

        if (resultText == null) return;

        int you = scoreManager != null ? scoreManager.HomeScore : 0;
        int bot = scoreManager != null ? scoreManager.AwayScore : 0;
        string outcome = playerWins ? "YOU WIN" : "YOU LOSE";

        resultText.gameObject.SetActive(true);
        resultText.text = "FORFEIT\n" + outcome + "\n" + you + " - " + bot;
    }

    void UpdateTimerText()
    {
        if (timerText == null) return;
        float t = Mathf.Max(0f, timeLeft);
        int minutes = Mathf.FloorToInt(t / 60f);
        int seconds = Mathf.FloorToInt(t % 60f);
        timerText.text = minutes + ":" + seconds.ToString("00"); // "0:23"
    }

    void UpdateQuarterText()
    {
        if (quarterText != null)
            quarterText.text = "Q" + currentQuarter;
    }

    // Elapsed match time as "MM:SS" (across quarters), used by the event feed.
    public string MatchTimeStamp()
    {
        float elapsed = Mathf.Max(0f, (currentQuarter - 1) * quarterLength + (quarterLength - timeLeft));
        int m = Mathf.FloorToInt(elapsed / 60f);
        int s = Mathf.FloorToInt(elapsed % 60f);
        return m.ToString("00") + ":" + s.ToString("00");
    }
}