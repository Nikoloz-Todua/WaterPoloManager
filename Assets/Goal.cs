using UnityEngine;

public class Goal : MonoBehaviour
{
    [SerializeField] private string goalName = "Goal"; // e.g. "Left" or "Right"

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Ball"))
        {
            Debug.Log("GOAL scored in the " + goalName + " net!");

            other.attachedRigidbody.linearVelocity = Vector2.zero;
            other.attachedRigidbody.position = Vector2.zero; // reset ball to center
        }
    }
}