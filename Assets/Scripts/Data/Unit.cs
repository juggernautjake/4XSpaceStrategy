using UnityEngine;

public enum UnitStatus { Idle, Traveling, Exploring, Colonizing, Returning }
public enum Rank { Rookie, Regular, Veteran, Elite, Legendary }

// A single ship. Carries its class, owner, current location, and its earned experience — the longer
// it serves and the more it accomplishes, the higher its rank (up to Legendary), which improves the
// stats tied to its role (research ships get better at research, warships at battle/survival).
public class Unit
{
    public int id;
    public string name;
    public UnitType type;
    public Faction owner;

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
