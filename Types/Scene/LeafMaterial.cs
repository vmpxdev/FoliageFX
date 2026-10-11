using System.Collections.Generic;
using UnityEngine;

// One source material on a tree: the asset's own material, the verdict on it,
// its look, and every renderer slot it fills. The source is never edited.
// What the effects make is written into the slots this class holds, through
// Assign, the one place a renderer's materials are written, and the slots get
// the source back on release. Kind says what the slots show: the source, a
// leaf copy on the leaf shader, or a tint copy (the source with only its
// colour changed, for a material that stays as it is but was recoloured).
sealed class LeafMaterial
{
    public const int KindOriginal = 0;
    public const int KindLeaf = 1;
    public const int KindTint = 2;

    static readonly int ColorId = Shader.PropertyToID("_Color");

    public Tree Owner;
    public string Name;
    public Material Source;
    public LeafLook Look;            // null for a Foreign material: another instance's look rules it
    public bool Candidate;
    public string Reason;
    public bool Foreign;
    public int Kind;
    public Material Copy;            // ShadowFix's
    public Material FallingMaterial; // the falling leaves' atlas material, FallingLeaves'
    public LeafEmitter Emitter;      // FallingLeaves'
    public readonly List<Renderer> Renderers = new List<Renderer>();
    public readonly List<int> SlotIndices = new List<int>();

    public bool Converted { get { return Kind == KindLeaf; } }

    public bool WantsLeaf(bool shadowFixOn)
    {
        if (!shadowFixOn || Foreign || Look == null) return false;
        return Look.Mode == FoliageFXConfig.ModeConvert || (Look.Mode == FoliageFXConfig.ModeAuto && Candidate);
    }

    // A renderer hands out a copy of its material array, so each slot is a
    // read, a write into the copy, and the array set back. A renderer the
    // asset destroyed is skipped.
    public void Assign(Material m)
    {
        for (int i = 0; i < Renderers.Count; i++)
        {
            Renderer r = Renderers[i];
            if (r == null) continue;
            Material[] mats = r.sharedMaterials;
            int slot = SlotIndices[i];
            if (slot < 0 || slot >= mats.Length || mats[slot] == m) continue;
            mats[slot] = m;
            r.sharedMaterials = mats;
        }
    }

    // The source back into every slot that still shows this material's copy;
    // a slot another instance has filled meanwhile is left to it.
    public void Restore()
    {
        if (Copy == null || Source == null) return;
        for (int i = 0; i < Renderers.Count; i++)
        {
            Renderer r = Renderers[i];
            if (r == null) continue;
            Material[] mats = r.sharedMaterials;
            int slot = SlotIndices[i];
            if (slot < 0 || slot >= mats.Length || mats[slot] != Copy) continue;
            mats[slot] = Source;
            r.sharedMaterials = mats;
        }
    }

    // Whether every slot of this material still shows m.
    public bool SlotsHold(Material m)
    {
        for (int i = 0; i < Renderers.Count; i++)
        {
            Renderer r = Renderers[i];
            if (r == null) return false;
            Material[] mats = r.sharedMaterials;
            int slot = SlotIndices[i];
            if (slot < 0 || slot >= mats.Length || mats[slot] != m) return false;
        }
        return true;
    }

    // The space the falling leaves drop from: every renderer this material is on.
    public bool CanopyBounds(out Bounds b)
    {
        b = new Bounds();
        bool any = false;
        for (int i = 0; i < Renderers.Count; i++)
        {
            Renderer r = Renderers[i];
            if (r == null) continue;
            if (any) b.Encapsulate(r.bounds);
            else b = r.bounds;
            any = true;
        }
        return any;
    }

    // The source's colour times the look's tint, the source's alpha kept: a
    // white tint leaves the asset's colour exactly as it was.
    public Color TintedColor()
    {
        Color source = Source != null && Source.HasProperty(ColorId) ? Source.GetColor(ColorId) : Color.white;
        Color tint = Look != null ? HSVColorPicker.HSVToRGB(Look.Tint) : Color.white;
        return new Color(source.r * tint.r, source.g * tint.g, source.b * tint.b, source.a);
    }

    public string StateLabel()
    {
        if (Foreign) return "other FoliageFX";
        bool forced = Look != null && Look.Mode == FoliageFXConfig.ModeConvert && !Candidate;
        if (Kind == KindLeaf) return forced ? "converted, forced" : "converted";
        if (Kind == KindTint) return "original, tinted";
        if (Look != null && Look.Mode == FoliageFXConfig.ModeKeep && Candidate) return "kept original";
        return "original";
    }
}
