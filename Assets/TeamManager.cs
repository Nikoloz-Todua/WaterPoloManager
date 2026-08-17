using UnityEngine;
using TMPro;

public class TeamManager : MonoBehaviour
{
    [SerializeField] private PlayerMovement[] players;
    [SerializeField] private TeammateAI[] teammateAIs; // same order as players
    [SerializeField] private TeamSide playerTeam;
    [SerializeField] private TMP_Text defenseModeText; // shows "DEFENSE: PRESS"/"ZONE"
    [Tooltip("After the active player loses/drops the ball, keep control on them this long before auto-switching to a new holder (so they can chase their own loose ball). Manual C / touch SWITCH is never delayed.")]
    [SerializeField] private float autoSwitchDelay = 0.5f;

    private int activeIndex = 0;
    private float noAutoSwitchUntil = -10f; // auto-switch-to-holder suppressed until this time
    private bool activeWasHolding;          // did the active player hold the ball last frame

    // The human-controlled player right now. TouchControls reads this every frame
    // to know which PlayerMovement receives the touch input.
    public static PlayerMovement ActivePlayer { get; private set; }

    // Roster slot of the active player (0-based). The touch HUD shows it as "P{index+1}".
    public static int ActivePlayerIndex { get; private set; }
    public static int ActiveCapNumber
    {
        get
        {
            MatchPlayerState state = ActivePlayer != null
                ? MatchPlayerState.For(ActivePlayer.transform) : null;
            return state != null ? state.CapNumber : ActivePlayerIndex + 1;
        }
    }

    private static TeamManager instance;

    void Awake() { instance = this; }

    void Start()
    {
        int first = FirstValidIndex();
        SetActive(first);
        UpdateDefenseModeText();
    }

    // MatchSquadManager creates match-local bench bodies before Start.  Expand the existing
    // serialized control arrays in memory only; no scene or permanent roster data is rewritten.
    public static void RegisterRuntimePlayer(PlayerMovement player, TeammateAI ai)
    {
        if (instance == null || player == null) return;
        if (instance.players == null) instance.players = new PlayerMovement[0];
        for (int i = 0; i < instance.players.Length; i++)
            if (instance.players[i] == player) return;

        int oldLength = instance.players != null ? instance.players.Length : 0;
        System.Array.Resize(ref instance.players, oldLength + 1);
        instance.players[oldLength] = player;
        int aiLength = instance.teammateAIs != null ? instance.teammateAIs.Length : 0;
        if (aiLength < oldLength + 1) System.Array.Resize(ref instance.teammateAIs, oldLength + 1);
        instance.teammateAIs[oldLength] = ai != null ? ai : player.GetComponent<TeammateAI>();
        player.IsActive = false;
    }

    // Force control to a specific player (used by the sprint duel so the camera follows the
    // human sprinter). No-op if the transform isn't one of our players.
    public static void ActivatePlayer(Transform t)
    {
        if (instance == null || instance.players == null || t == null) return;
        for (int i = 0; i < instance.players.Length; i++)
            if (instance.players[i] != null && instance.players[i].transform == t)
            { instance.SetActive(i); return; }
    }

    void Update()
    {
        if (players == null || players.Length == 0)
        {
            SetActive(-1);
            return;
        }

        // Cycle the PLAYER team's defensive scheme: Press → Zone → Drop → MPress (bots pick
        // their own situationally).
        if (Input.GetKeyDown(KeyCode.Z) && playerTeam != null)
        {
            playerTeam.defenseMode = (TeamSide.DefenseMode)(((int)playerTeam.defenseMode + 1) % 4);
            UpdateDefenseModeText();
        }

        // 0) If the active player got excluded, hand control to a valid teammate.
        if (!IsSelectableIndex(activeIndex))
        {
            int v = NextValidIndex(activeIndex);
            SetActive(v);
        }

        // When the active player loses/drops the ball, start a short window during which we
        // DON'T auto-switch to a new holder — so they keep control to chase their own loose
        // ball instead of the camera snapping to a teammate that grabbed it.
        bool activeHolds = activeIndex >= 0 && activeIndex < players.Length &&
                           players[activeIndex] != null && players[activeIndex].IsHolding;
        if (activeWasHolding && !activeHolds) noAutoSwitchUntil = Time.time + autoSwitchDelay;
        activeWasHolding = activeHolds;

        // 1) If one of my players holds the ball, control auto-follows to them (after the
        //    post-loss delay above; manual C in step 2 is never delayed).
        int holder = HolderIndex();
        if (holder != -1 && holder != activeIndex && Time.time >= noAutoSwitchUntil)
        {
            SetActive(holder);
            return;
        }

        // 2) Manual switch (only useful when nobody on my team holds the ball).
        // The touch SWITCH button merges into the same check via TouchSwitchDown.
        if (Input.GetKeyDown(KeyCode.C) || (ActivePlayer != null && ActivePlayer.TouchSwitchDown))
        {
            int next = NextValidIndex(activeIndex);
            if (next != -1) SetActive(next);
        }
    }

    bool IsExcludedIndex(int i)
    {
        return players != null && i >= 0 && i < players.Length && players[i] != null &&
               ExclusionManager.Instance != null &&
               ExclusionManager.Instance.IsExcluded(players[i].transform);
    }

    bool IsSelectableIndex(int i)
    {
        if (players == null || i < 0 || i >= players.Length || players[i] == null) return false;
        MatchPlayerState state = MatchPlayerState.For(players[i].transform);
        if (state != null && !state.Selectable) return false;
        return !IsExcludedIndex(i);
    }

    int FirstValidIndex()
    {
        if (players == null) return -1;
        for (int i = 0; i < players.Length; i++)
            if (IsSelectableIndex(i)) return i;
        return -1;
    }

    // Next selectable (non-null, non-excluded) player after `from`, or -1 if none.
    int NextValidIndex(int from)
    {
        if (players == null || players.Length == 0) return -1;
        int n = players.Length;
        for (int step = 1; step <= n; step++)
        {
            int idx = (from + step) % n;
            if (IsSelectableIndex(idx)) return idx;
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
        if (players == null) return -1;
        for (int i = 0; i < players.Length; i++)
        {
            if (!IsSelectableIndex(i)) continue; // bench/excluded/transition players can't hold
            if (players[i] != null && players[i].IsHolding) return i;
            if (teammateAIs != null && i < teammateAIs.Length &&
                teammateAIs[i] != null && teammateAIs[i].IsHolding) return i;
        }
        return -1;
    }

    void SetActive(int index)
    {
        if (players == null)
        {
            activeIndex = -1;
            ActivePlayerIndex = -1;
            ActivePlayer = null;
            return;
        }
        if (index >= 0 && !IsSelectableIndex(index)) index = FirstValidIndex();
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

            if (players[i] != null) players[i].IsActive = willBeActive;
        }
        activeIndex = index;
        ActivePlayerIndex = index;
        ActivePlayer = (index >= 0 && index < players.Length) ? players[index] : null;
    }

    public static void EnsureValidActive()
    {
        if (instance == null) return;
        if (instance.IsSelectableIndex(instance.activeIndex)) return;
        instance.SetActive(instance.FirstValidIndex());
    }
}
