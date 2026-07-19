using System.Collections.Generic;
using UnityEngine;

// The scattered voice of the Vael — a vanished, star-faring people whose technology outstripped anything
// the galaxy has seen since. Exactly ten worlds carry a fragment of their message; survey and then deeply
// study such a world and its fragment is revealed in the Vael Codex (a specially-styled viewer). Gather
// all ten and the whole of their making is granted to your empire.
//
// Deliberately its own tiny system (modelled on AncientLore's Reset/Export/Import shape): a set of found
// clue indices, plus the authored messages. Which worlds carry a clue is stored on the body (clueIndex);
// which clues you have recovered is stored here.
public static class AncientClues
{
    public const int Total = 10;
    public const string CivName = "the Vael";

    public struct Clue
    {
        public string title;   // the fragment's name
        public string body;    // the cryptic message itself
    }

    // The ten fragments, in the order they are meant to be read — a welcome, then wisdom, then warning,
    // then the final invitation. (You will rarely find them in order; the Codex lets you read them in it.)
    static readonly Clue[] Clues =
    {
        new Clue { title = "The First Mark", body =
            "Reader of buried light: you were not the first to ache to cross the dark. We crossed it. We are " +
            "the road you now walk, worn smooth by feet that are long since dust. Welcome, seeker — grief " +
            "travels with knowledge the way a shadow travels with a flame." },

        new Clue { title = "On Knowing", body =
            "Every answer is a country with no way back across its border. You have already left the shore of " +
            "not-knowing; look how small it has become behind you. You cannot un-see. You cannot turn back. " +
            "Consider this before you take the next step." },

        new Clue { title = "The Listening Void", body =
            "We charted the emptiness between the worlds and found it was not empty, only patient. The universe " +
            "keeps no secrets, seeker. It merely waits to see who is worthy of the question." },

        new Clue { title = "The Price of Fire", body =
            "We lit our minds like suns and burned away our own long night. Understand what we did not: a sun " +
            "does not choose what it consumes. Wisdom is never free. It is only unpaid — for now." },

        new Clue { title = "The Great Silence", body =
            "You have wondered why the stars are so quiet, why no voice answers yours. We are the answer. We " +
            "climbed the highest tower of knowing, and from its summit we saw why the wise fall silent." },

        new Clue { title = "The Mirror of Stars", body =
            "Every people who reach far enough meet themselves coming back. What you hunt out there among the " +
            "suns, seeker, you will find is a reflection. Be certain, before you arrive, that you can bear to look." },

        new Clue { title = "The Threshold", body =
            "There is a door woven into the fabric of things. We found it. We opened it. What waited there cannot " +
            "be told, only survived — and not all of us survived even the telling of it to ourselves." },

        new Clue { title = "The Weight of Forever", body =
            "We conquered death and named it our triumph. Then we lived long enough to learn what death had been " +
            "sparing us from. Be careful, traveller, which mercies you refuse." },

        new Clue { title = "The Last Warning", body =
            "Turn back. We say this knowing you will not, for we did not. The road through the stars has an end, " +
            "and the end has a name, and to speak the name is to arrive. You are so much closer than you know." },

        new Clue { title = "The Invitation", body =
            "You have gathered our scattered voice and made it whole again. That was the test — not strength, but " +
            "persistence; not conquest, but attention. Take now what we could not carry past the threshold: the " +
            "whole of our making, freely given. Finish the road. Learn what we learned. And then, seeker — decide " +
            "what you will do with the silence." },
    };

    public static int Count => Clues.Length;
    public static Clue Get(int index) => Clues[Mathf.Clamp(index, 0, Clues.Length - 1)];

    static readonly HashSet<int> _found = new HashSet<int>();
    public static event System.Action OnChanged;

    public static int FoundCount => _found.Count;
    public static bool IsFound(int index) => _found.Contains(index);
    public static bool AllFound => _found.Count >= Total;

    public static void Reset()
    {
        _found.Clear();
        OnChanged?.Invoke();
    }

    // Called when a clue-bearing world has been both surveyed AND deeply studied (UnitManager.DoDeepResearch).
    public static void Reveal(CelestialBody b)
    {
        if (b == null) return;
        RevealIndex(b.clueIndex, b.name);
    }

    // Reveal a specific fragment from any source (a world, a derelict station, …). Fires the specially-styled
    // notification + the Codex viewer, and — when the last of the ten is in hand — the all-ten reward.
    public static void RevealIndex(int idx, string sourceName)
    {
        if (idx < 0 || idx >= Total) return;
        if (_found.Contains(idx)) return;          // already recovered

        _found.Add(idx);
        var clue = Get(idx);

        // (Push plays the NotifKind.Ancient chime itself.)
        NotificationManager.Instance?.Push(
            $"A Voice of {CivName} — {clue.title}",
            $"A fragment of {CivName} surfaces at {sourceName}.  ({FoundCount}/{Total} recovered)",
            () => AncientClueWindow.Instance?.Show(idx),
            NotifKind.Ancient,
            clue.body);

        AncientClueWindow.Instance?.Show(idx);     // open the Codex on the newly-found fragment
        OnChanged?.Invoke();

        if (AllFound) GrantReward();
    }

    // Gathering all ten grants the Vael's whole making — a broad, permanent surge folded into the tech
    // effects (see TechManager.Recompute), announced with a victory notice.
    static void GrantReward()
    {
        TechManager.Recompute();                   // apply the passive Vael Legacy bonus now
        NotificationManager.Instance?.Push(        // Push plays the Victory fanfare itself
            "THE VAEL LEGACY IS YOURS",
            $"You have gathered all ten voices of {CivName} and made them whole. Their whole making now flows " +
            "through your empire — research, industry, reach and terraforming surge with borrowed wisdom. The " +
            "road continues, seeker. What lies at its end is yours to find.",
            () => AncientClueWindow.Instance?.Show(Total - 1),
            NotifKind.Victory,
            "+60% research · +50% ore yield · +60% ship range · +60% terraforming speed · faster, cheaper construction.");
    }

    // ---- Generation: guarantee ten clue sources across the galaxy, spread over WORLDS and DERELICTS ----
    // A "source" is anything that can hold a fragment — a solid unclaimed planet or moon, or an ancient
    // derelict station. Each gets a setter that stamps its clue index; we shuffle them all together and hand
    // out the ten fragments, so a run's clues might sit on moons, planets and drifting stations alike.
    public static void SeedGalaxy(Galaxy galaxy)
    {
        if (galaxy == null) return;

        var setters = new List<System.Action<int>>();

        foreach (var sys in galaxy.systems)
        {
            if (sys == null) continue;
            foreach (var b in sys.AllBodies())
            {
                if (b == null) continue;
                b.clueIndex = -1;   // clear any prior seeding
                // Solid, unclaimed worlds/moons only — a clue must be FOUND (surveyed + studied), so it can't
                // sit on the player's already-known home world, and a gas giant has no surface for it.
                if (b.type == CelestialBodyType.GasGiant || b.type == CelestialBodyType.Asteroid) continue;
                if (b.owner != null) continue;
                var cap = b;
                setters.Add(i => cap.clueIndex = i);
            }
        }

        if (galaxy.derelicts != null)
            foreach (var d in galaxy.derelicts)
            {
                if (d == null) continue;
                d.clueIndex = -1;
                var cap = d;
                setters.Add(i => cap.clueIndex = i);
            }

        for (int i = setters.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (setters[i], setters[j]) = (setters[j], setters[i]);
        }
        int n = Mathf.Min(Total, setters.Count);
        for (int i = 0; i < n; i++) setters[i](i);
    }

    // ---- Save / load ----
    public static List<int> Export() => new List<int>(_found);

    public static void Import(List<int> found)
    {
        _found.Clear();
        if (found != null)
            foreach (var i in found)
                if (i >= 0 && i < Total) _found.Add(i);
        // If a loaded game already has all ten, make sure the passive reward is applied.
        if (AllFound) TechManager.Recompute();
        OnChanged?.Invoke();
    }
}
