// One cell of a planet surface. Holds its biome, the ground under any water or ice covering it, its
// elevation, an optional ore deposit, and a per-tile shade value (0..1) that the viewer uses to add
// subtle brightness variation so the map reads as detailed pixels rather than flat blocks of colour.
public class TerrainTile
{
    public TerrainType type;
    public bool occupied;

    /// THE GROUND UNDER THE WATER OR THE ICE.
    ///
    /// Snow, ice and sea are covers, not biomes — "when an ocean grows in grid size, the terrain that was
    /// enveloped still exists; if I wanted to remove the water, it will still be there". It genuinely
    /// does still exist, because a world's elevation is decided by its geology alone and nothing about
    /// water, temperature or air can move it (see PlanetTerrainGenerator's elevation pipeline). This is
    /// what makes that legible: the tile readout can say "Ocean over Steppe", so a player can see what
    /// draining a sea would uncover before spending a terraforming project to find out.
    ///
    /// Equal to `type` on any tile that is neither flooded nor frozen.
    public TerrainType ground;

    /// The ground's own height, 0..1 — the same figure Sample.elevation reports. Stored rather than
    /// re-derived because the hover readout wants it every frame the cursor moves and re-deriving means
    /// re-running the whole noise field for one cell.
    ///
    /// NOT SAVED, like the rest of the terrain: the grid is rebuilt from the body's seed on load, and
    /// this comes back with it.
    public float elevation = 0.5f;

    public OreType ore = OreType.None;     // None if this tile has no ore
    public float oreRichness = 0f;         // 0..1, how concentrated the deposit is
    public float shade = 0.5f;             // per-tile brightness jitter for pixel detail

    public bool HasOre => ore != OreType.None;

    /// Is something lying ON this tile rather than being it? True for sea, lake, sea ice, snow and
    /// glacier — the cases where `ground` says something different from `type`.
    public bool HasCover => ground != type;

    public TerrainTile(TerrainType t)
    {
        type = t;
        ground = t;
        occupied = false;
    }

    public TerrainTile(TerrainType t, float shade)
    {
        type = t;
        ground = t;
        this.shade = shade;
        occupied = false;
    }

    public TerrainTile(TerrainType t, TerrainType ground, float shade, float elevation)
    {
        type = t;
        this.ground = ground;
        this.shade = shade;
        this.elevation = elevation;
        occupied = false;
    }
}
