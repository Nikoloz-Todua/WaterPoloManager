using UnityEngine;

public class TeamManager : MonoBehaviour
{
    [SerializeField] private PlayerMovement[] players; // drag Player and Player2 here

    private int activeIndex = 0;

    void Start()
    {
        SetActive(0); // start controlling the first player
    }

    void Update()
    {
        // press C to switch to the next player
        if (Input.GetKeyDown(KeyCode.C))
        {
            int next = (activeIndex + 1) % players.Length;
            SetActive(next);
        }
    }

    void SetActive(int index)
    {
        for (int i = 0; i < players.Length; i++)
            players[i].IsActive = (i == index);

        activeIndex = index;
    }
}