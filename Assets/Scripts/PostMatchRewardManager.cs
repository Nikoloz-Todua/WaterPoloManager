using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Post-match reward slots — Clash-Royale style. Self-bootstrapping singleton, same pattern as
// RosterManager (DontDestroyOnLoad, JSON persistence in persistentDataPath, own file
// rewardSlots.json — earned rewards must survive a relaunch, unlike session-only LeagueSeason).
//
// 4 slots. Finishing a match rolls a CardTier (Common 80% / Rare 16% / Epic 3.5% / Legendary 0.5%)
// into the first empty slot in state Locked ("TAP TO UNLOCK" — the timer does NOT start yet).
// All slots full → the drop is silently skipped. Tapping a Locked slot (hub popup) starts the
// unlock; when the tier's duration elapses the slot reads Ready and can be opened via
// CardPack.OpenTierPack. Unlock timing uses UtcNow ticks so it keeps counting while the app is
// closed. Only MatchTimer's FULL TIME path adds a reward (forfeits don't).
public class PostMatchRewardManager : MonoBehaviour
{
    public enum SlotState { Empty, Locked, Unlocking } // "Ready" is derived: Unlocking + elapsed

    [Serializable]
    public class Slot
    {
        public int state;            // SlotState as int (JsonUtility-friendly)
        public int tier;             // CardTier as int
        public long unlockStartTicks;

        public SlotState State => (SlotState)state;
        public CardTier Tier => (CardTier)tier;
    }

    [Serializable]
    class SaveData { public Slot[] slots = { new Slot(), new Slot(), new Slot(), new Slot() }; }

    public const int SlotCount = 4;
    const string SaveFileName = "rewardSlots.json";

    private static PostMatchRewardManager instance;
    public static PostMatchRewardManager Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject("PostMatchRewardManager");
                instance = go.AddComponent<PostMatchRewardManager>();
            }
            return instance;
        }
    }

    private SaveData data;
    private string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    void Load()
    {
        data = null;
        if (File.Exists(SavePath))
        {
            try { data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath)); }
            catch (Exception e) { Debug.LogWarning("PostMatchRewardManager: save unreadable, recreating. " + e.Message); }
        }
        if (data == null) data = new SaveData();
        if (data.slots == null || data.slots.Length != SlotCount)
        {
            Slot[] fixedSlots = { new Slot(), new Slot(), new Slot(), new Slot() };
            for (int i = 0; data.slots != null && i < data.slots.Length && i < SlotCount; i++)
                if (data.slots[i] != null) fixedSlots[i] = data.slots[i];
            data.slots = fixedSlots;
        }
    }

    void Save()
    {
        try { File.WriteAllText(SavePath, JsonUtility.ToJson(data, true)); }
        catch (Exception e) { Debug.LogWarning("PostMatchRewardManager: could not save. " + e.Message); }
    }

    // ----------------------------------------------------------------- queries

    public Slot GetSlot(int i) => (i >= 0 && i < SlotCount) ? data.slots[i] : null;

    public bool IsReady(int i)
    {
        Slot s = GetSlot(i);
        return s != null && s.State == SlotState.Unlocking && SecondsRemaining(i) <= 0;
    }

    public double SecondsRemaining(int i)
    {
        Slot s = GetSlot(i);
        if (s == null || s.State != SlotState.Unlocking) return 0;
        double total = CardPack.GetTierPack(s.Tier).unlockHours * 3600.0;
        double elapsed = (DateTime.UtcNow - new DateTime(s.unlockStartTicks, DateTimeKind.Utc)).TotalSeconds;
        return Math.Max(0, total - elapsed);
    }

    public static string FormatRemaining(double seconds)
    {
        TimeSpan t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (t.TotalHours >= 1) return (int)t.TotalHours + "H " + t.Minutes + "M";
        if (t.TotalMinutes >= 1) return t.Minutes + "M " + t.Seconds + "S";
        return t.Seconds + "S";
    }

    // Another slot is already counting down? (Clash rule: one unlock at a time.)
    public bool AnyUnlocking()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            Slot s = data.slots[i];
            if (s.State == SlotState.Unlocking && !IsReady(i)) return true;
        }
        return false;
    }

    // ----------------------------------------------------------------- mutations

    // Called by MatchTimer at full time: roll a tier, drop it into the first empty slot (Locked).
    public void AddRewardForMatch()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (data.slots[i].State != SlotState.Empty) continue;
            data.slots[i].state = (int)SlotState.Locked;
            data.slots[i].tier = (int)RollTier();
            data.slots[i].unlockStartTicks = 0;
            Save();
            return;
        }
        // all 4 full → silently skip (no queue/overflow by design)
    }

    static CardTier RollTier()
    {
        float r = UnityEngine.Random.value;
        if (r < 0.005f) return CardTier.Legendary; // 0.5%
        if (r < 0.04f) return CardTier.Epic;       // 3.5%
        if (r < 0.20f) return CardTier.Rare;       // 16%
        return CardTier.Common;                    // 80%
    }

    // "START UNLOCKING" pressed on a Locked slot's popup.
    public bool StartUnlock(int i)
    {
        Slot s = GetSlot(i);
        if (s == null || s.State != SlotState.Locked) return false;
        s.state = (int)SlotState.Unlocking;
        s.unlockStartTicks = DateTime.UtcNow.Ticks;
        Save();
        return true;
    }

    // Tap on a Ready slot: open the pack, grant the cards, clear the slot. Returns the grant
    // results for the reveal UI (null if the slot wasn't ready).
    public List<CardPack.GrantResult> OpenSlot(int i)
    {
        if (!IsReady(i)) return null;
        CardTier tier = GetSlot(i).Tier;
        data.slots[i].state = (int)SlotState.Empty;
        data.slots[i].unlockStartTicks = 0;
        Save();
        return CardPack.GrantAll(CardPack.OpenTierPack(tier));
    }
}
