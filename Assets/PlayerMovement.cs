using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float holdMoveSpeed = 2f;

    [Header("Ball")]
    [SerializeField] private Rigidbody2D ball;
    [SerializeField] private float grabDistance = 1.0f;
    [SerializeField] private float holdOffset = 0.6f;

    [Header("Shooting")]
    [SerializeField] private float maxShootPower = 12f;
    [SerializeField] private float chargeRate = 8f;

    [Header("Visual Feedback")]
    [SerializeField] private Color holdingColor = Color.green;
    [SerializeField] private Color activeColor = Color.red;
    [SerializeField] private Color inactiveColor = Color.gray;

    [Header("Aim line")]
    [SerializeField] private LineRenderer aimLine;
    [SerializeField] private float aimLineLength = 2.5f;

    public bool IsActive = false;
    public bool IsHolding => isHolding;
    public Vector2 Facing => lastDirection;

    private Rigidbody2D rb;
    private SpriteRenderer sprite;
    private Vector2 input;
    private Vector2 lastDirection = Vector2.up;
    private float currentPower = 0f;
    private bool isHolding = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sprite = GetComponent<SpriteRenderer>();
        if (aimLine != null) aimLine.enabled = false;
    }

    void Update()
    {
        if (IsActive)
        {
            float x = Input.GetAxisRaw("Horizontal");
            float y = Input.GetAxisRaw("Vertical");
            input = new Vector2(x, y).normalized;

            if (input != Vector2.zero)
                lastDirection = input;

            if (Input.GetKeyDown(KeyCode.E))
            {
                if (isHolding) DropBall();
                else TryGrabBall();
            }

            if (isHolding)
            {
                if (Input.GetKey(KeyCode.Space))
                    currentPower = Mathf.Min(currentPower + chargeRate * Time.deltaTime, maxShootPower);

                if (Input.GetKeyUp(KeyCode.Space))
                {
                    Shoot();
                    currentPower = 0f;
                }
            }
        }
        else
        {
            input = Vector2.zero;
        }

        if (sprite != null)
        {
            if (isHolding) sprite.color = holdingColor;
            else sprite.color = IsActive ? activeColor : inactiveColor;
        }

        UpdateAimLine();
    }

    void FixedUpdate()
    {
        if (!IsActive) return;
        float speed = isHolding ? holdMoveSpeed : moveSpeed;
        rb.linearVelocity = input * speed;
    }

    void UpdateAimLine()
    {
        if (aimLine == null) return;

        bool show = isHolding;
        aimLine.enabled = show;
        if (!show) return;

        Vector3 start = transform.position;
        Vector3 end = start + (Vector3)(lastDirection * aimLineLength);
        aimLine.SetPosition(0, start);
        aimLine.SetPosition(1, end);
    }

    void TryGrabBall()
    {
        if (ball == null) return;
        // can only grab a LOOSE ball (cannot steal from a holder)
        if (MatchContext.Instance != null && !MatchContext.Instance.BallIsLoose) return;

        if (Vector2.Distance(transform.position, ball.position) <= grabDistance)
        {
            GrabBall();
        }
    }

    void GrabBall()
    {
        isHolding = true;
        ball.simulated = false;
        ball.linearVelocity = Vector2.zero;
        ball.transform.SetParent(transform);
        ball.transform.localPosition = (Vector3)(lastDirection * holdOffset);

        if (MatchContext.Instance != null)
            MatchContext.Instance.SetPossession(MatchContext.Instance.PlayerTeam);
    }

    void DropBall()
    {
        if (ball == null) return;
        isHolding = false;
        ball.transform.SetParent(null);
        ball.simulated = true;
        ball.linearVelocity = Vector2.zero;

        if (MatchContext.Instance != null)
            MatchContext.Instance.SetPossession(null);
    }

    void Shoot()
    {
        if (ball == null) return;
        isHolding = false;
        ball.transform.SetParent(null);
        ball.simulated = true;
        ball.linearVelocity = Vector2.zero;
        ball.AddForce(lastDirection * currentPower, ForceMode2D.Impulse);

        if (MatchContext.Instance != null)
            MatchContext.Instance.SetPossession(null);
    }

    public void ReleaseBall()
    {
        isHolding = false;
        if (ball != null)
        {
            ball.transform.SetParent(null);
            ball.simulated = true;
        }

        if (MatchContext.Instance != null)
            MatchContext.Instance.SetPossession(null);
    }

    void LateUpdate()
    {
        if (isHolding && ball != null)
        {
            ball.transform.localPosition = (Vector3)(lastDirection * holdOffset);
        }
    }
}