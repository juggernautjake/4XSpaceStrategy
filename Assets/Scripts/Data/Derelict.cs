using System.Collections.Generic;
using UnityEngine;

// A super-old, broken station left drifting about the galaxy — a relic of the Vael, hidden in odd places:
// far out past a system's planets, sitting still in the dead space between stars, hugging a system's sun,
// or circling the galactic black hole. Some hold a fragment of the Vael's message; the rest hold salvage —
// lost materials or half-understood technology worth recovering.
public class Derelict
{
    public enum Orbit { FarOut, DeadSpace, StarHugging, BlackHole }

    public int id;
    public int systemIndex = -1;      // the system it belongs to (index into galaxy.systems); -1 for dead space
    public Orbit orbit;

    public Vector3 deadSpacePos;      // DeadSpace: absolute galaxy position it sits at, dead still
    public float orbitRadius;         // FarOut / StarHugging / BlackHole: distance from what it circles
    public float orbitPhase;
    public float orbitSpeed;          // deg/sec (slow)

    public int clueIndex = -1;        // a Vael fragment (0..9), or -1 for a salvage derelict
    public int rewardMetal, rewardEnergy, rewardResearch;   // salvage (only when clueIndex < 0)
    public bool studied;

    [System.NonSerialized] public GameObject visual;

    public string Kind => orbit switch
    {
        Orbit.FarOut => "adrift in the far reaches",
        Orbit.DeadSpace => "becalmed in dead space",
        Orbit.StarHugging => "skimming the fire of a sun",
        Orbit.BlackHole => "circling the black hole",
        _ => "adrift"
    };
}

// Scatters a handful of derelicts across a freshly generated galaxy, guaranteeing the four distinct odd
// orbits appear. Salvage rewards are rolled here; which derelicts hold a Vael fragment is decided later by
// AncientClues.SeedGalaxy (so the ten fragments are shared across worlds AND derelicts).
public static class DerelictGen
{
    public static void Populate(Galaxy galaxy)
    {
        if (galaxy == null || galaxy.systems == null || galaxy.systems.Count == 0) return;
        galaxy.derelicts.Clear();

        // A few more derelicts than there are "clue slots" they might take, so salvage ones exist too.
        int count = Mathf.Clamp(4 + galaxy.systems.Count / 2, 4, 9);
        int id = 0;

        // Guarantee one of each odd orbit first, then fill with random styles.
        var styles = new List<Derelict.Orbit>
        {
            Derelict.Orbit.FarOut, Derelict.Orbit.DeadSpace, Derelict.Orbit.StarHugging, Derelict.Orbit.BlackHole
        };
        while (styles.Count < count)
            styles.Add((Derelict.Orbit)Random.Range(0, 4));

        foreach (var style in styles)
        {
            var d = new Derelict { id = id++, orbit = style };
            Place(d, galaxy);
            RollSalvage(d);
            galaxy.derelicts.Add(d);
        }
    }

    static void Place(Derelict d, Galaxy galaxy)
    {
        switch (d.orbit)
        {
            case Derelict.Orbit.DeadSpace:
                // Sit dead still somewhere between two systems.
                d.systemIndex = -1;
                if (galaxy.systems.Count >= 2)
                {
                    var a = galaxy.systems[Random.Range(0, galaxy.systems.Count)].galaxyPosition;
                    var b = galaxy.systems[Random.Range(0, galaxy.systems.Count)].galaxyPosition;
                    d.deadSpacePos = Vector3.Lerp(a, b, Random.Range(0.35f, 0.65f))
                                   + new Vector3(Random.Range(-30f, 30f), 0f, Random.Range(-30f, 30f));
                }
                else d.deadSpacePos = galaxy.systems[0].galaxyPosition + new Vector3(80f, 0f, 40f);
                break;

            case Derelict.Orbit.BlackHole:
                d.systemIndex = -1;   // circles the galactic centre, not a system
                d.orbitRadius = Random.Range(60f, 140f);
                d.orbitPhase = Random.Range(0f, 360f);
                d.orbitSpeed = Random.Range(1.5f, 4f);
                break;

            case Derelict.Orbit.StarHugging:
                d.systemIndex = PickSystem(galaxy);
                d.orbitRadius = 0f;   // resolved at render time from the star's actual size (very close in)
                d.orbitPhase = Random.Range(0f, 360f);
                d.orbitSpeed = Random.Range(8f, 16f);   // fast, skimming the star
                break;

            default: // FarOut
                d.systemIndex = PickSystem(galaxy);
                d.orbitRadius = 0f;   // resolved at render time to well past the outermost planet
                d.orbitPhase = Random.Range(0f, 360f);
                d.orbitSpeed = Random.Range(1f, 3f);    // slow, far out
                break;
        }
    }

    static int PickSystem(Galaxy galaxy)
    {
        // Prefer a non-home system so the derelict is something to go and find.
        for (int tries = 0; tries < 8; tries++)
        {
            int i = Random.Range(0, galaxy.systems.Count);
            if (i != galaxy.homeIndex) return i;
        }
        return Random.Range(0, galaxy.systems.Count);
    }

    static void RollSalvage(Derelict d)
    {
        // Meaningful but not game-breaking salvage. Overwritten (ignored) if this derelict is later given a
        // Vael fragment instead.
        d.rewardMetal = Random.Range(60, 220);
        d.rewardEnergy = Random.Range(60, 220);
        d.rewardResearch = Random.Range(20, 90);
    }
}
