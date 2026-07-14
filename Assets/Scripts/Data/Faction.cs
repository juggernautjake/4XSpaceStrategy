using System.Collections.Generic;
using UnityEngine;

// Who owns a body/system. Relation is relative to the player.
public enum FactionRelation { Player, Ally, Enemy, Neutral }

public class Faction
{
    public int id;
    public string name;
    public Color color;
    public FactionRelation relation;

    public Faction(int id, string name, Color color, FactionRelation relation)
    { this.id = id; this.name = name; this.color = color; this.relation = relation; }
}

// The fixed roster of factions. Ownership is stored as a Faction reference on bodies/systems
// (null == unclaimed / no owner).
public static class FactionManager
{
    static List<Faction> _all;

    public static Faction Player => Get(0);

    public static List<Faction> All { get { if (_all == null) Build(); return _all; } }

    public static Faction Get(int id)
    {
        if (_all == null) Build();
        return (id >= 0 && id < _all.Count) ? _all[id] : null;
    }

    static void Build()
    {
        _all = new List<Faction>
        {
            new Faction(0, "Your Empire",      new Color(0.35f, 1f, 0.45f),  FactionRelation.Player),
            new Faction(1, "The Concord",      new Color(0.35f, 0.7f, 1f),   FactionRelation.Ally),
            new Faction(2, "Vurl Dominion",    new Color(1f, 0.35f, 0.3f),   FactionRelation.Enemy),
            new Faction(3, "Ashen Legion",     new Color(1f, 0.55f, 0.25f),  FactionRelation.Enemy),
            new Faction(4, "Free Traders",     new Color(0.7f, 0.7f, 0.75f), FactionRelation.Neutral),
            new Faction(5, "The Hollow Court",  new Color(0.72f, 0.55f, 0.9f),FactionRelation.Neutral),
        };
    }

    // Colour for an owner (null owner -> unclaimed grey).
    public static Color OwnerColor(Faction f) => f != null ? f.color : new Color(0.5f, 0.5f, 0.55f);

    public static string OwnerName(Faction f) => f != null ? f.name : "Unclaimed";

    public static string OwnerLabel(Faction f)
    {
        if (f == null) return "Unclaimed";
        return $"{f.name} ({f.relation})";
    }
}
