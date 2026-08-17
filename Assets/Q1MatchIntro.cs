using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Q1-only pre-duel presentation.  The existing SprintDuel remains the complete rules/gameplay
// owner; this merely moves the same legal bodies into two short huddles and back to the duel's
// own authoritative start coordinates.
[DefaultExecutionOrder(-325)]
public sealed class Q1MatchIntro : MonoBehaviour
{
    public static Q1MatchIntro Instance { get; private set; }

    [SerializeField] private float huddleMoveSpeed = 4.5f;
    [SerializeField] private float huddleRadius = 0.78f;
    [SerializeField] private float huddleHoldSeconds = 4.0f;
    [SerializeField] private float arrivalRadius = 0.20f;
    [SerializeField, Range(0.25f, 0.55f)] private float huddleDepthInOwnHalf = 0.38f;

    // Return much closer than the loose huddle tolerance, then settle the last imperceptible gap
    // exactly onto SprintDuel's live authoritative target before ownership changes hands.
    private const float DuelHandoffArrivalRadius = 0.025f;

    private Coroutine sequence;
    private readonly List<MatchPlayerState> moving = new List<MatchPlayerState>();
    private Action onComplete;

    public static Q1MatchIntro Ensure(MatchContext owner)
    {
        if (Instance != null) return Instance;
        Q1MatchIntro intro = owner.GetComponent<Q1MatchIntro>();
        if (intro == null) intro = owner.gameObject.AddComponent<Q1MatchIntro>();
        return intro;
    }

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    public void StartIntro(Action complete)
    {
        if (sequence != null)
        {
            StopCoroutine(sequence);
            ReleaseIntroMovement(null, false);
        }
        onComplete = complete;
        sequence = StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        MatchContext context = MatchContext.Instance;
        SprintDuel duel = SprintDuel.Instance;
        MatchSquadManager squad = MatchSquadManager.Instance;
        if (context == null || duel == null || squad == null)
        {
            Finish();
            yield break;
        }

        duel.PrepareForIntro();
        moving.Clear();
        AddLegalTeam(context.PlayerTeam);
        AddLegalTeam(context.BotTeam);
        if (moving.Count == 0)
        {
            Finish();
            yield break;
        }

        CommandHuddle(context.PlayerTeam);
        CommandHuddle(context.BotTeam);
        while (!AllArrived()) yield return null;

        // At target the Rigidbody is still transition-owned but has zero velocity, so the current
        // floating/idle flipbook supplies the visible talk beat without new animation assets.
        float hold = 0f;
        while (hold < huddleHoldSeconds)
        {
            if (Time.timeScale > 0f) hold += Time.deltaTime;
            yield return null;
        }

        for (int i = 0; i < moving.Count; i++)
        {
            MatchPlayerState player = moving[i];
            if (player == null || !player.GameplayEligible) continue;
            player.BeginMove(MatchMovePurpose.Q1Huddle,
                duel.StartPositionFor(player.Team, player.transform), huddleMoveSpeed,
                DuelHandoffArrivalRadius, true, false, MatchMoveAnchor.SprintStart);
        }
        while (!AllArrived()) yield return null;

        ReleaseIntroMovement(duel, true);
        Finish();
    }

    void AddLegalTeam(TeamSide team)
    {
        if (team == null || team.members == null) return;
        for (int i = 0; i < team.members.Length; i++)
        {
            MatchPlayerState state = MatchPlayerState.For(team.members[i]);
            if (state != null && state.GameplayEligible) moving.Add(state);
        }
    }

    void CommandHuddle(TeamSide team)
    {
        if (team == null || team.members == null || team.defendGoal == null) return;
        Vector2 ownGoal = team.defendGoal.position;
        Vector2 forward = team.attackGoal != null
            ? ((Vector2)team.attackGoal.position - ownGoal).normalized
            : (ownGoal.x < 0f ? Vector2.right : Vector2.left);
        float span = team.attackGoal != null
            ? Vector2.Distance(team.attackGoal.position, ownGoal) : 14f;
        Vector2 centre = ownGoal + forward * (span * huddleDepthInOwnHalf);

        int eligible = 0;
        for (int i = 0; i < team.members.Length; i++)
            if (MatchPlayerState.IsGameplayEligible(team.members[i])) eligible++;
        int ringCount = Mathf.Max(1, eligible - 1);
        int ringIndex = 0;
        bool captainAssigned = false;

        for (int i = 0; i < team.members.Length; i++)
        {
            MatchPlayerState player = MatchPlayerState.For(team.members[i]);
            if (player == null || !player.GameplayEligible) continue;
            Vector2 target;
            if (!captainAssigned)
            {
                // No persistent captain field exists in current roster data.  The first eligible
                // starter is a stable presentation-only captain; nothing is saved.
                target = centre;
                captainAssigned = true;
            }
            else
            {
                float angle = ringIndex * Mathf.PI * 2f / ringCount;
                target = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * huddleRadius;
                ringIndex++;
            }
            player.BeginMove(MatchMovePurpose.Q1Huddle, target, huddleMoveSpeed,
                             arrivalRadius, true, false, MatchMoveAnchor.Q1Huddle);
        }
    }

    bool AllArrived()
    {
        for (int i = 0; i < moving.Count; i++)
            if (moving[i] != null && moving[i].GameplayEligible && !moving[i].AtMoveTarget)
                return false;
        return true;
    }

    void Finish()
    {
        sequence = null;
        moving.Clear();
        Action callback = onComplete;
        onComplete = null;
        callback?.Invoke();
    }

    void ReleaseIntroMovement(SprintDuel duel, bool settleAtDuelStart)
    {
        for (int i = 0; i < moving.Count; i++)
        {
            MatchPlayerState player = moving[i];
            if (player == null || player.MovePurpose != MatchMovePurpose.Q1Huddle) continue;
            if (settleAtDuelStart && duel != null && player.GameplayEligible)
                player.PlaceAt(duel.StartPositionFor(player.Team, player.transform));
            player.StopMove(MatchMovePurpose.Q1Huddle);
        }
    }

    public void Shutdown()
    {
        if (sequence != null) StopCoroutine(sequence);
        sequence = null;
        onComplete = null;
        ReleaseIntroMovement(null, false);
        moving.Clear();
    }
}
