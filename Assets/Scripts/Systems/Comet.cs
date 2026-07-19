using System.Collections.Generic;
using UnityEngine;

// A comet crossing the galaxy — a bright head with a trailing tail, sweeping through on a straight path and
// then gone. Most are only ice and dust; some are laced with rare metals or fragments of lost technology;
// a rare one carries a stray echo of the Vael; and a very few carry something that will make you laugh.
public class Comet
{
    public enum Payload { Nothing, Materials, Technology, EasterEgg, Lore }

    public Vector3 pos;        // current position, local to SystemParent (galaxy space)
    public Vector3 dir;        // unit travel direction
    public float speed;
    public float size;         // head radius
    public bool studied;

    public Payload payload;
    public int metal, energy, research;
    public string flavor;      // what a study turns up (the funny egg, the reveal, etc.)

    [System.NonSerialized] public GameObject visual;
    [System.NonSerialized] public readonly HashSet<int> announced = new HashSet<int>();

    public string SizeWord => size > 3.3f ? "A colossal" : size > 2.1f ? "A great" : "A large";
    public string RevealText => string.IsNullOrEmpty(flavor) ? "Ice, dust, and nothing more." : flavor;

    // Roll what this comet is carrying. Weighted so plenty are duds — the tantalising fly-through notice is
    // the same for all of them, so you never know until you look.
    public static void Roll(Comet c)
    {
        float r = Random.value;
        if (r < 0.40f)
        {
            c.payload = Payload.Nothing;
            c.research = Random.Range(0, 6);
            c.flavor = "A magnificent, dirty snowball — and nothing more. Beautiful, though.";
        }
        else if (r < 0.72f)
        {
            c.payload = Payload.Materials;
            c.metal = Random.Range(90, 280);
            c.energy = Random.Range(80, 240);
            c.research = Random.Range(10, 45);
            c.flavor = "Its ice was veined with rare metals and volatile fuels — a genuine windfall.";
        }
        else if (r < 0.88f)
        {
            c.payload = Payload.Technology;
            c.research = Random.Range(70, 180);
            c.flavor = "Frozen in the nucleus: fragments of a device no one on your team can yet explain. " +
                       "The physicists have not slept in days.";
        }
        else if (r < 0.965f)
        {
            c.payload = Payload.EasterEgg;
            c.metal = Random.Range(1, 12);
            c.flavor = Eggs[Random.Range(0, Eggs.Length)];
        }
        else
        {
            c.payload = Payload.Lore;
            c.flavor = Lore[Random.Range(0, Lore.Length)];
        }
    }

    // The funny ones. Studying one of these makes the whole comet worth it, if only for the report.
    static readonly string[] Eggs =
    {
        "Deep in the ice, your scanners resolve a frozen sign a kilometre tall. It reads: \"BEWARE OF THE LEOPARD.\"",
        "On closer inspection the comet is a very old, very cold sandwich the size of a small moon. No one on the crew will admit to making it.",
        "Etched into the nucleus, in letters you can read from orbit: \"WASH ME.\"",
        "You catch the comet. Inside is one (1) rubber duck, perfectly preserved, and a note that says only: \"you found me :)\"",
        "The ice yields a single frozen garden gnome. It stares back with infinite patience. You leave it exactly where it is.",
        "Your crew reports, with some concern, that the comet smells faintly and impossibly of grilled onions.",
        "Inside the coma, a message in a bottle. It reads, in full: \"wrong number.\"",
        "The comet is hollow. Inside is a smaller comet. You elect, unanimously, not to open that one.",
        "Painted across the nucleus in enormous cheerful letters: \"FREE HUGS.\" There is no one out here to hug.",
        "It is not a comet. It is nine hundred billion tonnes of frozen peas travelling at eleven kilometres a second. You do not ask why. You are afraid to ask why.",
    };

    // Bonus echoes of the Vael — flavour, not one of the ten Codex fragments (a comet is no place to keep a
    // relic you'd want to find again).
    static readonly string[] Lore =
    {
        "Riding the ice, a shard of Vael glass still warm to the touch whispers: \"even light grows tired, seeker. Even we did.\"",
        "The comet carries an older comet's memory. A Vael voice, thin as frost: \"we sent these ahead of us, so that something of us would keep moving after we stopped.\"",
        "Frozen in the heart of it, a single Vael word repeats without end. Your linguists render it, at last, as: \"almost.\"",
    };
}
