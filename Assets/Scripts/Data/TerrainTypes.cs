// Master list of terrain / biome types used across generation, colouring and the surface viewer.
// NOTE: If you add a value here, also add a colour for it in TerrainColorMap so it never renders magenta.
public enum TerrainType
{
    // --- Original core set ---
    Plains,
    Mountains,
    Forest,
    Ice,
    MagmaField,
    Volcano,
    Desert,
    Ocean,
    Island,
    Crater,
    Barren,

    // --- Temperate / life-bearing ---
    Grassland,
    Jungle,
    Swamp,
    Savanna,
    Steppe,
    Tundra,
    Taiga,
    Hills,
    Highlands,
    Beach,
    Lake,
    River,
    Reef,

    // --- Cold worlds ---
    Snow,
    Glacier,
    FrozenSea,

    // --- Hot / dry worlds ---
    Dunes,
    SaltFlat,
    Canyon,
    Badlands,
    Wasteland,

    // --- Volcanic / hostile ---
    AshWaste,
    ObsidianFlat,
    LavaRock,
    GeyserField,
    CrackedGround,

    // --- Exotic / mineral ---
    CrystalField,
    MetallicCrust,
    GasClouds,
    Storm
}
