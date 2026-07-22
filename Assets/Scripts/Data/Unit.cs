using System.Collections.Generic;
using UnityEngine;

public enum UnitStatus { Idle, Traveling, Exploring, Colonizing, Returning, Researching }
public enum Rank { Rookie, Regular, Veteran, Elite, Legendary }

// A queued instruction for a ship. Every order has an optional target (a body) or a point in space.
// Move = travel there (then auto-do the class default on arrival); the others are explicit actions
// performed once the ship is at the target.
public enum OrderKind { Move, Survey, Research, Colonize, Terraform }

public class ShipOrder
{
    public OrderKind kind;
    public CelestialBody target;   // null when moving to a point in empty space
    public Vector3 point;
    public bool isPoint;

    public string Describe()
    {
        if (kind == OrderKind.Move) return isPoint ? "Move to deep space" : $"Move to {TargetName}";
        // OrderKind.Research is the DEEP SURVEY — the second, slower pass a research ship makes over a
        // world that has already been surveyed. The enum name is kept because its ordinal is serialized.
        if (kind == OrderKind.Research) return $"Deep Research {TargetName}";
        return $"{kind} {TargetName}";
    }
    string TargetName => target != null ? target.name : "?";
}

// A single ship. Carries its class, owner, current location, and its earned experience — the longer
// it serves and the more it accomplishes, the higher its rank (up to Legendary), which improves the
// stats tied to its role (research ships get better at research, warships at battle/survival).
public class Unit
{
    public int id;
    public string name;
    public UnitType type;
    public Faction owner;

    // Why this ship is not drawn — the same three reasons a world can be concealed for (see
    // Visibility.cs). This is the field a cloaking device writes: the ship keeps flying, keeps its
    // orders, keeps building and keeps being ticked; it simply is not rendered and cannot be clicked.
    // Not saved yet — a cloak is a state a tech will grant rather than something generation produces,
    // and there is no tech to grant it until that slice is built.
    public HideReason hideReason = HideReason.None;

    public CelestialBody location;         // where it currently is (null while mid-transit)
    public UnitStatus status = UnitStatus.Idle;

    // Experience & record.
    public float experience;
    public int battles;
    public float researchContributed;
    public int worldsExplored;
    public float serviceTime;               // seconds in service

    // Travel state.
    public CelestialBody travelTarget;      // null when moving to a point in empty space
    public float travelElapsed, travelDuration;
    public Vector3 travelFrom, travelTo;    // world positions for the moving token
    public Vector3 parkPosition;            // where it sits when idling in deep space
    public bool inSpace;                    // true when parked in space (no body)

    // Mission progress on the current world.
    public float missionTimer;              // limited stay on hostile worlds
    public float researchTimer;             // progress of an in-progress Research action (0..1)

    // Order queue. orders[0] is the active order; the rest are queued. When paused, the active order
    // still finishes but the queue does not advance to the next one.
    public List<ShipOrder> orders = new List<ShipOrder>();
    public bool queuePaused;

    // Ore samples collected by surveying but not yet researched (scouts can carry these back to a
    // world with a research centre to have them analysed).
    public List<int> samples = new List<int>();

    static readonly float[] RankXp = { 0f, 60f, 180f, 400f, 800f };

    public UnitInfo Info => UnitDatabase.Get(type);

    public Rank Rank
    {
        get
        {
            Rank r = Rank.Rookie;
            for (int i = 0; i < RankXp.Length; i++) if (experience >= RankXp[i]) r = (Rank)i;
            return r;
        }
    }

    public string RankName => Rank.ToString();
    public float RankMultiplier => 1f + (int)Rank * 0.15f;

    // Effective stats (rank boosts the role-relevant ones).
    public int EffectiveResearch => Mathf.RoundToInt(Info.research * (Info.canResearch ? RankMultiplier : 1f));
    public int EffectiveAttack => Mathf.RoundToInt(Info.attack * (Info.attack > 0 ? RankMultiplier : 1f));
    public int EffectiveHealth => Mathf.RoundToInt(Info.health * (1f + (int)Rank * 0.1f));
    public int Armor => Info.armor + (int)Rank;
    public int Speed => Info.speed;

    public void AddExperience(float xp)
    {
        experience += Mathf.Max(0f, xp);
    }

    public float TravelProgress => travelDuration > 0f ? Mathf.Clamp01(travelElapsed / travelDuration) : 1f;

    public string HoverText()
    {
        string ownerHex = "#" + ColorUtility.ToHtmlStringRGB(FactionManager.OwnerColor(owner));
        return $"<b>{name}</b>\n{Info.name} · <color=#FFD24D>{RankName}</color>\n" +
               $"<color={ownerHex}>{FactionManager.OwnerName(owner)}</color>";
    }
}
