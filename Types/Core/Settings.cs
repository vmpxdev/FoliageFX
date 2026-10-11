using System.Text;
using SimpleJSON;

// Save schema. Settings travel in the scene only, as one block in the
// plugin's node: the parameters under their keys (ParamEntry.Key,
// "falling.size_mm") and every tree's looks under `trees` (TreeLooks). The
// keys are the schema: never the storable name or the panel label, so either
// can be renamed without touching saved data. This class stamps the block
// with the schema number and migrates older shapes in one place.
//
// Rules:
//   - adding a parameter never bumps FoliageFXConfig.Schema: a missing key
//     takes the plugin default on load, and SaveHistory says it is new;
//   - the schema bumps when MigrateStep rewrites saved data (a key rename, a
//     value rewrite); a change of meaning that is not rewritten is a
//     SaveHistory.Changed line and never bumps it;
//   - unknown keys are skipped, never applied and never fatal, so a scene
//     from a newer build loads what it can (a later save leaves them out:
//     they belong to no setting here);
//   - a value is validated on load (range, choices), so a hand-edited or
//     corrupt scene cannot put a parameter out of bounds.
sealed class Settings
{
    public const string SchemaKey = "FoliageFXSchema";
    public const string ProductKey = "product";
    public const string VersionKey = "version";
    public const string ParamsKey = "params";
    public const string TreesKey = "trees";
    public const string BlockKey = "FoliageFX";      // the plugin's keyed block inside its scene node

    // ---- scene block ----

    // The block the facade writes into the scene: {FoliageFXSchema, version,
    // params, trees}. Everything with a saved scope goes in; volatile values
    // never. `trees` is left out while no tree has a look of its own or is
    // switched off.
    public static JSONClass SceneBlock(ParamSet set, JSONClass trees)
    {
        JSONClass block = new JSONClass();
        block.Add(SchemaKey, new JSONData(FoliageFXConfig.Schema));
        block.Add(VersionKey, new JSONData(BuildInfo.Stamp()));
        JSONClass paramsNode = new JSONClass();
        set.Write(paramsNode, ParamSet.Saved, null);
        block.Add(ParamsKey, paramsNode);
        if (trees != null && trees.Count > 0) block.Add(TreesKey, trees);
        return block;
    }

    // Reads the plugin's values back from its scene node, migrating first,
    // and hands back the block's tree looks for TreeLooks.Read (null when the
    // scene has none). A node without the block is read only when it holds
    // our keys: VaM's own fields alone (id, plugin label) are not our data,
    // and reading them would put every setting to its default. Returns how
    // many values were applied.
    public static int ReadScene(JSONClass node, ParamSet set, bool missingToDefault, out JSONClass trees)
    {
        trees = null;
        if (node == null || set == null) return 0;
        JSONClass block = node[BlockKey] as JSONClass;
        JSONClass source;
        if (block != null)
        {
            Migrate(block);
            // A block whose settings are not an object: nothing can be read,
            // and with missingToDefault every setting goes to its default (a
            // complete restore).
            source = block[ParamsKey] as JSONClass;
            trees = block[TreesKey] as JSONClass;
        }
        else
        {
            if (!set.ClaimsAny(node)) return 0;
            Migrate(node);
            source = node;
        }
        return set.Read(source, ParamSet.Saved, null, missingToDefault);
    }

    // ---- scene JSON (the plugin's node inside the atom's storables) ----

    public static void Stamp(JSONClass jc)
    {
        if (jc == null) return;
        if (jc.HasKey(SchemaKey)) jc.Remove(SchemaKey);
        jc.Add(SchemaKey, new JSONData(FoliageFXConfig.Schema));
    }

    // The build stamp the scene was saved by ("3", "dev 3 main abc1234"), or "".
    public static string SavedVersion(JSONClass node)
    {
        if (node == null) return "";
        JSONClass block = node[BlockKey] as JSONClass;
        JSONNode v = block != null ? block[VersionKey] : null;
        return v != null ? (v.Value ?? "") : "";
    }

    // 0 means "no stamp": a node written without the block, or by a build
    // that did not stamp. Treated as the current schema (nothing to migrate).
    public static int SchemaOf(JSONNode jc)
    {
        if (jc == null) return 0;
        JSONNode n = jc[SchemaKey];
        if (n == null) return 0;
        return n.AsInt;
    }

    // Migrates `jc` in place from its stamped schema to the current one.
    // Returns false when the data comes from a newer build: its known values
    // still apply, and the rest is skipped.
    public static bool Migrate(JSONClass jc)
    {
        if (jc == null) return true;
        int from = SchemaOf(jc);
        if (from == 0 || from == FoliageFXConfig.Schema) return true;
        if (from > FoliageFXConfig.Schema) return false;
        while (from < FoliageFXConfig.Schema)
        {
            MigrateStep(jc, from);
            from++;
        }
        Stamp(jc);
        return true;
    }

    // One step: schema `from` -> `from + 1`. Add a case per bump. A scene
    // block holds its keys under `params` and the tree looks under `trees`.
    static void MigrateStep(JSONClass jc, int from)
    {
        switch (from)
        {
            // case 1: Rename(KeysOf(jc), "old.key", "new.key"); break;
            default: break;
        }
    }

    static JSONClass KeysOf(JSONClass jc)
    {
        return jc[ParamsKey] as JSONClass ?? jc;
    }

    // A key renamed in a release: its value moves to the new key.
    static void Rename(JSONClass jc, string oldKey, string newKey)
    {
        if (jc == null || !jc.HasKey(oldKey) || jc.HasKey(newKey)) return;
        JSONNode n = jc[oldKey];
        jc.Remove(oldKey);
        jc.Add(newKey, n);
    }

    // A JSON file's bytes as JSON, past the UTF-8 byte order mark an editor
    // may have saved: the mark is not JSON.
    public static JSONNode ParseBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        int start = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) start = 3;
        string text = Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
        return JSON.Parse(text);
    }
}
