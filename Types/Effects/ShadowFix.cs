using System.Text;
using UnityEngine;

// Converts and restores: decides each material's kind, makes or drops the
// copy, and writes the look into it. A leaf copy is a new material made from
// the source with the shader swapped for the leaf shader; the property names
// match the Standard shader's, so textures, tiling, colour and smoothness
// carry over, and the copy goes into the AlphaTest queue where it casts and
// receives shadows. A material that stays as it is but was recoloured gets a
// tint copy, the source with only its colour changed. Either way the source
// itself is never written; Release puts it back in its slots and destroys
// the copy.
//
// Off (this switch, the tree's own, the master, or no shader) means every
// slot holds its source again and no copy exists.
sealed class ShadowFix
{
    public const string Id = FoliageFXConfig.ShadowFixId;

    // Mip levels average a leaf texture's alpha toward grey, so a distant
    // canopy thins out under a cutoff; the shader scales alpha up with the
    // mip level by this much. A texture without mips has nothing to make up.
    const float MipCoverage = 0.25f;
    // _Cull: 0 draws both faces of a leaf card, 2 culls the back.
    const float CullOff = 0f;
    const float CullBack = 2f;

    static readonly int MainTexId = Shader.PropertyToID(FoliageFXConfig.LeafTextureProperty);
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
    static readonly int HueShiftId = Shader.PropertyToID("_HueShift");
    static readonly int SaturationId = Shader.PropertyToID("_Saturation");
    static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
    static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
    static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");
    static readonly int TranslucencyId = Shader.PropertyToID("_Translucency");
    static readonly int CullId = Shader.PropertyToID("_Cull");
    static readonly int SoftEdgesId = Shader.PropertyToID("_SoftEdges");
    static readonly int MipCoverageId = Shader.PropertyToID("_MipCoverage");

    public JSONStorableBool Enabled;
    public JSONStorableBool SoftEdges;
    public JSONStorableBool OtherShaders;
    public JSONStorableBool AllAtoms;
    // Set by Lifecycle whenever the leaf shader comes or goes, so the state
    // line can say "no shader" before any tree was applied.
    public bool ShaderReady;

    public void Create(ParamSet set)
    {
        Enabled = set.Bool(Id, ParamSet.Look, "enabled", new JSONStorableBool("Shadow Fix", true));
        SoftEdges = set.Bool(Id, ParamSet.Look, "soft_edges", new JSONStorableBool("Shadow Fix Soft Edges", true));
        OtherShaders = set.Bool(Id, ParamSet.Look, "other_shaders", new JSONStorableBool("Shadow Fix Convert Other Shaders", false));
        // Scene scope: which atoms a scene's instance looks after is about
        // that scene, and Lifecycle.Attach sets its default from the atom.
        AllAtoms = set.Bool(Id, ParamSet.Scene, "all_atoms", new JSONStorableBool("Shadow Fix All Atoms", true));
    }

    // ---- effect contract ----

    // Writes property values every time (a slider being dragged lands here
    // each frame), but makes or destroys a copy only when a kind changes, so
    // a pass over an applied tree allocates nothing.
    public void ApplyTree(Tree tree, Shader leafShader, bool msaa)
    {
        bool on = Enabled.val && leafShader != null && tree.Enabled && !tree.Yielded;
        bool soft = SoftEdges.val && msaa;
        for (int i = 0; i < tree.Materials.Count; i++)
        {
            LeafMaterial m = tree.Materials[i];
            if (m.Foreign || m.Source == null || m.Look == null) continue;
            int want = LeafMaterial.KindOriginal;
            if (m.WantsLeaf(on)) want = LeafMaterial.KindLeaf;
            else if (on && m.Look.Tinted && m.Source.HasProperty(ColorId)) want = LeafMaterial.KindTint;
            // A copy destroyed behind our back (another plugin, a scene
            // clear) is made again rather than left as a missing material.
            if (want != m.Kind || (want != LeafMaterial.KindOriginal && m.Copy == null))
            {
                // A slot another instance has filled since this tree was read
                // is its: the tree is read again, and the material then
                // counts as that instance's.
                if (want != LeafMaterial.KindOriginal && m.Copy == null && !m.SlotsHold(m.Source))
                {
                    tree.NeedsGather = true;
                    tree.Dirty = true;
                    continue;
                }
                Switch(m, want, leafShader);
            }
            if (m.Kind == LeafMaterial.KindLeaf) WriteLook(m, soft);
            else if (m.Kind == LeafMaterial.KindTint) m.Copy.SetColor(ColorId, m.TintedColor());
        }
    }

    // The source goes back first, so a MakeCopy that throws leaves the
    // renderers with a material, never with a destroyed one.
    void Switch(LeafMaterial m, int want, Shader leafShader)
    {
        if (m.Copy != null)
        {
            m.Restore();
            UnityEngine.Object.Destroy(m.Copy);
            m.Copy = null;
        }
        m.Kind = LeafMaterial.KindOriginal;
        if (want == LeafMaterial.KindLeaf) m.Copy = MakeCopy(m.Source, leafShader);
        else if (want == LeafMaterial.KindTint) m.Copy = MakeTint(m.Source);
        if (m.Copy != null) m.Assign(m.Copy);
        m.Kind = want;
    }

    // The Standard shader samples its normal map only with _NORMALMAP on, so
    // a Standard source's keyword decides; a legacy shader always used its
    // bump map. Every other Standard keyword (the blend modes among them) is
    // dropped: the leaf shader has none.
    static Material MakeCopy(Material source, Shader leafShader)
    {
        bool standard = source.shader != null && source.shader.name.StartsWith("Standard", System.StringComparison.Ordinal);
        bool bump = source.HasProperty(BumpMapId) && source.GetTexture(BumpMapId) != null
            && (!standard || source.IsKeywordEnabled(FoliageFXConfig.NormalMapKeyword));
        Texture2D main = source.HasProperty(MainTexId) ? source.GetTexture(MainTexId) as Texture2D : null;
        Material m = new Material(source);
        m.name = source.name + FoliageFXConfig.CopySuffix;
        m.shader = leafShader;
        m.shaderKeywords = bump ? new string[] { FoliageFXConfig.NormalMapKeyword } : new string[0];
        m.SetOverrideTag("RenderType", "TransparentCutout");
        m.renderQueue = FoliageFXConfig.LeafQueue;
        m.SetFloat(MipCoverageId, main != null && main.mipmapCount > 1 ? MipCoverage : 0f);
        return m;
    }

    static Material MakeTint(Material source)
    {
        Material m = new Material(source);
        m.name = source.name + FoliageFXConfig.CopySuffix;
        return m;
    }

    static void WriteLook(LeafMaterial m, bool soft)
    {
        Material c = m.Copy;
        LeafLook look = m.Look;
        c.SetColor(ColorId, m.TintedColor());
        c.SetFloat(HueShiftId, look.Hue);
        c.SetFloat(SaturationId, look.Saturation);
        c.SetFloat(BrightnessId, look.Brightness);
        c.SetFloat(CutoffId, look.Cutoff);
        c.SetFloat(GlossinessId, look.Smoothness);
        c.SetFloat(TranslucencyId, look.Translucency);
        c.SetFloat(CullId, look.DoubleSided ? CullOff : CullBack);
        c.SetFloat(SoftEdgesId, soft ? 1f : 0f);
    }

    // The source goes back into the slots that still show the copy before
    // the copy is destroyed, so a renderer never draws a missing material; a
    // slot another instance has taken meanwhile is left to it. Safe to call twice.
    public void Release(LeafMaterial m)
    {
        if (m == null) return;
        if (m.Copy != null) m.Restore();
        else if (m.Kind != LeafMaterial.KindOriginal && m.Source != null) m.Assign(m.Source);
        m.Kind = LeafMaterial.KindOriginal;
        if (m.Copy != null) UnityEngine.Object.Destroy(m.Copy);
        m.Copy = null;
    }

    // ---- UI text ----

    public string State(TreeRegistry trees)
    {
        if (!Enabled.val) return "off";
        if (!ShaderReady) return "no shader";
        int materials = 0, onTrees = 0;
        if (trees != null)
            for (int i = 0; i < trees.Trees.Count; i++)
            {
                int n = trees.Trees[i].ConvertedCount;
                materials += n;
                if (n > 0) onTrees++;
            }
        string s = materials + (materials == 1 ? " material on " : " materials on ") + onTrees + (onTrees == 1 ? " tree" : " trees");
        if (SoftEdges.val && QualitySettings.antiAliasing <= 1) s += ", hard edges (MSAA off)";
        return s;
    }

    public void Describe(StringBuilder sb)
    {
        int aa = QualitySettings.antiAliasing;
        sb.Append(" | shadow fix: ").Append(Enabled.val ? "on" : "off")
          .Append(", soft edges ").Append(SoftEdges.val ? "on" : "off").Append(" (MSAA ").Append(aa > 1 ? "x" + aa : "off").Append(')')
          .Append(", other shaders ").Append(OtherShaders.val ? "on" : "off")
          .Append(", all atoms ").Append(AllAtoms.val ? "on" : "off")
          .Append(", shader ").Append(ShaderReady ? "ready" : "missing");
    }
}
