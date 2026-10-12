using System.Collections.Generic;
using System.Text;
using UnityEngine;

// One CustomUnityAsset atom: its loader, the source materials of the asset
// it loaded, and where its ground is. The loaded prefab is parented under the
// loader's transform, so the renderers below the loader are the asset's and
// nothing else's.
//
// Gather is the only place the renderers are read, and it runs only when the
// tree needs it (NeedsGather): the loader loaded or cleared an asset, the
// tree's precedence changed, the scan found a renderer gone, or everything
// was released. A tree that is merely Dirty keeps its materials and has the
// effects re-applied to them. A gather starts from a released tree, so it
// always reads the asset's own materials, never this plugin's copies.
sealed class Tree
{
    // The renderers of the tree being gathered; cleared after each gather so
    // no destroyed asset is kept alive by it.
    static readonly List<Renderer> found = new List<Renderer>(64);

    public Atom Atom;
    public string Uid;
    public string Key;
    public CustomUnityAssetLoader Loader;
    public bool Yielded;
    public bool Dirty = true;
    public bool NeedsGather = true;
    public readonly List<LeafMaterial> Materials = new List<LeafMaterial>();

    readonly TreeRegistry registry;
    TreeLooks looks;
    float groundOffset;   // the asset's lowest point, below the loader
    string assetName;
    bool nameRead;
    CustomUnityAssetLoader.AssetLoadedCallback onLoaded;
    CustomUnityAssetLoader.AssetClearedCallback onCleared;
    bool hooked;

    public Tree(TreeRegistry registry, Atom atom, CustomUnityAssetLoader loader, string key)
    {
        this.registry = registry;
        Atom = atom;
        Uid = atom != null ? atom.uid : "";
        Key = key;
        Loader = loader;
    }

    // The lowest point of the asset, kept as an offset from the loader so a
    // tree moved after its gather still drops its leaves to its own foot.
    public float GroundY
    {
        get { return Loader != null ? Loader.transform.position.y + groundOffset : groundOffset; }
    }

    // The tree's own switch on the Trees page; on until the looks say off.
    // Before the first gather the registry's looks answer, so a tree a scene
    // saved as off never reads on while its asset is still loading.
    public bool Enabled
    {
        get
        {
            TreeLooks l = looks != null ? looks : (registry != null ? registry.Looks : null);
            return l == null || l.IsEnabled(Key);
        }
    }

    public int ConvertedCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < Materials.Count; i++)
                if (Materials[i].Kind == LeafMaterial.KindLeaf) n++;
            return n;
        }
    }

    // VaM calls these while it walks its own callback list, so they only set
    // flags; the next Update does the work.
    public void Hook(OverlayLog log)
    {
        if (hooked || Loader == null) return;
        onLoaded = AssetChanged;
        onCleared = AssetChanged;
        log.RunWarn("could not follow the asset of " + Uid, delegate
        {
            Loader.RegisterAssetLoadedCallback(onLoaded);
            Loader.RegisterAssetClearedCallback(onCleared);
            hooked = true;
        });
    }

    public void Unhook()
    {
        if (!hooked) return;
        hooked = false;
        if (Loader == null) return;
        Loader.DeregisterAssetLoadedCallback(onLoaded);
        Loader.DeregisterAssetClearedCallback(onCleared);
    }

    void AssetChanged()
    {
        NeedsGather = true;
        Dirty = true;
        nameRead = false;
    }

    // The asset the atom has loaded, as its maker named it ("Sakura_Tree"),
    // for the tree editor: the loader places the prefab as "<prefab>(Clone)",
    // in the prefab's own case; until that exists, the file name in the Asset
    // chooser's path, which the bundle keeps in lower case. Null with no
    // asset. Read again only when the loader loads or clears one.
    public string AssetName
    {
        get
        {
            if (!nameRead)
            {
                nameRead = true;
                assetName = ReadAssetName();
            }
            return assetName;
        }
    }

    string ReadAssetName()
    {
        if (Loader == null) return null;
        string name = null;
        Transform root = Loader.transform;
        for (int i = 0; i < root.childCount && name == null; i++)
        {
            string child = root.GetChild(i).name;
            if (child.EndsWith("(Clone)", System.StringComparison.Ordinal)) name = child.Substring(0, child.Length - 7);
        }
        if (name == null)
        {
            JSONStorableStringChooser chooser = Loader.GetStringChooserJSONParam("assetName");
            string path = chooser != null ? chooser.val : null;
            if (string.IsNullOrEmpty(path) || path == "None") return null;
            int slash = path.LastIndexOf('/');
            name = slash >= 0 ? path.Substring(slash + 1) : path;
            int dot = name.LastIndexOf('.');
            if (dot > 0) name = name.Substring(0, dot);
        }
        return name.Length > 0 ? name : null;
    }

    // Mesh and skinned mesh renderers only: a particle or line renderer in
    // the prefab is no foliage, and the falling leaves' own systems sit under
    // the same loader. The ground is the lowest point of the asset.
    public void Gather(TreeLooks looks, Shader leafShader, bool otherShaders)
    {
        NeedsGather = false;
        this.looks = looks;
        if (Materials.Count > 0) ReleaseMaterials();
        if (Loader == null) return;
        found.Clear();
        Loader.GetComponentsInChildren<Renderer>(true, found);
        bool anyBounds = false;
        float baseY = Loader.transform.position.y;
        float lowest = baseY;
        for (int i = 0; i < found.Count; i++)
        {
            Renderer r = found[i];
            if (r == null || (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer))) continue;
            float bottom = r.bounds.min.y;
            if (!anyBounds || bottom < lowest) lowest = bottom;
            anyBounds = true;
            Material[] mats = r.sharedMaterials;
            for (int s = 0; s < mats.Length; s++)
            {
                if (mats[s] == null) continue;
                LeafMaterial m = Find(mats[s]);
                if (m == null)
                {
                    m = new LeafMaterial();
                    m.Owner = this;
                    m.Source = mats[s];
                    Materials.Add(m);
                }
                m.Renderers.Add(r);
                m.SlotIndices.Add(s);
            }
        }
        found.Clear();
        groundOffset = lowest - baseY;
        for (int i = 0; i < Materials.Count; i++)
        {
            LeafMaterial m = Materials[i];
            m.Name = UniqueName(i);
            m.Foreign = LeafRules.IsOurs(m.Source, leafShader);
            LeafRules.Judge(m.Source, leafShader, otherShaders, out m.Candidate, out m.Reason);
            m.Look = m.Foreign || looks == null ? null : looks.Get(Key, m.Name, m.Source);
        }
    }

    LeafMaterial Find(Material source)
    {
        for (int i = 0; i < Materials.Count; i++)
            if (Materials[i].Source == source) return Materials[i];
        return null;
    }

    // Duplicate material names inside one asset get " #2", " #3" in the
    // order the renderers list them, which a reload of the same asset repeats.
    string UniqueName(int index)
    {
        string name = Materials[index].Source.name;
        int n = 1;
        for (int i = 0; i < index; i++)
            if (Materials[i].Source.name == name) n++;
        return n == 1 ? name : name + " #" + n;
    }

    // Every material back to its source: the effects give back what they own
    // (the registry's hooks), then any slot still showing a copy, because a
    // hook failed, is put back here. The tree is gathered again before the
    // effects touch it next.
    public void Release()
    {
        ReleaseMaterials();
        NeedsGather = true;
        Dirty = true;
    }

    void ReleaseMaterials()
    {
        for (int i = 0; i < Materials.Count; i++)
        {
            LeafMaterial m = Materials[i];
            if (registry != null && registry.ReleaseEffects != null) registry.ReleaseEffects(m);
            if (m.Copy != null)
            {
                m.Restore();
                UnityEngine.Object.Destroy(m.Copy);
                m.Copy = null;
            }
            else if (m.Kind != LeafMaterial.KindOriginal && m.Source != null) m.Assign(m.Source);
            m.Kind = LeafMaterial.KindOriginal;
        }
        Materials.Clear();
    }

    // "not converted" means leaves wait for a fix that is not running (Shadow
    // Fix off, Enabled off, no shader yet); "off" is the tree's own switch.
    public string State()
    {
        if (Yielded) return "own FoliageFX";
        if (!Enabled) return "off";
        if (Materials.Count == 0)
            return NeedsGather || Loader == null || !Loader.isAssetLoaded ? "loading" : "nothing to fix";
        int converted = ConvertedCount;
        if (converted > 0) return converted + " of " + Materials.Count + " converted";
        for (int i = 0; i < Materials.Count; i++)
            if (Materials[i].WantsLeaf(true)) return "not converted";
        return "nothing to fix";
    }

    public void Describe(StringBuilder sb)
    {
        sb.Append(Uid);
        if (Key != Uid) sb.Append(" [").Append(Key).Append(']');
        sb.Append(": ").Append(State());
        if (Yielded || Materials.Count == 0) return;
        sb.Append(", ground ").Append(GroundY.ToString("F2")).Append(" m (");
        for (int i = 0; i < Materials.Count; i++)
        {
            LeafMaterial m = Materials[i];
            if (i > 0) sb.Append(", ");
            sb.Append(m.Name).Append(' ').Append(m.StateLabel());
            if (m.Source != null && m.Source.shader != null) sb.Append(" on ").Append(m.Source.shader.name).Append(" q").Append(m.Source.renderQueue);
            sb.Append(" x").Append(m.Renderers.Count);
        }
        sb.Append(')');
    }
}
