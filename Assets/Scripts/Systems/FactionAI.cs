using System.Collections.Generic;
using UnityEngine;

// The living civilisations of the galaxy. Every non-player faction is given a RACE (one of the playable
// species) and a PERSONALITY derived from that race — how fast it breeds, how eagerly it grabs new
// worlds, and how harsh a world it's willing to settle. From a seeded homeworld each civilisation then
// grows and spreads SLOWLY and NATURALLY on its own: its settled worlds' populations climb toward a
// carrying capacity, and every so often it colonises the best habitable unclaimed world it can reach.
//
// Deliberately self-contained: it does NOT touch the player's ColonyManager / PlayerEconomy (which stay
// player-only). It owns the whole life of the AI factions — their species lens, their growth, their
// expansion — so nothing here has to reach into the player-hardwired economy stack.
public class FactionAI : MonoBehaviour
{
    public static FactionAI Instance;

    // A civilisation's race + personality. Persisted (see GameStateSerializer) so a reload keeps each
    // faction's character and expansion clock exactly where it was.
    public class Profile
    {
        public int factionId;
        public int speciesIndex;     // which race (SpeciesDatabase index)
        public float expansionism;   // 0..1: how eagerly it colonises (shorter cooldown, roams further)
        public float growthDrive;    // ~0.6..1.6: population growth-rate multiplier
        public float hardiness;      // the minimum habitability (%) it will settle — hardy races go lower
        public float expandCooldown; // seconds until its next colonisation attempt
        public string temperament;   // flavour label for the UI
    }

    static readonly Dictionary<int, Profile> _profiles = new Dictionary<int, Profile>();

    public static Profile ProfileFor(Faction f) => f != null && _profiles.TryGetValue(f.id, out var p) ? p : null;
    public static Species SpeciesOf(Faction f)
    {
        var p = ProfileFor(f);
        return p != null ? SpeciesDatabase.Get(p.speciesIndex) : null;
    }

    // A one-line "who they are" for the UI: race + temperament.
    public static string Describe(Faction f)
    {
        var p = ProfileFor(f);
        if (p == null) return null;
        var sp = SpeciesDatabase.Get(p.speciesIndex);
        return $"{sp.name} · {p.temperament}";
    }

    // How often each faction takes a turn of thought (game-seconds; scales with game speed since it uses
    // Time.deltaTime). Deliberately slow — this is meant to feel gradual, not frantic.
    const float StepSeconds = 3f;
    const int MaxWorldsPerFaction = 10;   // keep expansion gentle; leave the galaxy room for the player

    float _accum;

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("FactionAI").AddComponent<FactionAI>();
    }

    void Awake() { Instance = this; }

    // ---- New game: give every non-player faction a race + personality and a homeworld ----
    public static void NewGame(Galaxy galaxy)
    {
        _profiles.Clear();
        if (galaxy == null) return;
        AssignProfiles();
        SeedHomeworlds(galaxy);
    }

    static void AssignProfiles()
    {
        int speciesCount = Mathf.Max(1, SpeciesDatabase.All.Count);
        foreach (var f in FactionManager.All)
        {
            if (f == null || f.relation == FactionRelation.Player) continue;

            var sp = SpeciesDatabase.Get(f.id % speciesCount);   // a distinct race per faction where possible
            float fert = sp.fertility / 10f, adapt = sp.adaptability / 10f, dur = sp.durability / 10f;

            var p = new Profile
            {
                factionId = f.id,
                speciesIndex = f.id % speciesCount,
                // Breeders and adaptable races push out harder; everyone keeps a floor of ambition.
                expansionism = Mathf.Clamp01(0.3f + (fert + adapt) * 0.3f + Random.Range(-0.08f, 0.08f)),
                // Fertility drives how fast the population climbs.
                growthDrive = Mathf.Clamp(0.7f + fert * 0.7f + Random.Range(-0.1f, 0.1f), 0.6f, 1.6f),
                // Durable, wide-tolerance races will colonise worlds that would kill a softer species.
                hardiness = Mathf.Clamp(52f - dur * 2.6f - (sp.tolerance - 1f) * 8f, 25f, 50f),
                expandCooldown = Random.Range(25f, 70f),
            };
            p.temperament = Temperament(p, sp);
            _profiles[f.id] = p;
        }
    }

    static string Temperament(Profile p, Species sp)
    {
        string drive = p.expansionism > 0.6f ? "Expansionist" : p.expansionism < 0.4f ? "Reclusive" : "Measured";
        string breed = sp.fertility >= 8 ? " breeders" : sp.fertility <= 3 ? " slow-breeders" : "";
        string grit = p.hardiness <= 34f ? ", hardy pioneers" : "";
        return (drive + breed + grit).Trim();
    }

    static void SeedHomeworlds(Galaxy galaxy)
    {
        var usedSystems = new HashSet<StarSystemData>();
        foreach (var kv in _profiles)
        {
            var f = FactionManager.Get(kv.Key);
            if (f == null) continue;
            var species = SpeciesDatabase.Get(kv.Value.speciesIndex);

            CelestialBody best = null; StarSystemData bestSys = null; float bestScore = -1f;
            foreach (var sys in galaxy.systems)
            {
                if (sys == null || sys == galaxy.Home || sys.bodies == null) continue;
                bool sysUsed = usedSystems.Contains(sys);
                foreach (var b in sys.bodies)
                {
                    if (!Settleable(b)) continue;
                    float hab = HabFor(b, species);
                    if (hab < 40f) continue;                          // a civilisation starts on a decent world
                    float score = hab + Random.Range(0f, 10f) - (sysUsed ? 40f : 0f);   // spread the homeworlds out
                    if (score > bestScore) { bestScore = score; best = b; bestSys = sys; }
                }
            }
            if (best != null)
            {
                Settle(best, f, species, true);
                if (bestSys != null) usedSystems.Add(bestSys);
            }
        }
    }

    // ---- Per-turn life: grow the worlds, and now and then reach for a new one ----
    void Update()
    {
        if (SystemContext.Galaxy == null || _profiles.Count == 0) return;
        _accum += Time.deltaTime;
        if (_accum < StepSeconds) return;
        float step = _accum;
        _accum = 0f;

        foreach (var kv in _profiles)
        {
            var f = FactionManager.Get(kv.Key);
            var prof = kv.Value;
            if (f == null) continue;
            var species = SpeciesDatabase.Get(prof.speciesIndex);

            int worlds = GrowWorlds(f, prof, species, step);
            if (worlds == 0) continue;   // a faction with no worlds left doesn't respawn

            prof.expandCooldown -= step;
            if (prof.expandCooldown <= 0f)
            {
                if (worlds < MaxWorldsPerFaction) TryExpand(f, prof, species);
                // More expansionist civilisations come back around sooner.
                prof.expandCooldown = Mathf.Lerp(90f, 30f, prof.expansionism) * Random.Range(0.85f, 1.15f);
            }
        }
    }

    // Grow every settled world this faction owns toward its carrying capacity (logistic, so it slows as it
    // fills), and let cities accrue with population. Returns how many settled worlds the faction has.
    static int GrowWorlds(Faction f, Profile prof, Species species, float step)
    {
        int count = 0;
        foreach (var b in SystemContext.AllBodies())
        {
            if (b == null || b.owner != f || !b.settled) continue;
            count++;

            float hab = HabFor(b, species);
            int cap = CapacityFor(b, species, hab);
            // Floor at 20% (not 30%) so a HARDY race's marginal colony — which it will settle down to its
            // ~25% hardiness — still grows, just slowly, rather than sitting frozen at its seed population.
            float habF = Mathf.InverseLerp(20f, 100f, hab);

            if (b.population < cap)
            {
                float room = 1f - (float)b.population / Mathf.Max(1, cap);
                // A logistic term (fast in the middle, tapering near the cap) plus a small constant crawl so
                // a young colony of a handful of people still gets going.
                float grow = (Mathf.Max(1f, b.population) * room * 0.02f + 0.15f) * prof.growthDrive * habF * step;
                b.popAccum += grow;   // NonSerialized fractional carry; ColonyManager never touches non-player worlds
                if (b.popAccum >= 1f)
                {
                    int add = Mathf.FloorToInt(b.popAccum);
                    b.population = Mathf.Min(cap, b.population + add);
                    b.popAccum -= add;
                }
            }

            // Cities thicken as the population grows — one per ~60 people, capped so it stays believable.
            int wantCities = Mathf.Clamp(1 + b.population / 60, 1, 25);
            if (b.cities < wantCities) b.cities++;
        }
        return count;
    }

    // Colonise the best habitable unclaimed world this faction can reach — preferring systems where it
    // already has a foothold, so its territory grows outward as a contiguous patch rather than scattering.
    static void TryExpand(Faction f, Profile prof, Species species)
    {
        CelestialBody best = null; float bestScore = -1f;
        foreach (var b in SystemContext.AllBodies())
        {
            if (!Settleable(b)) continue;
            float hab = HabFor(b, species);
            if (hab < prof.hardiness) continue;   // won't settle a world below its tolerance
            float score = hab + Random.Range(0f, 12f);
            if (SystemHasOwner(b, f)) score += 25f;   // grow next to what it already holds
            if (score > bestScore) { bestScore = score; best = b; }
        }
        if (best != null) Settle(best, f, species, false);
    }

    // ---- Helpers ----

    static bool Settleable(CelestialBody b)
    {
        if (b == null || b.owner != null || b.parentBody != null) return false;
        if (b.type == CelestialBodyType.GasGiant || b.type == CelestialBodyType.Asteroid) return false;
        // Leave the player's home system to the player — rivals grow their empires elsewhere.
        var g = SystemContext.Galaxy;
        if (g != null && b.system == g.Home) return false;
        return true;
    }

    static float HabFor(CelestialBody b, Species species)
    {
        var star = b.hostStar;
        return star != null ? Habitability.Rate(star, species, b.type, b.distanceFromStar) : b.habitability;
    }

    static bool SystemHasOwner(CelestialBody b, Faction f)
    {
        var sys = b.system;
        if (sys == null || sys.bodies == null) return false;
        foreach (var other in sys.bodies)
            if (other != null && other.owner == f) return true;
        return false;
    }

    static int CapacityFor(CelestialBody b, Species species, float hab)
    {
        float size = Mathf.Max(1, b.surfaceSize);
        float habF = Mathf.InverseLerp(20f, 100f, hab);          // barely-livable worlds hold far fewer (floor matches growth)
        float fert = species.fertility / 10f;
        return Mathf.Max(5, Mathf.RoundToInt(size * Mathf.Lerp(2f, 18f, habF) * (0.7f + fert * 0.6f)));
    }

    static void Settle(CelestialBody b, Faction f, Species species, bool homeworld)
    {
        if (b == null || f == null) return;
        b.owner = f;
        b.settled = true;
        if (b.cities < 1) b.cities = 1;
        if (b.population <= 0) b.population = homeworld ? Random.Range(40, 80) : Random.Range(6, 16);

        // The first civilisation to settle a system flies its flag over it, so the galaxy view and the
        // system window show who holds it (persisted via the system's ownerId).
        if (b.system != null && b.system.owner == null) b.system.owner = f;

        // Wear the faction's owner ring so the player can see a civilisation holds this world (the same ring
        // player/colonised worlds use). At new-game time the visuals already exist; on load the renderer
        // draws the ring itself from body.owner, so this is only for rings claimed live.
        if (b.visualObject != null)
        {
            var oc = b.visualObject.GetComponent<OrbitController>();
            if (oc != null) oc.SetOwnerHighlight(FactionManager.OwnerColor(f), true);
        }
    }

    // ---- Save / load ----
    public static List<FactionAIDTO> ToDTOs()
    {
        var list = new List<FactionAIDTO>();
        foreach (var kv in _profiles)
        {
            var p = kv.Value;
            list.Add(new FactionAIDTO
            {
                factionId = p.factionId,
                speciesIndex = p.speciesIndex,
                expansionism = p.expansionism,
                growthDrive = p.growthDrive,
                hardiness = p.hardiness,
                expandCooldown = p.expandCooldown,
                temperament = p.temperament,
            });
        }
        return list;
    }

    public static void LoadDTOs(List<FactionAIDTO> dtos)
    {
        _profiles.Clear();
        if (dtos == null) return;
        foreach (var d in dtos)
            _profiles[d.factionId] = new Profile
            {
                factionId = d.factionId,
                speciesIndex = d.speciesIndex,
                expansionism = d.expansionism,
                growthDrive = d.growthDrive,
                hardiness = d.hardiness,
                expandCooldown = d.expandCooldown,
                temperament = string.IsNullOrEmpty(d.temperament) ? "Measured" : d.temperament,
            };
    }
}
