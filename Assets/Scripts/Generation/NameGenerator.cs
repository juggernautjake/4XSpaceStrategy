using System.Collections.Generic;
using UnityEngine;

// Generates large numbers of unique, pronounceable names for star systems, and derives guaranteed-
// unique planet, moon and star names from them. A shared registry ensures no two names collide within
// a single galaxy. Call Reset() once before generating a galaxy.
public static class NameGenerator
{
    static readonly HashSet<string> used = new HashSet<string>();

    // Real-ish star catalogue / constellation words.
    static readonly string[] Catalog =
    { "Kepler", "Cygnus", "Vega", "Tau Ceti", "Draconis", "Helios", "Orion", "Lyra", "Aquila", "Nyx",
      "Erebus", "Rhea", "Ymir", "Talos", "Zephyr", "Kestrel", "Corvus", "Mensa", "Pavo", "Indus",
      "Altair", "Rigel", "Antares", "Proxima", "Sirius", "Wolf", "Gliese", "Arcturus", "Deneb", "Mizar",
      "Alcor", "Polaris", "Castor", "Pollux", "Spica", "Capella", "Fomalhaut", "Achernar", "Hadar",
      "Mirfak", "Alphard", "Regulus", "Bellatrix", "Nashira", "Sabik", "Izar", "Vindemi", "Menkar",
      "Alnilam", "Saiph", "Tarazed", "Rasalgethi", "Sadr", "Enif", "Scheat", "Markab", "Algol", "Atria" };

    // Invented-word syllables for endless procedural names.
    static readonly string[] Syl1 =
    { "Xan", "Vor", "Zeph", "Kai", "Thal", "Nex", "Ory", "Cael", "Dra", "Fen", "Grim", "Hel", "Ish",
      "Jor", "Kry", "Lun", "Mor", "Nol", "Oph", "Pyr", "Quor", "Rho", "Syr", "Tyr", "Ur", "Vael",
      "Wyn", "Yth", "Zor", "Aeg", "Bel", "Cyn", "Dho", "Eir", "Fael", "Gor" };
    static readonly string[] Syl2 =
    { "ara", "eth", "ion", "oth", "ux", "yra", "ael", "orn", "ise", "und", "ath", "eon", "ovar",
      "essa", "ix", "olis", "andra", "ymir", "ora", "ent", "ius", "alis", "une", "ero", "aris", "os" };

    static readonly string[] Greek =
    { "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta", "Iota", "Kappa", "Lambda",
      "Sigma", "Tau", "Omega", "Prime", "Majoris", "Minoris" };

    static readonly string[] RomanNumerals =
    { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII" };

    public static void Reset() => used.Clear();

    // A unique system name. Tries several procedural forms, then falls back to a numeric suffix.
    public static string UniqueSystemName()
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            string n = RollSystemName();
            if (used.Add(n)) return n;
        }
        string baseN = RollSystemName();
        int k = 2;
        while (!used.Add($"{baseN} {k}")) k++;
        return $"{baseN} {k}";
    }

    static string RollSystemName()
    {
        switch (Random.Range(0, 3))
        {
            case 0: // Catalogue + designation, e.g. "Vega Beta", "Kepler-231"
                return Random.value < 0.5f
                    ? $"{Pick(Catalog)} {Pick(Greek)}"
                    : $"{Pick(Catalog)}-{Random.Range(1, 999)}";
            case 1: // Invented word, e.g. "Xanara", "Vorion Prime"
                string w = Pick(Syl1) + Pick(Syl2);
                return Random.value < 0.3f ? $"{w} {Pick(Greek)}" : w;
            default: // Catalogue word alone, e.g. "Antares"
                return Pick(Catalog);
        }
    }

    // "<System> <Roman>" — unique because the system name is unique.
    public static string PlanetName(string systemName, int index) => $"{systemName} {Roman(index)}";

    // "<Planet><letter>" — unique because the planet name is unique.
    public static string MoonName(string planetName, int index) => $"{planetName}{(char)('a' + index)}";

    // A single sun takes the system name; multiples get A/B/C suffixes.
    public static string StarName(string systemName, int index, int count)
        => count <= 1 ? systemName : $"{systemName} {(char)('A' + index)}";

    // --- Galaxy names -------------------------------------------------------------------------

    // Evocative rather than catalogue-like: the galaxy is the one name the player reads at the widest
    // zoom, sitting under a slowly turning spiral, so it wants to sound like a place and not a serial
    // number. Systems already carry the "Kepler-231" register; this is the counterweight to it.
    static readonly string[] GalaxyWord =
    { "Aureth", "Vantara", "Ceruleth", "Ossian", "Halcyon", "Ithrane", "Sable", "Verrin", "Cassaris",
      "Umbral", "Solenne", "Tenebris", "Aurelian", "Mirovar", "Kaleth", "Zephyrine", "Obsidia", "Lucent",
      "Nyxara", "Thessaly", "Amaranth", "Corvine", "Elysia", "Fathom", "Gossamer", "Hollow", "Ivory" };

    // The shape-word. Deliberately mixed between structures ("Spiral", "Wheel") and moods ("Veil",
    // "Hollow") so two galaxies rarely read as the same kind of object.
    static readonly string[] GalaxyForm =
    { "Spiral", "Veil", "Wheel", "Coil", "Expanse", "Reach", "Cascade", "Maelstrom", "Drift", "Whorl",
      "Cradle", "Shroud", "Bloom", "Tide", "Crown", "Abyss", "Halo", "Lattice" };

    public static string GalaxyName()
    {
        switch (Random.Range(0, 4))
        {
            case 0:  return $"The {Pick(GalaxyWord)} {Pick(GalaxyForm)}";   // "The Aureth Veil"
            case 1:  return $"{Pick(GalaxyWord)} {Pick(GalaxyForm)}";       // "Ceruleth Spiral"
            case 2:  return $"{Pick(GalaxyWord)}'s {Pick(GalaxyForm)}";     // "Ossian's Maelstrom"
            default: return $"{Pick(GalaxyWord)}-{Random.Range(1, 99)}";    // "Vantara-47"
        }
    }

    static string Pick(string[] a) => a[Random.Range(0, a.Length)];

    static string Roman(int i) => (i >= 0 && i < RomanNumerals.Length) ? RomanNumerals[i] : (i + 1).ToString();
}
