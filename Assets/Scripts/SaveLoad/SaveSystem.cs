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
            string json = JsonUtility.ToJson(game, true);
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
    public static List<SaveGame> ListSaves()
    {
        var result = new List<SaveGame>();
        foreach (var file in Directory.GetFiles(Dir, "*.json"))
        {
            try
            {
                var g = JsonUtility.FromJson<SaveGame>(File.ReadAllText(file));
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
