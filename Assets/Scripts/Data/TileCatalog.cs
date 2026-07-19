using System.Collections.Generic;

// Authored reference data for the Tile Catalogue viewer: for every TerrainType, what it is and the rough
// conditions that generate it. These describe the classifier rules in PlanetTerrainGenerator in plain
// language (elevation band, the temperature the °C coherence pass gates on, moisture, and which world
// types produce it) — a reader's guide to the map, not a second source of truth the generator reads.
public static class TileCatalog
{
    public struct Entry
    {
        public TerrainType type;
        public string category;
        public string desc;
        public string elevation;
        public string temperature;
        public string moisture;
        public string worlds;
    }

    static Entry E(TerrainType t, string cat, string desc, string elev, string temp, string moist, string worlds)
        => new Entry { type = t, category = cat, desc = desc, elevation = elev, temperature = temp, moisture = moist, worlds = worlds };

    const string Any = "—";

    // In enum order so the list reads predictably. Every TerrainType has an entry.
    public static readonly Entry[] All =
    {
        // --- Water ---
        E(TerrainType.Ocean, "Water", "Open liquid sea — the large connected water bodies of a world.",
            "Below sea level", "Above freezing (0°C+)", Any, "Rocky, Ocean, warm Ice worlds"),
        E(TerrainType.Lake, "Water", "An enclosed inland body of water, cut off from the open ocean.",
            "Low basins", "Above freezing (0°C+)", "Wet", "Rocky, Ice worlds"),
        E(TerrainType.River, "Water", "Flowing fresh water threading through the land.",
            "Low channels", "Above freezing (0°C+)", "Wet", "Life-bearing worlds"),
        E(TerrainType.Reef, "Water", "Coral shallows in warm, sunlit ocean.",
            "Shallow (just below sea level)", "Warm ocean", "—", "Ocean worlds"),
        E(TerrainType.Island, "Water", "Land poking above a world-spanning ocean.",
            "Rises above the sea", "Temperate", "—", "Ocean worlds"),
        E(TerrainType.Beach, "Coast", "Sandy shore where soft lowland meets the open ocean.",
            "At the waterline", "Above freezing", "—", "Any world with oceans"),

        // --- Cold / frozen ---
        E(TerrainType.FrozenSea, "Frozen", "Sea frozen solid — liquid water can't persist here.",
            "Below sea level", "Below 0°C", Any, "Cold worlds, poles, Ice worlds"),
        E(TerrainType.Ice, "Frozen", "A sheet of surface ice over frozen ground.",
            "Low to mid", "Below 0°C", Any, "Ice worlds, frozen worlds"),
        E(TerrainType.Glacier, "Frozen", "A thick highland ice cap grinding downhill.",
            "Highlands / peaks", "Well below 0°C", "Wet", "Ice worlds"),
        E(TerrainType.Snow, "Frozen", "Snow-covered ground — buried by cold at height or latitude.",
            "Any raised or polar ground", "Below 0°C (deep cold: below −25°C)", Any, "Cold worlds, high peaks, poles"),
        E(TerrainType.Tundra, "Frozen", "Frozen ground with sparse, hardy low cover.",
            "Lowland", "Just below freezing", "Low–moderate", "Cold worlds, cold shores"),
        E(TerrainType.Taiga, "Cold forest", "Boreal conifer forest that endures long, hard winters.",
            "Lowland", "Cold (below ~−12°C band)", "Wet", "Life-bearing worlds"),
        E(TerrainType.CrystalField, "Frozen", "Fields of ice/mineral crystals on a frozen world.",
            "Mid to high", "Below 0°C", "Wet", "Ice worlds, airless bodies"),

        // --- Temperate / life-bearing ---
        E(TerrainType.Grassland, "Vegetation", "Open grassland — the workhorse temperate biome.",
            "Lowland", "Temperate (0–55°C)", "Moderate", "Life-bearing worlds"),
        E(TerrainType.Plains, "Vegetation", "Broad, gently grassed lowland.",
            "Lowland", "Temperate", "Low–moderate", "Life-bearing worlds"),
        E(TerrainType.Steppe, "Vegetation", "Dry grassy plain on the edge of the arid band.",
            "Lowland", "Temperate", "Dry", "Life-bearing worlds"),
        E(TerrainType.Forest, "Vegetation", "Temperate woodland.",
            "Lowland to hills", "Temperate (thins above ~55°C)", "Wet", "Life-bearing worlds"),
        E(TerrainType.Jungle, "Vegetation", "Hot, dripping rainforest — needs real warmth and heavy rain.",
            "Lowland", "Warm (up to ~55°C, then thins to savanna)", "Very wet", "Life-bearing worlds"),
        E(TerrainType.Swamp, "Vegetation", "Waterlogged wetland where low ground stays soaked.",
            "Low, near water", "Warm & wet (dries out above ~55°C)", "Very wet", "Life-bearing worlds"),
        E(TerrainType.Savanna, "Vegetation", "Tropical grassland dotted with hardy growth.",
            "Lowland", "Hot (warm band, and where jungle is too hot)", "Moderate", "Life-bearing worlds"),

        // --- Elevation ---
        E(TerrainType.Hills, "Highland", "Rolling raised ground below the true highlands.",
            "Elevated", "Follows latitude/altitude", Any, "Rocky worlds"),
        E(TerrainType.Highlands, "Highland", "High tableland — cold and thin-aired for its latitude.",
            "High", "Cooled by altitude", Any, "Most solid worlds"),
        E(TerrainType.Mountains, "Highland", "Folded mountain ranges, concentrated along fault belts.",
            "Peaks / strong ridges", "Cold at height (snow-capped)", Any, "Solid worlds with relief"),

        // --- Hot / arid ---
        E(TerrainType.Desert, "Arid", "Dry, sun-baked sand and rock.",
            "Lowland", "Hot & dry (or scorched >75°C)", "Very dry", "Warm life-bearing / dry worlds"),
        E(TerrainType.Dunes, "Arid", "Wind-shaped sand seas.",
            "Lowland", "Hot & dry", "Very dry", "Warm worlds"),
        E(TerrainType.SaltFlat, "Arid", "The cracked bed of an evaporated sea.",
            "Low basins", "Hot / dry", "None", "Barren, dry worlds"),
        E(TerrainType.Badlands, "Arid", "Eroded, ridged wasteland of bare rock.",
            "Mid, ridged", "Hot / dry", "Dry", "Barren worlds"),
        E(TerrainType.Canyon, "Arid", "Deep-cut rock gorges.",
            "Ridged", "—", "Dry", "Barren worlds"),
        E(TerrainType.Wasteland, "Arid", "Bare, hostile ground — dead flat or scorched over 100°C.",
            "Lowland", "Scorching (100°C+) or dead", "None", "Barren / scorched worlds"),

        // --- Volcanic / hostile ---
        E(TerrainType.Volcano, "Volcanic", "An active volcanic cone. Scattered along fault belts on active worlds.",
            "Peaks over convergent faults", "Furnace-hot at the vent", "—", "Volcanic worlds; active Rocky worlds"),
        E(TerrainType.MagmaField, "Volcanic", "Open fields of molten rock.",
            "Low to mid", "Extreme heat", "—", "Volcanic worlds"),
        E(TerrainType.LavaRock, "Volcanic", "Cooled, jagged volcanic rock.",
            "Raised", "Hot", "—", "Volcanic worlds"),
        E(TerrainType.ObsidianFlat, "Volcanic", "Glassy black volcanic plains.",
            "Low", "Hot", "—", "Volcanic worlds"),
        E(TerrainType.AshWaste, "Volcanic", "Choking plains of volcanic ash.",
            "Lowland", "Hot", "—", "Volcanic worlds"),
        E(TerrainType.GeyserField, "Volcanic", "Steaming vents and hot springs.",
            "Lowland", "Warm, geothermally active", "—", "Volcanic worlds"),
        E(TerrainType.CrackedGround, "Volcanic", "Fractured crust over restless ground.",
            "Mid", "Warm / variable", "—", "Volcanic worlds, hot airless bodies"),

        // --- Rock / airless ---
        E(TerrainType.Barren, "Rock", "Lifeless bare rock.",
            "Any", "Any", "None", "Barren worlds, moons, asteroids"),
        E(TerrainType.Crater, "Rock", "An impact crater on airless ground.",
            "Low", "Any", "None", "Moons, asteroids"),
        E(TerrainType.MetallicCrust, "Rock", "Exposed metal-rich crust.",
            "High", "Any", "None", "Barren / airless bodies"),

        // --- Gas giant ---
        E(TerrainType.GasClouds, "Gas giant", "Banded cloud decks of a gas giant.",
            "Cloud tops", "—", "—", "Gas giants"),
        E(TerrainType.Storm, "Gas giant", "A vast, churning storm system.",
            "Cloud tops", "—", "—", "Gas giants"),
    };

    static Dictionary<TerrainType, Entry> _byType;
    public static Entry Get(TerrainType t)
    {
        if (_byType == null)
        {
            _byType = new Dictionary<TerrainType, Entry>();
            foreach (var e in All) _byType[e.type] = e;
        }
        return _byType.TryGetValue(t, out var found) ? found
             : E(t, "—", t.ToString(), Any, Any, Any, Any);
    }
}
