using UnityEngine;
using TMPro;

// Drives match time: `totalQuarters` quarters of `quarterLength` seconds each.
// At the final whistle it freezes play (Time.timeScale = 0) and shows the result.
// Reads the score from ScoreManager to decide the winner.
public class MatchTimer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ScoreManager scoreManager;
    [SerializeField] private TMP_Text timerText;   // time left in the quarter, e.g. "0:23"
    [SerializeField] private TMP_Text quarterText; // current quarter, e.g. "Q1"
    [SerializeField] private TMP_Text resultText;  // hidden until match ends, then shows winner

    [Header("Match settings")]
    [SerializeField] private float quarterLength = 30f; // seconds per quarter (bump to 90 later)
    [SerializeField] private int totalQuarters = 4;

    private float timeLeft;     // seconds remaining in the current quarter
    private int currentQuarter; // 1..totalQuarters
    private bool matchOver;

    // read-only access for other systems (shot clock pauses on this; event feed stamps with it)
    public bool MatchOver => matchOver;

    void Start()
    {
        Time.timeScale = 1f;    // un-freeze in case a previous match ended frozen
        currentQuarter = 1;
        timeLeft = quarterLength;
        matchOver = false;

        if (resultText != null) resultText.gameObject.SetActive(false);
        UpdateQuarterText();
        UpdateTimerText();
    }

    void Update()
    {
        if (matchOver) return;

        timeLeft -= Time.deltaTime;

        if (timeLeft <= 0f)
        {
            // last quarter just ran out → final whistle
            if (currentQuarter >= totalQuarters)
            {
                timeLeft = 0f;
                UpdateTimerText();
                EndMatch();
                return;
            }

            // otherwise roll into the next quarter (play continues, no stoppage)
            currentQuarter++;
            timeLeft = quarterLength;
            UpdateQuarterText();
        }

        UpdateTimerText();
    }

    void EndMatch()
    {
        matchOver = true;
        Time.timeScale = 0f; // freeze all movement + AI

        if (resultText == null) return;

        int you = scoreManager != null ? scoreManager.HomeScore : 0;
        int bot = scoreManager != null ? scoreManager.AwayScore : 0;

        string outcome;
        if (you > bot)      outcome = "YOU WIN";
        else if (bot > you) outcome = "YOU LOSE";
        else                outcome = "DRAW";

        resultText.gameObject.SetActive(true);
        resultText.text = "FULL TIME\n" + outcome + "\n" + you + " - " + bot;
    }

    // Force an immediate end (e.g. a forfeit from too many exclusions). Reuses the same
    // freeze + result-reveal as the final whistle, but with a forced winner instead of
    // the score-based decision.
    public void ForfeitMatch(bool playerWins)
    {
        if (matchOver) return;
        matchOver = true;
        Time.timeScale = 0f;

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