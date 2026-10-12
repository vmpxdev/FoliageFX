using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Leaves cut from each tree's own texture drop from the canopy. One emitter
// per material that is converted and whose look says Falling; a tree's rate
// is shared among its dropping materials, so a tree with three leaf
// materials drops as many leaves as one with a single material. Each emitter
// draws with a leaf material of its own: a new material on the leaf shader
// with the atlas as its texture and the look's colour settings, double-sided
// and without sway (the particles move on their own).
//
// The leaf shapes come from a sprite job per source texture, shared by every
// material using that texture and run as a coroutine on the plugin; a job
// that has not answered within SpriteJobTimeoutSeconds is dropped and
// started again. A tree that asked for shapes still being found is marked
// dirty when they arrive. This class owns the emitters, the leaf materials
// and the jobs' results, and destroys them in Release and ReleaseAll.
sealed class FallingLeaves
{
    public const string Id = FoliageFXConfig.FallingId;

    // _Cull 0: a falling leaf shows both faces as it tumbles.
    const float CullOff = 0f;

    static readonly int MainTexId = Shader.PropertyToID(FoliageFXConfig.LeafTextureProperty);
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    static readonly int HueShiftId = Shader.PropertyToID("_HueShift");
    static readonly int SaturationId = Shader.PropertyToID("_Saturation");
    static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
    static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
    static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");
    static readonly int TranslucencyId = Shader.PropertyToID("_Translucency");
    static readonly int CullId = Shader.PropertyToID("_Cull");
    static readonly int SoftEdgesId = Shader.PropertyToID("_SoftEdges");
    static readonly int WindStrengthId = Shader.PropertyToID("_WindStrength");

    sealed class SpriteJob
    {
        public Texture Source;
        public LeafSprites Sprites;
        public bool Done;
        public float Started;
    }

    public JSONStorableBool Enabled;
    public JSONStorableBool CastShadows;
    public JSONStorableFloat Rate;
    public JSONStorableFloat SizeMm;
    public JSONStorableFloat FallSpeed;
    public JSONStorableFloat RestSeconds;
    // Set by Lifecycle.Attach: each step of a leaf-shape extraction runs inside its
    // boundary, so a texture that breaks the routine ends that job with no
    // shapes instead of a coroutine dying silently and being retried for ever.
    public OverlayLog Log;

    readonly List<SpriteJob> jobs = new List<SpriteJob>();
    readonly List<LeafEmitter> emitters = new List<LeafEmitter>();
    readonly List<Material> leafMaterials = new List<Material>();
    readonly List<Tree> waiting = new List<Tree>();
    readonly List<LeafSprites> resolved = new List<LeafSprites>();   // this apply's shapes per material

    public void Create(ParamSet set)
    {
        Enabled = set.Bool(Id, ParamSet.Look, "enabled", new JSONStorableBool("Falling Leaves", false));
        CastShadows = set.Bool(Id, ParamSet.Look, "shadows", new JSONStorableBool("Falling Leaves Cast Shadows", true));
        // The rate and the times on the ground and in the air size the
        // particle systems, so their ends are ceilings; the leaf size
        // allocates nothing and is open.
        Rate = set.Float(Id, ParamSet.Look, "rate", new JSONStorableFloat("Falling Leaves Rate", 3f, 0.1f, 30f, true));
        SizeMm = set.Float(Id, ParamSet.Look, "size_mm", new JSONStorableFloat("Falling Leaves Size mm", 50f, 10f, 300f, false));
        FallSpeed = set.Float(Id, ParamSet.Look, "fall_speed", new JSONStorableFloat("Falling Leaves Fall Speed m/s", 0.7f, 0.1f, 3f, true));
        RestSeconds = set.Float(Id, ParamSet.Look, "rest_s", new JSONStorableFloat("Falling Leaves Time On Ground s", 8f, 0f, 60f, true));
    }

    bool Wants(Tree tree, LeafMaterial m)
    {
        return Enabled.val && !tree.Yielded && !m.Foreign && m.Converted && m.Look != null && m.Look.Falling;
    }

    // ---- effect contract ----

    // An emitter is made once and then only re-tuned, so a slider dragged
    // over an applied tree allocates nothing; it is made again only when its
    // leaf shapes changed (another texture, a job that finished again).
    public void ApplyTree(Tree tree, MonoBehaviour host, Shader leafShader, Wind wind, bool msaa, bool softEdges)
    {
        DropGoneJobs();
        // The rate is shared among the materials that drop something: one
        // whose shapes are still being found, or whose texture has none, takes
        // no share, so a tree drops at its rate from the first leaf.
        resolved.Clear();
        int dropping = 0;
        for (int i = 0; i < tree.Materials.Count; i++)
        {
            LeafMaterial m = tree.Materials[i];
            LeafSprites found = Wants(tree, m) && leafShader != null && tree.Loader != null ? SpritesFor(m, host, tree) : null;
            resolved.Add(found);
            if (found != null) dropping++;
        }
        float rate = Rate.val / Mathf.Max(1, dropping);
        float windStrength = wind != null && wind.Enabled.val ? wind.Strength.val : 0f;
        float windDeg = wind != null ? wind.Direction.val : 0f;
        bool soft = softEdges && msaa;
        for (int i = 0; i < tree.Materials.Count; i++)
        {
            LeafMaterial m = tree.Materials[i];
            LeafSprites sprites = resolved[i];
            if (sprites == null)
            {
                Release(m);
                continue;
            }
            if (m.Emitter == null || m.Emitter.Falling == null || m.Emitter.Sprites != sprites)
            {
                Release(m);
                Bounds canopy;
                if (!m.CanopyBounds(out canopy)) continue;
                m.FallingMaterial = MakeLeafMaterial(m, sprites, leafShader);
                leafMaterials.Add(m.FallingMaterial);
                m.Emitter = LeafEmitter.Create(tree.Loader.transform, canopy, tree.GroundY, sprites, m.FallingMaterial);
                emitters.Add(m.Emitter);
            }
            m.Emitter.Apply(rate, SizeMm.val * 0.001f, FallSpeed.val, windStrength, windDeg, RestSeconds.val, CastShadows.val);
            WriteLook(m, soft);
        }
    }

    // The scan tick: a job that has not answered in time is dropped and
    // started again by the next apply of the trees that wait for it.
    public void Tick()
    {
        float now = Time.unscaledTime;
        bool stalled = false;
        for (int i = 0; i < jobs.Count; i++)
            if (!jobs[i].Done && now - jobs[i].Started > FoliageFXConfig.SpriteJobTimeoutSeconds) stalled = true;
        if (!stalled) return;
        for (int i = 0; i < waiting.Count; i++) waiting[i].Dirty = true;
        waiting.Clear();
    }

    // The shapes for this material's texture: null while they are being
    // found, and when the texture has none.
    LeafSprites SpritesFor(LeafMaterial m, MonoBehaviour host, Tree tree)
    {
        Texture tex = m.Source != null && m.Source.HasProperty(MainTexId) ? m.Source.GetTexture(MainTexId) : null;
        if (tex == null || host == null) return null;
        SpriteJob job = FindJob(tex);
        if (job != null && !job.Done && Time.unscaledTime - job.Started > FoliageFXConfig.SpriteJobTimeoutSeconds)
        {
            jobs.Remove(job);
            job = null;
        }
        if (job == null)
        {
            job = new SpriteJob();
            job.Source = tex;
            job.Sprites = new LeafSprites();
            job.Started = Time.unscaledTime;
            jobs.Add(job);
            host.StartCoroutine(Run(job));
        }
        if (!job.Done)
        {
            if (!waiting.Contains(tree)) waiting.Add(tree);
            return null;
        }
        return job.Sprites.ShapeCount > 0 ? job.Sprites : null;
    }

    SpriteJob FindJob(Texture tex)
    {
        for (int i = 0; i < jobs.Count; i++)
            if (jobs[i].Source == tex) return jobs[i];
        return null;
    }

    IEnumerator Run(SpriteJob job)
    {
        IEnumerator steps = LeafSprites.Extract(job.Source, job.Sprites, delegate { Finished(job); });
        bool more = false;
        OverlayLog.Fn step = delegate { more = steps.MoveNext(); };
        while (true)
        {
            if (!Log.RunOnce("find the leaf shapes of a texture", step))
            {
                Finished(job);
                yield break;
            }
            if (!more) yield break;
            yield return steps.Current;
        }
    }

    // A job dropped meanwhile (timed out, or everything released) keeps
    // nothing: its result is destroyed on arrival.
    void Finished(SpriteJob job)
    {
        if (job.Done) return;
        if (!jobs.Contains(job))
        {
            job.Sprites.Destroy();
            return;
        }
        job.Done = true;
        if (job.Sprites.ShapeCount == 0 && Log != null)
            Log.Warn("No falling leaves from the texture " + (job.Source != null ? job.Source.name : "") + ": " + Refusal(job));
        for (int i = 0; i < waiting.Count; i++) waiting[i].Dirty = true;
        waiting.Clear();
    }

    // A texture the asset destroyed (a cleared or replaced asset) takes its
    // shapes with it; its materials were released with their tree.
    void DropGoneJobs()
    {
        for (int i = jobs.Count - 1; i >= 0; i--)
        {
            if (jobs[i].Source != null) continue;
            jobs[i].Sprites.Destroy();
            jobs.RemoveAt(i);
        }
    }

    // The source's metallic carries over; everything the look sets is
    // written by WriteLook on every apply.
    static Material MakeLeafMaterial(LeafMaterial m, LeafSprites sprites, Shader leafShader)
    {
        Material mat = new Material(leafShader);
        mat.name = m.Name + " falling leaves" + FoliageFXConfig.CopySuffix;
        mat.SetTexture(MainTexId, sprites.Atlas);
        mat.SetOverrideTag("RenderType", "TransparentCutout");
        mat.renderQueue = FoliageFXConfig.LeafQueue;
        mat.SetFloat(MetallicId, m.Source != null && m.Source.HasProperty(MetallicId) ? m.Source.GetFloat(MetallicId) : 0f);
        return mat;
    }

    // The same look as the leaves on the tree.
    static void WriteLook(LeafMaterial m, bool soft)
    {
        Material mat = m.FallingMaterial;
        LeafLook look = m.Look;
        if (mat == null || look == null) return;
        mat.SetColor(ColorId, m.TintedColor());
        mat.SetFloat(HueShiftId, look.Hue);
        mat.SetFloat(SaturationId, look.Saturation);
        mat.SetFloat(BrightnessId, look.Brightness);
        mat.SetFloat(CutoffId, look.Cutoff);
        mat.SetFloat(GlossinessId, look.Smoothness);
        mat.SetFloat(TranslucencyId, look.Translucency);
        mat.SetFloat(CullId, CullOff);
        mat.SetFloat(SoftEdgesId, soft ? 1f : 0f);
        mat.SetFloat(WindStrengthId, 0f);
    }

    // Safe to call twice, and on an emitter the loader already destroyed.
    public void Release(LeafMaterial m)
    {
        if (m == null) return;
        if (m.Emitter != null)
        {
            emitters.Remove(m.Emitter);
            m.Emitter.Destroy();
            m.Emitter = null;
        }
        if (m.FallingMaterial != null)
        {
            leafMaterials.Remove(m.FallingMaterial);
            UnityEngine.Object.Destroy(m.FallingMaterial);
            m.FallingMaterial = null;
        }
    }

    // Everything this effect made, including anything a tree's release
    // missed, and every job's shapes. A job still running finds itself gone
    // when it answers and destroys its own result.
    public void ReleaseAll()
    {
        for (int i = 0; i < emitters.Count; i++) emitters[i].Destroy();
        emitters.Clear();
        for (int i = 0; i < leafMaterials.Count; i++)
            if (leafMaterials[i] != null) UnityEngine.Object.Destroy(leafMaterials[i]);
        leafMaterials.Clear();
        for (int i = 0; i < jobs.Count; i++) jobs[i].Sprites.Destroy();
        jobs.Clear();
        waiting.Clear();
    }

    // ---- UI text ----

    public string State()
    {
        if (!Enabled.val) return "off";
        for (int i = 0; i < jobs.Count; i++)
            if (!jobs[i].Done) return "finding leaf shapes";
        int falling = 0, resting = 0;
        for (int i = 0; i < emitters.Count; i++)
        {
            LeafEmitter e = emitters[i];
            if (e.Falling != null) falling += e.Falling.particleCount;
            if (e.Resting != null) resting += e.Resting.particleCount;
        }
        return falling + " falling, " + resting + " resting";
    }

    // The tree editor's line under the material.
    public string MaterialLine(LeafMaterial m)
    {
        if (m == null) return "";
        Texture tex = LeafTexture(m);
        if (tex == null) return "Falling leaves: none; this material has no leaf texture.";
        SpriteJob job = FindJob(tex);
        // Known to give nothing: said whatever the switches, since no switch changes it.
        if (job != null && job.Done && job.Sprites.ShapeCount == 0) return "Falling leaves: none; " + Refusal(job);
        if (!Enabled.val) return "Falling leaves: off on the Effects page.";
        if (!m.Converted) return "Falling leaves: only from converted materials.";
        if (m.Look == null || !m.Look.Falling) return "Falling leaves: off for this material.";
        if (job == null || !job.Done) return "Falling leaves: finding leaf shapes...";
        int n = job.Sprites.ShapeCount;
        return "Falling leaves: " + n + (n == 1 ? " leaf shape" : " leaf shapes") + " from the texture.";
    }

    // False once it is known that this material's texture gives no leaf
    // shapes, or when it has no texture: its Drop Falling Leaves then does
    // nothing, and the Trees page greys it. True while not yet known.
    public bool CanDrop(LeafMaterial m)
    {
        if (m == null) return false;
        Texture tex = LeafTexture(m);
        if (tex == null) return false;
        SpriteJob job = FindJob(tex);
        return job == null || !job.Done || job.Sprites.ShapeCount > 0;
    }

    static Texture LeafTexture(LeafMaterial m)
    {
        return m.Source != null && m.Source.HasProperty(MainTexId) ? m.Source.GetTexture(MainTexId) : null;
    }

    static string Refusal(SpriteJob job)
    {
        return job.Sprites.Refusal ?? "the texture could not be read.";
    }

    public void Describe(StringBuilder sb)
    {
        sb.Append(" | falling leaves: ").Append(Enabled.val ? "on" : "off")
          .Append(", rate ").Append(Rate.val.ToString("0.##")).Append("/s per tree")
          .Append(", size ").Append(Mathf.RoundToInt(SizeMm.val)).Append(" mm")
          .Append(", fall ").Append(FallSpeed.val.ToString("0.##")).Append(" m/s")
          .Append(", on ground ").Append(RestSeconds.val.ToString("0.#")).Append(" s")
          .Append(", shadows ").Append(CastShadows.val ? "on" : "off")
          .Append(", ").Append(emitters.Count).Append(emitters.Count == 1 ? " emitter (" : " emitters (").Append(State()).Append(')')
          .Append(", textures ").Append(jobs.Count);
        for (int i = 0; i < jobs.Count; i++)
        {
            SpriteJob j = jobs[i];
            sb.Append(i == 0 ? ": " : ", ").Append(j.Source != null ? j.Source.name : "gone").Append(' ');
            if (!j.Done) sb.Append("finding");
            else if (j.Sprites.ShapeCount > 0) sb.Append(j.Sprites.ShapeCount).Append(" shapes");
            else sb.Append("none: ").Append(Refusal(j));
        }
    }
}
