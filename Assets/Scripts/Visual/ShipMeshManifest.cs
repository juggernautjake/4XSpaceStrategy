using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// WHICH WAY IS UP — orientation for imported ship meshes
//
// Generated meshes arrive pointing anywhere. Meshy, Kaedim, Tripo and every other text-to-3D service
// makes an arbitrary choice about which way the bow faces and which way is up, and it makes a
// DIFFERENT arbitrary choice on the next model. With three hand-authored meshes that was a non-problem:
// somebody eyeballed a Quaternion.Euler(-90, 0, 0), typed it into UnitModelLibrary, and moved on.
//
// With five civilizations of twenty-nine hulls it is a hundred and forty-five eyeballed constants
// living in a file that has to be recompiled to change one of them. That is not a workflow, and it
// would be the reason the art never got finished.
//
// So orientation moves OUT of code and into data:
//
//   1. A manifest — `Resources/SpaceAssets/Ships/ship-meshes.txt` — one line per mesh that needs a
//      correction. Editable while the game is running; no recompile, no rebuild, no programmer.
//   2. Anything not in the manifest is ORIENTED FROM ITS OWN BOUNDS by the heuristic below, which is
//      right for most conventional hulls and reports what it decided so a wrong guess is one paste
//      away from fixed.
//
// The manifest is the authority. The heuristic exists so that the manifest can stay nearly empty.
//
// ---- THE FORMAT ------------------------------------------------------------------------------
//
//   # comments and blank lines are ignored
//   Terran_Dreadnought        0 0 0
//   Aquarii_Carrier         -90 0 0
//   Pyrothian_MiningBarge     0 180 0     1.15
//   Cryithn_Probe             0 0 0       1.0     spin
//   Sylvan_HyperSpeedRelay    0 90 0      1.0     nospin
//
//   name      the mesh file's name without extension or folder
//   rotation  pitch yaw roll in degrees, applied BEFORE the ship is pointed along its course
//   scale     optional multiplier on the class's normal size (blank or absent = 1)
//   flags     `spin` = always tumble freely (radially symmetric things with no meaningful facing)
//             `nospin` = hold a fixed attitude (rings, relays, anything that should not rotate)
// ============================================================================================
public static class ShipMeshManifest
{
    public const string ManifestPath = "SpaceAssets/Ships/ship-meshes";

    public class Entry
    {
        public Quaternion rotation = Quaternion.identity;
        public float scale = 1f;
        public bool forceSpin;
        public bool forceNoSpin;
        /// True when this came from the manifest rather than from the bounds heuristic. The heuristic
        /// logs itself; an authored entry does not need announcing.
        public bool authored;
    }

    static Dictionary<string, Entry> entries;

    /// Re-read the manifest from disk. Called on first use and by the Dev reload — which is the whole
    /// point of the file existing: an artist can fix a sideways ship and see it corrected without a
    /// recompile.
    public static void Reload()
    {
        entries = new Dictionary<string, Entry>(System.StringComparer.OrdinalIgnoreCase);

        var asset = Resources.Load<TextAsset>(ManifestPath);
        if (asset == null) return;                 // no manifest is a perfectly good state: all-auto

        foreach (var raw in asset.text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            // Split on any run of whitespace, so the file can be aligned into columns for readability.
            var tok = line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length < 4) continue;          // name + three angles is the minimum meaningful line

            // THE NAME IS EVERYTHING BEFORE THE FIRST NUMBER, and it has to be, because mesh files are
            // named by humans and humans put spaces in filenames. "LP Colony Ship -90 0 0" is a line
            // this file will genuinely contain; taking the first token as the name would key it under
            // "LP" and then fail to parse "Colony" as an angle. Scanning for where the numbers start
            // costs nothing and means the artist never has to think about quoting.
            int firstNum = -1;
            for (int i = 1; i < tok.Length; i++)
                if (float.TryParse(tok[i], out _)) { firstNum = i; break; }

            // Three angles must follow, and a name must precede. A line that fails any of this is
            // REPORTED rather than skipped in silence: the whole value of this file is that an artist
            // edits it without a programmer, and a typo that simply does nothing is an hour of
            // wondering why the ship is still sideways.
            if (firstNum < 1 || firstNum + 2 >= tok.Length)
            {
                Debug.LogWarning($"ShipMeshManifest: cannot read line \"{line}\" — expected " +
                                 "\"<name> <pitch> <yaw> <roll> [scale] [spin|nospin]\". Ignored.");
                continue;
            }

            var e = new Entry { authored = true };
            if (!float.TryParse(tok[firstNum], out float pitch) ||
                !float.TryParse(tok[firstNum + 1], out float yaw) ||
                !float.TryParse(tok[firstNum + 2], out float roll))
            {
                Debug.LogWarning($"ShipMeshManifest: cannot read the three angles on line \"{line}\". Ignored.");
                continue;
            }
            e.rotation = Quaternion.Euler(pitch, yaw, roll);

            int after = firstNum + 3;
            if (after < tok.Length && float.TryParse(tok[after], out float s) && s > 0.001f)
            { e.scale = s; after++; }

            for (int i = after; i < tok.Length; i++)
            {
                if (string.Equals(tok[i], "spin", System.StringComparison.OrdinalIgnoreCase)) e.forceSpin = true;
                if (string.Equals(tok[i], "nospin", System.StringComparison.OrdinalIgnoreCase)) e.forceNoSpin = true;
            }

            entries[string.Join(" ", tok, 0, firstNum)] = e;
        }

        Debug.Log($"ShipMeshManifest: {entries.Count} authored orientation(s) loaded from {ManifestPath}.");
    }

    /// The authored correction for a mesh, or null if it is not listed and should be auto-oriented.
    public static Entry Authored(string meshName)
    {
        if (entries == null) Reload();
        if (string.IsNullOrEmpty(meshName)) return null;
        return entries.TryGetValue(LeafName(meshName), out var e) ? e : null;
    }

    // ============================================================================================
    // THE HEURISTIC
    //
    // Three facts about the shape of ships, in order of how reliable they are:
    //
    //   1. THE LONGEST AXIS IS THE LENGTH. Ships are longer than they are wide. This is the safest
    //      assumption in the whole file and it is almost never wrong.
    //   2. THE SHORTEST AXIS IS UP. Ships are wider than they are tall — a hull has a beam and a
    //      draught, and the draught is smaller. This is why the prompt guide insists on a flat ventral
    //      surface and a detailed dorsal one: it makes the mesh obey this rule rather than fight it.
    //   3. THE HEAVIER END IS THE STERN. Engines and reactors mass more than sensors and cockpits, so
    //      whichever half of the mesh holds more geometry is the back.
    //
    // Rule 3 is the shakiest and it is also the one that matters least: a ship flying backwards is
    // instantly obvious and takes one manifest line to fix, whereas a ship flying SIDEWAYS looks like a
    // rendering bug. So the axes are established first and the direction along the length last.
    //
    // Returns the rotation that brings the mesh into the game's convention: +Z forward, +Y up.
    // ============================================================================================
    public static Quaternion AutoOrient(GameObject prefab, string meshName, out string explanation)
    {
        explanation = "identity (no mesh to measure)";
        if (prefab == null) return Quaternion.identity;

        if (!TryMeasure(prefab, out Bounds bounds, out Vector3 massCentre))
            return Quaternion.identity;

        Vector3 size = bounds.size;
        if (size.sqrMagnitude < 1e-8f) return Quaternion.identity;

        // ---- 1) longest axis is the ship's length; 2) shortest is its up ----
        int lengthAxis = LargestAxis(size);
        int upAxis = SmallestAxis(size);
        if (upAxis == lengthAxis) upAxis = (lengthAxis + 1) % 3;   // degenerate (a cube); pick anything

        Vector3 forward = Axis(lengthAxis);
        Vector3 up = Axis(upAxis);

        // ---- 3) the heavier half is the stern, so forward points AWAY from the mass ----
        float along = Vector3.Dot(massCentre - bounds.center, forward);
        if (along > 0f) forward = -forward;

        // Orthonormalise: `up` is measured on a different axis from `forward`, so they are already
        // perpendicular — but LookRotation is unforgiving about float drift and silently returns
        // identity on a degenerate pair, which would look like the correction had not been applied.
        up = Vector3.ProjectOnPlane(up, forward);
        if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
        up.Normalize();

        // The rotation that takes the mesh's own (forward, up) onto the game's (+Z, +Y).
        Quaternion meshToWorld = Quaternion.LookRotation(forward, up);
        Quaternion correction = Quaternion.Inverse(meshToWorld);

        explanation = $"length on {AxisName(lengthAxis)} ({size[lengthAxis]:0.##}), " +
                      $"up on {AxisName(upAxis)} ({size[upAxis]:0.##}), " +
                      $"stern toward {(along > 0f ? "+" : "-")}{AxisName(lengthAxis)}";

        Debug.Log($"ShipMeshManifest: auto-oriented '{meshName}' — {explanation}. " +
                  $"If this is wrong, add a line to {ManifestPath}.txt");

        return correction;
    }

    /// Combined bounds of every renderer, and the geometric centre of mass approximated from the
    /// renderers' own bounds weighted by volume.
    ///
    /// Renderer bounds rather than mesh vertices on purpose: an imported prefab may have several
    /// sub-meshes at different local transforms, and walking vertices would need every one of them
    /// baked into a common space. Renderer bounds are already in world space and already account for
    /// the hierarchy — coarser, and correct.
    static bool TryMeasure(GameObject prefab, out Bounds bounds, out Vector3 massCentre)
    {
        bounds = default;
        massCentre = Vector3.zero;

        var renderers = prefab.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) return false;

        bool any = false;
        float totalVolume = 0f;
        Vector3 weighted = Vector3.zero;

        foreach (var r in renderers)
        {
            if (r == null) continue;
            var b = r.bounds;
            if (!any) { bounds = b; any = true; }
            else bounds.Encapsulate(b);

            float v = Mathf.Max(1e-6f, b.size.x * b.size.y * b.size.z);
            weighted += b.center * v;
            totalVolume += v;
        }

        if (!any) return false;
        massCentre = totalVolume > 0f ? weighted / totalVolume : bounds.center;
        return true;
    }

    static int LargestAxis(Vector3 v)
        => v.x >= v.y && v.x >= v.z ? 0 : (v.y >= v.z ? 1 : 2);

    static int SmallestAxis(Vector3 v)
        => v.x <= v.y && v.x <= v.z ? 0 : (v.y <= v.z ? 1 : 2);

    static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

    static string AxisName(int i) => i == 0 ? "X" : i == 1 ? "Y" : "Z";

    /// The file's own name, without folders or extension — what the manifest keys on.
    public static string LeafName(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        int slash = path.LastIndexOfAny(new[] { '/', '\\' });
        string leaf = slash >= 0 ? path.Substring(slash + 1) : path;
        int dot = leaf.LastIndexOf('.');
        return dot > 0 ? leaf.Substring(0, dot) : leaf;
    }
}
