using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;

// Every tree's looks, by tree key and material name, and each tree's switch:
// the scene's "trees" node. A tree key is its atom's uid, or SelfTreeKey for
// the atom the plugin sits on, so those looks travel with the atom and a
// rename leaves them alone. A look outlives the gather that made it: a tree
// that loads its asset again finds the same looks under the same names.
//
// Only what the user made is written: customised looks, and trees switched
// off. A look nobody touched is the default its source gives and is made
// again from it, so a scene never pins a default that a later asset version
// would read differently.
sealed class TreeLooks
{
    sealed class Set
    {
        public string Key;
        public bool Enabled = true;
        public readonly List<string> Names = new List<string>();
        public readonly List<LeafLook> Looks = new List<LeafLook>();

        public LeafLook Find(string name)
        {
            for (int i = 0; i < Names.Count; i++)
                if (Names[i] == name) return Looks[i];
            return null;
        }
    }

    readonly List<Set> sets = new List<Set>();
    readonly Dictionary<string, Set> byKey = new Dictionary<string, Set>();

    public LeafLook Get(string treeKey, string materialName, Material source)
    {
        Set s = Ensure(treeKey);
        LeafLook look = s.Find(materialName);
        if (look != null) return look;
        look = new LeafLook();
        look.Reset(source);
        s.Names.Add(materialName);
        s.Looks.Add(look);
        return look;
    }

    public bool IsEnabled(string treeKey)
    {
        Set s;
        return treeKey == null || !byKey.TryGetValue(treeKey, out s) || s.Enabled;
    }

    public void SetEnabled(string treeKey, bool on)
    {
        if (treeKey == null) return;
        Ensure(treeKey).Enabled = on;
    }

    // An atom renamed in VaM: its looks follow it. A set already under the new
    // key belonged to an atom that is gone (VaM keeps uids unique), so it goes.
    public void Rename(string oldKey, string newKey)
    {
        if (oldKey == null || newKey == null || oldKey == newKey) return;
        Set s;
        if (!byKey.TryGetValue(oldKey, out s)) return;
        Set taken;
        if (byKey.TryGetValue(newKey, out taken)) { sets.Remove(taken); byKey.Remove(newKey); }
        byKey.Remove(oldKey);
        s.Key = newKey;
        byKey[newKey] = s;
    }

    public void Forget(string key)
    {
        Set s;
        if (key == null || !byKey.TryGetValue(key, out s)) return;
        sets.Remove(s);
        byKey.Remove(key);
    }

    // Every look of the tree back to its defaults, the materials of an
    // earlier asset included, and the tree switched on. A loaded material's
    // look is reset again from its source by the caller, for its smoothness.
    public void ResetTree(string treeKey)
    {
        if (treeKey == null) return;
        Set s = Ensure(treeKey);
        s.Enabled = true;
        for (int i = 0; i < s.Looks.Count; i++) s.Looks[i].Reset(null);
    }

    public int CustomizedCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < sets.Count; i++)
                for (int k = 0; k < sets[i].Looks.Count; k++)
                    if (sets[i].Looks[k].Customized) n++;
            return n;
        }
    }

    // The looks of an atom that has left the scene are not written: a scene
    // saved after a tree was deleted does not carry its looks on for ever.
    public JSONClass Write()
    {
        JSONClass trees = new JSONClass();
        SuperController sc = SuperController.singleton;
        for (int i = 0; i < sets.Count; i++)
        {
            Set s = sets[i];
            if (s.Key != FoliageFXConfig.SelfTreeKey && sc != null && sc.GetAtomByUid(s.Key) == null) continue;
            JSONClass materials = new JSONClass();
            for (int k = 0; k < s.Looks.Count; k++)
                if (s.Looks[k].Customized) materials.Add(s.Names[k], s.Looks[k].Write());
            if (materials.Count == 0 && s.Enabled) continue;
            JSONClass tree = new JSONClass();
            tree.Add("enabled", new JSONData(s.Enabled));
            tree.Add("materials", materials);
            trees.Add(s.Key, tree);
        }
        return trees;
    }

    // Replaces everything: what the node does not name is gone, and its
    // materials take fresh looks from their sources at the next gather.
    public void Read(JSONClass trees)
    {
        sets.Clear();
        byKey.Clear();
        if (trees == null) return;
        foreach (KeyValuePair<string, JSONNode> kv in trees)
        {
            JSONClass tree = kv.Value as JSONClass;
            if (tree == null || string.IsNullOrEmpty(kv.Key)) continue;
            Set s = Ensure(kv.Key);
            bool on;
            if (tree.HasKey("enabled") && bool.TryParse((tree["enabled"].Value ?? "").Trim(), out on)) s.Enabled = on;
            JSONClass materials = tree.HasKey("materials") ? tree["materials"] as JSONClass : null;
            if (materials == null) continue;
            foreach (KeyValuePair<string, JSONNode> m in materials)
            {
                JSONClass node = m.Value as JSONClass;
                if (node == null || s.Find(m.Key) != null) continue;
                s.Names.Add(m.Key);
                s.Looks.Add(LeafLook.Read(node));
            }
        }
    }

    Set Ensure(string key)
    {
        Set s;
        if (byKey.TryGetValue(key, out s)) return s;
        s = new Set();
        s.Key = key;
        sets.Add(s);
        byKey[key] = s;
        return s;
    }
}
