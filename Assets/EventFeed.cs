using System.Collections.Generic;
using UnityEngine;
using TMPro;

// Rolling on-screen log of notable match events (goals, exclusions, turnovers, forfeit).
// Singleton; every caller null-checks Instance so the game runs fine without it.
public class EventFeed : MonoBehaviour
{
    public static EventFeed Instance { get; private set; }

    [Header("References")]
    [SerializeField] private TMP_Text feedText;
    [SerializeField] private MatchTimer matchTimer; // supplies the timestamp

    [Header("Settings")]
    [SerializeField] private int maxLines = 5;

    private readonly List<string> lines = new List<string>();

    void Awake() { Instance = this; }

    void Start()
    {
        if (feedText != null) feedText.text = "";
    }

    // Prepend "MM:SS  <text>" (newest on top); keep only the last `maxLines` lines.
    public void AddEvent(string text)
    {
        string stamp = matchTimer != null ? matchTimer.MatchTimeStamp() : "00:00";
        lines.Insert(0, stamp + "  " + text);

        int cap = Mathf.Max(1, maxLines);
        while (lines.Count > cap) lines.RemoveAt(lines.Count - 1);

        if (feedText != null) feedText.text = string.Join("\n", lines);
    }
}
