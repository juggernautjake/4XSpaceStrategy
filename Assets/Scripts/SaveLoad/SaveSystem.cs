using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Handles reading/writing named save files as JSON under persistentDataPath/Saves.
// Multiple saves can coexist; each is one .json file named after the (sanitized) save name.
public static class SaveSystem
{
    static string Dir
    {
        get
        {
            string d = Path.Combine(Application.persistentDataPath, "Saves");
            if (!Directory.Exists(d)) Directory.CreateDirectory(d);
            return d;
        }
    }

    public static string SavesFolder => Dir;

    static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "save";
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }

    static string PathFor(string name) => Path.Combine(Dir, Sanitize(name) + ".json");

    public static bool Exists(string name) => File.Exists(PathFor(name));

    public static void Save(SaveGame game)
    {
        try
        {
            // COMPACT, not pretty-printed. JsonUtility's pretty printer puts every element of a list on
            // its own indented line, and a save now carries terrain grids and flattened plate layouts —
            // thousands of numbers per world. Pretty-printing roughly doubled the file and the time to
            // parse it back, in exchange for readability a multi-megabyte file does not have anyway.
            string json = JsonUtility.ToJson(game, false);
            File.WriteAllText(PathFor(game.saveName), json);
            Debug.Log($"Saved '{game.saveName}' -> {PathFor(game.saveName)}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Save failed: {e.Message}");
        }
    }

    public static SaveGame Load(string name)
    {
        try
        {
            string p = PathFor(name);
            if (!File.Exists(p)) { Debug.LogWarning($"No save named '{name}'"); return null; }
            return JsonUtility.FromJson<SaveGame>(File.ReadAllText(p));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Load failed: {e.Message}");
            return null;
        }
    }

    public static void Delete(string name)
    {
        string p = PathFor(name);
        if (File.Exists(p)) File.Delete(p);
    }

    // Lightweight listing for the load menu — reads each file's header fields.
    //
    // Deserialized as SaveHeader, NOT as SaveGame. JsonUtility ignores fields the target type does not
    // have, so this reads the same files while allocating four strings instead of the entire galaxy.
    // That mattered the moment saves started carrying terrain grids: this runs once per file every
    // time the menu opens, and building three hundred BodyDTOs per save to display a date is work
    // nobody asked for.
    public static List<SaveHeader> ListSaves()
    {
        var result = new List<SaveHeader>();
        foreach (var file in Directory.GetFiles(Dir, "*.json"))
        {
            try
            {
                var g = JsonUtility.FromJson<SaveHeader>(File.ReadAllText(file));
                if (g != null)
                {
                    if (string.IsNullOrEmpty(g.saveName))
                        g.saveName = Path.GetFileNameWithoutExtension(file);
                    result.Add(g);
                }
            }
            catch { /* skip corrupt files */ }
        }
        result.Sort((a, b) => string.Compare(b.savedAtIso, a.savedAtIso, System.StringComparison.Ordinal));
        return result;
    }
}
