using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// SHIP HARDPOINTS — where each hull's engines and guns really are
//
// Baked offline from the .glb geometry by tools/bake-ship-hardpoints.mjs into
// Resources/SpaceAssets/Ships/ship-hardpoints.json, because finding a drive bell means reading the
// emission texture and clustering the stern's triangles, which is no job for a frame budget.
//
// THE FRAME: positions are in UNITY MESH SPACE — glTF with X negated, exactly as glTFast imports it.
// The generated hulls are a single node at identity, so that is also the local frame of the
// instantiated model's root, and a point from this file can be used as a child's localPosition as-is.
//
// STALENESS: each entry records the ship-meshes.txt rotation it was baked under. A nozzle is "the
// rear-most cluster" only for the rotation that decides which end is the rear, so if the manifest line
// has changed since the bake (an F10 edit, a re-rolled hull) the entry is refused and ShipLights falls
// back to the hull's bounds. A wrong plume on the nose is much worse than a generic one at the stern.
// ============================================================================================
public static class ShipHardpoints
{
    public const string Path = "SpaceAssets/Ships/ship-hardpoints";

    [System.Serializable] public class Nozzle { public float[] p; public float r; public float glow; }
    [System.Serializable] public class Gun { public float[] p; public float[] d; }

    [System.Serializable]
    public class Entry
    {
        public string name;
        public float[] rot;
        public bool station;
        public float[] boundsCenter, boundsSize;
        public Nozzle[] nozzles;
        public Gun[] guns;
    }

    [System.Serializable] class HardpointFile { public string frame; public Entry[] entries; }

    static Dictionary<string, Entry> byName;

    public static void Reload()
    {
        byName = new Dictionary<string, Entry>(System.StringComparer.OrdinalIgnoreCase);
        var asset = Resources.Load<TextAsset>(Path);
        if (asset == null) return;
        try
        {
            var f = JsonUtility.FromJson<HardpointFile>(asset.text);
            if (f?.entries != null)
                foreach (var e in f.entries)
                    if (e != null && !string.IsNullOrEmpty(e.name)) byName[e.name] = e;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ShipHardpoints] Could not read Resources/{Path}.json ({ex.Message}). Hulls fall back to their bounds.");
        }
        Resources.UnloadAsset(asset);
    }

    /// The baked points for this mesh, if they were baked under the rotation the hull is using now.
    public static Entry For(string meshPath, Quaternion modelRotation)
    {
        if (byName == null) Reload();
        if (string.IsNullOrEmpty(meshPath)) return null;
        if (!byName.TryGetValue(ShipMeshManifest.LeafName(meshPath), out var e) || e == null) return null;
        if (e.rot == null || e.rot.Length < 3) return null;
        var baked = Quaternion.Euler(e.rot[0], e.rot[1], e.rot[2]);
        return Quaternion.Angle(baked, modelRotation) < 1f ? e : null;
    }

    public static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
}
