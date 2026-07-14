// One cell of a planet surface. Holds its biome, an optional ore deposit, and a per-tile
// shade value (0..1) that the viewer uses to add subtle brightness variation so the map reads
// as detailed pixels rather than flat blocks of colour.
public class TerrainTile
{
    public TerrainType type;
    public bool occupied;

    public OreType ore = OreType.None;     // None if this tile has no ore
    public float oreRichness = 0f;         // 0..1, how concentrated the deposit is
    public float shade = 0.5f;             // per-tile brightness jitter for pixel detail

    public bool HasOre => ore != OreType.None;

    public TerrainTile(TerrainType t)
    {
        type = t;
        occupied = false;
    }

    public TerrainTile(TerrainType t, float shade)
    {
        type = t;
        this.shade = shade;
        occupied = false;
    }
}
