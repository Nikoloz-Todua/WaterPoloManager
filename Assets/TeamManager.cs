using UnityEngine;

public class TeamManager : MonoBehaviour
{
    [SerializeField] private PlayerMovement[] players;
    [SerializeField] private Rigidbody2D ball;
    [SerializeField] private float passSpeedFactor = 3f;
    [SerializeField] private float minPassSpeed = 5f;
    [SerializeField] private float maxPassSpeed = 14f;

    private int activeIndex = 0;

    void Start()
    {
        SetActive(0);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.C))
        {
            int next = (activeIndex + 1) % players.Length;
            SetActive(next);
        }

        if (Input.GetKeyDown(KeyCode.B))
        {
            TryPass();
        }
    }

    void TryPass()
    {
        if (ball == null) return;

        PlayerMovement passer = players[activeIndex];
        if (!passer.IsHolding) return;

        int targetIndex = -1;
        float bestDist = Mathf.Infinity;
        for (int i = 0; i < players.Length; i++)
        {
            if (i == activeIndex) continue;
            float d = Vector2.Distance(passer.transform.position, players[i].transform.position);
            if (d < bestDist) { bestDist = d; targetIndex = i; }
        }
        if (targetIndex == -1) return;

        PlayerMovement receiver = players[targetIndex];
        Vector2 dir = ((Vector2)receiver.transform.position - (Vector2)passer.transform.position).normalized;

        passer.ReleaseBall();
        float speed = Mathf.Clamp(bestDist * passSpeedFactor, minPassSpeed, maxPassSpeed);
        ball.linearVelocity = dir * speed;

        SetActive(targetIndex);
    }

    void SetActive(int index)
    {
        for (int i = 0; i < players.Length; i++)
            players[i].IsActive = (i == index);
        activeIndex = index;
    }
}