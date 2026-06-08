using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float holdMoveSpeed = 1.5f;

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
    }

    void FixedUpdate()
{
    if (!IsActive) return; // when not controlled, let the AI drive this player
    float speed = isHolding ? holdMoveSpeed : moveSpeed;
    rb.linearVelocity = input * speed;
}

    void LateUpdate()
    {
        if (isHolding && ball != null)
        {
            ball.position = (Vector2)transform.position + lastDirection * holdOffset;
            ball.linearVelocity = Vector2.zero;
        }
    }

    void TryGrabBall()
    {
        if (ball == null) return;
        if (Vector2.Distance(transform.position, ball.position) <= grabDistance)
        {
            isHolding = true;
            ball.simulated = false;
        }
    }

    void DropBall()
    {
        if (ball == null) return;
        isHolding = false;
        ball.simulated = true;
        ball.linearVelocity = Vector2.zero;
    }

    void Shoot()
    {
        if (ball == null) return;
        isHolding = false;
        ball.simulated = true;
        ball.linearVelocity = Vector2.zero;
        ball.AddForce(lastDirection * currentPower, ForceMode2D.Impulse);
    }

    public void ReleaseBall()
    {
        isHolding = false;
        if (ball != null) ball.simulated = true;
    }
}