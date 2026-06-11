using UnityEngine;
using TMPro;

public class TeamManager : MonoBehaviour
{
    [SerializeField] private PlayerMovement[] players;
    [SerializeField] private TeammateAI[] teammateAIs; // same order as players
    [SerializeField] private TeamSide playerTeam;
    [SerializeField] private TMP_Text defenseModeText; // shows "DEFENSE: PRESS"/"ZONE"

    private int activeIndex = 0;

    void Start()
    {
        SetActive(0);
        UpdateDefenseModeText();
    }

    void Update()
    {
        // Cycle the PLAYER team's defensive scheme: Press → Zone → Drop → MPress (bots pick
        // their own situationally).
        if (Input.GetKeyDown(KeyCode.Z) && playerTeam != null)
        {
            playerTeam.defenseMode = (TeamSide.DefenseMode)(((int)playerTeam.defenseMode + 1) % 4);
            UpdateDefenseModeText();
        }

        // 0) If the active player got excluded, hand control to a valid teammate.
        if (IsExcludedIndex(activeIndex))
        {
            int v = NextValidIndex(activeIndex);
            if (v != -1) SetActive(v);
        }

        // 1) If one of my players holds the ball, control auto-follows to them.
        int holder = HolderIndex();
        if (holder != -1 && holder != activeIndex)
        {
            SetActive(holder);
            return;
        }

        // 2) Manual switch (only useful when nobody on my team holds the ball).
        if (Input.GetKeyDown(KeyCode.C))
        {
            int next = NextValidIndex(activeIndex);
            if (next != -1) SetActive(next);
        }
    }

    bool IsExcludedIndex(int i)
    {
        return i >= 0 && i < players.Length && players[i] != null &&
               ExclusionManager.Instance != null &&
               ExclusionManager.Instance.IsExcluded(players[i].transform);
    }

    // Next selectable (non-null, non-excluded) player after `from`, or -1 if none.
    int NextValidIndex(int from)
    {
        int n = players.Length;
        for (int step = 1; step <= n; step++)
        {
            int idx = (from + step) % n;
            if (players[idx] != null && !IsExcludedIndex(idx)) return idx;
        }
        return -1;
    }

    // Public so other systems can refresh the label (it shows the PLAYER team's mode;
    // bot mode changes are reported through the event feed instead).
    public void UpdateDefenseModeText()
    {
        if (defenseModeText == null || playerTeam == null) return;
        defenseModeText.text = "DEFENSE: " + playerTeam.defenseMode.ToString().ToUpper();
    }

    // returns the index of the player currently holding the ball, or -1
    int HolderIndex()
    {
        for (int i = 0; i < players.Length; i++)
        {
            if (IsExcludedIndex(i)) continue; // excluded players can't hold
            if (players[i] != null && players[i].IsHolding) return i;
            if (teammateAIs != null && i < teammateAIs.Length &&
                teammateAIs[i] != null && teammateAIs[i].IsHolding) return i;
        }
        return -1;
    }

    void SetActive(int index)
    {
        for (int i = 0; i < players.Length; i++)
        {
            bool willBeActive = (i == index);

            // If this teammate's AI was carrying the ball and we're taking control,
            // hand the hold over to PlayerMovement instead of dropping it.
            if (willBeActive && teammateAIs != null && i < teammateAIs.Length &&
                teammateAIs[i] != null && teammateAIs[i].IsHolding)
            {
                teammateAIs[i].IsHolding = false;        // AI lets go (no drop)
                players[i].TakeOverHeldBall();           // human keeps the ball
            }

            players[i].IsActive = willBeActive;
        }
        activeIndex = index;
    }
}