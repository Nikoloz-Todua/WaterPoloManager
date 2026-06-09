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
        // Toggle the PLAYER team's defensive scheme (bots always Press).
        if (Input.GetKeyDown(KeyCode.Z) && playerTeam != null)
        {
            playerTeam.defenseMode = playerTeam.defenseMode == TeamSide.DefenseMode.Press
                ? TeamSide.DefenseMode.Zone
                : TeamSide.DefenseMode.Press;
            UpdateDefenseModeText();
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
            int next = (activeIndex + 1) % players.Length;
            SetActive(next);
        }
    }

    void UpdateDefenseModeText()
    {
        if (defenseModeText == null || playerTeam == null) return;
        defenseModeText.text = "DEFENSE: " + (playerTeam.defenseMode == TeamSide.DefenseMode.Zone ? "ZONE" : "PRESS");
    }

    // returns the index of the player currently holding the ball, or -1
    int HolderIndex()
    {
        for (int i = 0; i < players.Length; i++)
        {
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