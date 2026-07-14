using UnityEngine;

public class PlanetSurface
{
    public int width;
    public int height;
    public TerrainTile[,] tiles;

    public PlanetSurface(int w, int h)
    {
        width = w;
        height = h;
        tiles = new TerrainTile[width, height];
        // No log here: generating a galaxy builds a surface for every planet AND every moon, twice over,
        // which buried the console in dozens of identical lines on every new game.
    }
}