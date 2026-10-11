using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

// Owns the plugin's runtime: the master switch, the scan tick, the order the
// effects run in on a tree, release, and the frame timing.
//
//   Update: every SceneScanSeconds the registry scans (CustomUnityAsset
//   atoms that came or went, precedence) and MSAA is read; then each dirty
//   tree is gathered if its renderers are stale, and ShadowFix.ApplyTree,
//   Wind.Apply on each material and Falling.ApplyTree run on it, in that
//   order, each in its own RunOnce.
//
// The effects' switches and sliders carry no callbacks of ours: each frame
// their values are compared with the ones last applied (a dozen numbers, no
// allocation), and a change marks every tree, or has every tree read again
// when it changes the verdicts (Convert Other Shaders, the leaf shader
// arriving). A frame with nothing dirty applies nothing, so once every tree
// is done a frame compares those values and does nothing else: the sway is
// the shader's and the falling leaves are Unity's particle systems.
//
// Off (the master, or the plugin disabled) releases everything the effects
// made. The scan and the reading of the materials go on with the master off,
// so the Trees page still lists every tree's materials and verdicts; reading
// changes nothing in the scene.
sealed class Lifecycle
{
    // Cost slots, one per effect, addressed by feature id.
    const int FeatShadowFix = 0;
    const int FeatWind = 1;
    const int FeatFalling = 2;
    const int FeatCount = 3;
    static readonly string[] featureIds = new string[] { FoliageFXConfig.ShadowFixId, FoliageFXConfig.WindId, FoliageFXConfig.FallingId };
    static readonly string[] featureNames = new string[] { "shadow fix", "wind", "falling leaves" };

    static readonly string Dot = "  " + ((char)0x00B7) + "  ";

    readonly OverlayLog log;
    readonly MVRScript script;
    readonly Stopwatch watch = new Stopwatch();
    readonly Stopwatch featWatch = new Stopwatch();
    readonly float[] featFrame = new float[FeatCount];
    readonly float[] featAvg = new float[FeatCount];
    readonly float[] featPeak = new float[FeatCount];     // worst frame within the peak window
    readonly float[] featPeakAt = new float[FeatCount];

    public TreeRegistry Trees;
    public ShadowFix ShadowFix;
    public Wind Wind;
    public FallingLeaves Falling;
    public LeafShader Shader;
    public TreeLooks Looks;

    bool attached;
    bool master = true;
    bool held;
    bool onTree;               // the containing atom is a CustomUnityAsset
    float nextScan;
    bool msaa;
    bool touchAll;
    bool regatherAll;
    bool refreshLooks;
    bool scanAll;
    UnityEngine.Shader appliedShader;
    Tree current;              // the tree the guarded steps work on
    int scans;
    int gathers;

    // The effects' values as last applied.
    bool fixOn, softOn, otherOn, allOn, windOn, fallOn, fallShadows;
    float windStrength, windSpeed, windDirection, fallRate, fallSize, fallSpeed, fallRest;

    // Frame timing for the Overview's caption and Diagnose.
    float msAverage;
    float msWorst;             // rolling peak: the highest frame within TimingPeakSeconds
    float msWorstAt;
    float sharedAvg;           // everything but the effects: the scan, the gathers, the checks
    float budgetMs = 16.7f;    // the frame the game runs at, smoothed (includes our own cost)
    float targetMs;            // the frame VaM aims for; 0 = no target, use the measured frame
    int samples;

    // One delegate per guarded step, built once here: written at the call
    // site, each would be a fresh allocation every time a tree is applied.
    // Each reads its fields when it runs, so an effect wired after
    // construction is still found.
    readonly OverlayLog.Fn fnFrame, fnScan, fnGather, fnShadowFix, fnWind, fnFalling;
    readonly TreeRegistry.ReleaseFn releaseEffects;

    public Lifecycle(OverlayLog log, MVRScript script)
    {
        this.log = log;
        this.script = script;
        Trees = new TreeRegistry(log);
        Looks = new TreeLooks();
        Trees.Looks = Looks;
        Shader = new LeafShader(script, log);
        releaseEffects = ReleaseEffects;
        Trees.ReleaseEffects = releaseEffects;
        fnFrame = Frame;
        fnScan = delegate { Trees.Scan(scanAll); };
        fnGather = delegate { current.Gather(Looks, LeafShaderNow(), ShadowFix != null && ShadowFix.OtherShaders.val); };
        fnShadowFix = delegate { if (ShadowFix != null) ShadowFix.ApplyTree(current, LeafShaderNow(), msaa); };
        fnWind = delegate
        {
            if (Wind == null) return;
            List<LeafMaterial> mats = current.Materials;
            for (int i = 0; i < mats.Count; i++) Wind.Apply(mats[i]);
        };
        fnFalling = delegate
        {
            if (Falling != null) Falling.ApplyTree(current, this.script, LeafShaderNow(), Wind, msaa, ShadowFix != null && ShadowFix.SoftEdges.val);
        };
    }

    public bool Master { get { return master; } }

    // The registry's release hook: the falling leaves go first (their
    // emitter draws with a material of their own), then the copy, each in
    // its own boundary so one failure cannot keep the other from giving back.
    void ReleaseEffects(LeafMaterial m)
    {
        if (Falling != null) log.RunWarn("could not remove the falling leaves of " + m.Name, delegate { Falling.Release(m); });
        if (ShadowFix != null) log.RunWarn("could not put back the original of " + m.Name, delegate { ShadowFix.Release(m); });
    }

    UnityEngine.Shader LeafShaderNow()
    {
        return Shader != null ? Shader.Shader : null;
    }

    // The facade wires the three effects before this: the scope default
    // needs ShadowFix, and the falling leaves' extraction runs inside the log.
    // All Atoms is on, except for an instance sitting on a CustomUnityAsset,
    // which looks after its own tree; a scene restore after this moves the
    // value, and the default stays where a Reset puts it.
    public void Attach(Atom containing)
    {
        attached = true;
        Trees.Self = containing;
        onTree = containing != null && containing.type == FoliageFXConfig.TreeAtomType;
        nextScan = 0f;
        if (Falling != null) Falling.Log = log;
        if (ShadowFix != null && ShadowFix.AllAtoms != null)
        {
            ShadowFix.AllAtoms.defaultVal = !onTree;
            ShadowFix.AllAtoms.val = !onTree;
        }
    }

    // Master off releases everything the effects made, so off is the same as
    // no plugin; on, every tree is applied again.
    public void SetMaster(bool on)
    {
        if (master == on) return;
        master = on;
        if (!on) ReleaseOwned("Enabled off");
        else
        {
            touchAll = true;
            nextScan = 0f;
        }
    }

    public void Update()
    {
        if (held || !attached || Trees == null) return;
        watch.Reset();
        watch.Start();
        log.RunOnce("scene update", fnFrame);
        watch.Stop();
        RecordTiming((float)watch.Elapsed.TotalMilliseconds);
    }

    void Frame()
    {
        PollSwitches();
        PollShader();
        float now = Time.unscaledTime;
        if (now >= nextScan)
        {
            nextScan = now + FoliageFXConfig.SceneScanSeconds;
            scans++;
            scanAll = ShadowFix == null || ShadowFix.AllAtoms.val;
            log.RunOnce("scan the scene for CustomUnityAsset atoms", fnScan);
            // MSAA can be switched in VaM's settings at any time; soft
            // edges follow it.
            bool aa = QualitySettings.antiAliasing > 1;
            if (aa != msaa)
            {
                msaa = aa;
                touchAll = true;
            }
            if (Falling != null) Falling.Tick();
        }
        List<Tree> trees = Trees.Trees;
        if (regatherAll)
        {
            regatherAll = false;
            for (int i = 0; i < trees.Count; i++)
            {
                trees[i].NeedsGather = true;
                trees[i].Dirty = true;
            }
        }
        bool every = touchAll;
        bool looksAgain = refreshLooks;
        touchAll = false;
        refreshLooks = false;
        for (int i = 0; i < trees.Count; i++)
        {
            Tree t = trees[i];
            if (!t.Dirty && !every) continue;
            ApplyTree(t, looksAgain);
        }
    }

    // A yielded tree was released when it yielded and stays untouched. A tree
    // is read before the effects run on it whenever its renderers are stale;
    // with the master off it is only read.
    void ApplyTree(Tree t, bool looksAgain)
    {
        t.Dirty = false;
        if (t.Yielded) return;
        current = t;
        if (t.NeedsGather)
        {
            gathers++;
            log.RunOnce("read the materials of a CustomUnityAsset", fnGather);
        }
        else if (looksAgain) RefreshLooks(t);
        if (master)
        {
            Guard(FeatShadowFix, fnShadowFix);
            Guard(FeatWind, fnWind);
            Guard(FeatFalling, fnFalling);
        }
        current = null;
    }

    // TreeLooks.Read replaces every look, so after a restore each material
    // fetches its own again; one nobody saved is made fresh from its source.
    void RefreshLooks(Tree t)
    {
        if (Looks == null) return;
        for (int i = 0; i < t.Materials.Count; i++)
        {
            LeafMaterial m = t.Materials[i];
            if (!m.Foreign && m.Source != null) m.Look = Looks.Get(t.Key, m.Name, m.Source);
        }
    }

    void Guard(int feature, OverlayLog.Fn fn)
    {
        featWatch.Reset();
        featWatch.Start();
        log.RunOnce(featureNames[feature], "apply", fn);
        featWatch.Stop();
        featFrame[feature] += (float)featWatch.Elapsed.TotalMilliseconds;
    }

    void PollSwitches()
    {
        if (ShadowFix != null && ShadowFix.Enabled != null)
        {
            if (Moved(ref fixOn, ShadowFix.Enabled.val) | Moved(ref softOn, ShadowFix.SoftEdges.val)) touchAll = true;
            if (Moved(ref otherOn, ShadowFix.OtherShaders.val)) regatherAll = true;
            if (Moved(ref allOn, ShadowFix.AllAtoms.val)) nextScan = 0f;
        }
        if (Wind != null && Wind.Enabled != null)
        {
            if (Moved(ref windOn, Wind.Enabled.val) | Moved(ref windStrength, Wind.Strength.val)
                | Moved(ref windSpeed, Wind.Speed.val) | Moved(ref windDirection, Wind.Direction.val)) touchAll = true;
        }
        if (Falling != null && Falling.Enabled != null)
        {
            if (Moved(ref fallOn, Falling.Enabled.val) | Moved(ref fallShadows, Falling.CastShadows.val)
                | Moved(ref fallRate, Falling.Rate.val) | Moved(ref fallSize, Falling.SizeMm.val)
                | Moved(ref fallSpeed, Falling.FallSpeed.val) | Moved(ref fallRest, Falling.RestSeconds.val)) touchAll = true;
        }
    }

    // The leaf shader decides what counts as another instance's copy as well
    // as what can be converted, so its arrival has every tree read again.
    void PollShader()
    {
        UnityEngine.Shader s = LeafShaderNow();
        if ((object)s != (object)appliedShader)
        {
            appliedShader = s;
            regatherAll = true;
        }
        if (ShadowFix != null) ShadowFix.ShaderReady = s != null;
    }

    static bool Moved(ref bool last, bool now)
    {
        if (last == now) return false;
        last = now;
        return true;
    }

    static bool Moved(ref float last, float now)
    {
        if (last == now) return false;
        last = now;
        return true;
    }

    // An edit in the tree editor: that tree is applied on the next Update.
    public void Touch(Tree tree)
    {
        if (tree != null) tree.Dirty = true;
    }

    // Every tree applied again, each material fetching its look anew: a scene
    // restore, an effect's Reset to Defaults.
    public void TouchAll()
    {
        touchAll = true;
        refreshLooks = true;
    }

    public void Hold()
    {
        if (held) return;
        held = true;
        ReleaseOwned("plugin disabled");
    }

    public void Resume()
    {
        if (!held) return;
        held = false;
        nextScan = 0f;
        touchAll = true;
    }

    // Everything the effects made goes back; the trees stay tracked and
    // hooked, and are read again before anything is applied to them.
    void ReleaseOwned(string reason)
    {
        if (Trees != null) log.RunWarn("could not put back the original materials", Trees.ReleaseAll);
        if (Falling != null) log.RunWarn("could not remove the falling leaves", Falling.ReleaseAll);
        log.Info("Original materials back on every tree (" + reason + ").");
    }

    // The plugin going away: released, and every loader hook removed.
    public void Release(string reason)
    {
        ReleaseOwned(reason);
        if (Trees != null) log.RunWarn("could not stop following the CustomUnityAsset atoms", Trees.Forget);
    }

    // The Rescan button: everything released, the scene scanned now, every
    // tree read and applied again on the next Update, leaf shapes found anew.
    public void Rescan()
    {
        ReleaseOwned("rescan");
        if (!attached) return;
        scanAll = ShadowFix == null || ShadowFix.AllAtoms.val;
        Trees.Scan(scanAll);
        scans++;
        nextScan = Time.unscaledTime + FoliageFXConfig.SceneScanSeconds;
        Trees.MarkAllDirty();
        touchAll = true;
    }

    // ---- UI text ----

    public string StatusLine()
    {
        if (Shader != null && Shader.Failed) return Shader.Error;
        int trees = Trees.TreeCount;
        if (trees == 0) return "No CustomUnityAsset atoms in the scene";
        if (!master) return "Off" + Dot + "original materials on every tree";
        if (LeafShaderNow() == null) return "Loading the leaf shader";
        int converted = Trees.ConvertedCount;
        return trees + (trees == 1 ? " tree" : " trees") + Dot + converted + (converted == 1 ? " leaf material converted" : " leaf materials converted");
    }

    void RecordTiming(float ms)
    {
        if (samples < FoliageFXConfig.TimingWindow) samples++;
        msAverage += (ms - msAverage) / samples;
        // A peak that never decays would only ever report the first scene
        // load; this one forgets a spike once it is older than the window.
        float now = Time.unscaledTime;
        if (ms >= msWorst || now - msWorstAt > FoliageFXConfig.TimingPeakSeconds)
        {
            msWorst = ms;
            msWorstAt = now;
        }
        float features = 0f;
        for (int i = 0; i < FeatCount; i++)
        {
            featAvg[i] += (featFrame[i] - featAvg[i]) / samples;
            if (featFrame[i] >= featPeak[i] || now - featPeakAt[i] > FoliageFXConfig.TimingPeakSeconds)
            {
                featPeak[i] = featFrame[i];
                featPeakAt[i] = now;
            }
            features += featFrame[i];
            featFrame[i] = 0f;
        }
        float shared = ms - features;
        if (shared < 0f) shared = 0f;
        sharedAvg += (shared - sharedAvg) / samples;
        float dt = Time.unscaledDeltaTime * 1000f;
        if (dt > 1f && dt < 200f) budgetMs += (dt - budgetMs) / samples;
        // The share is against the frame the game aims for, not the one it
        // gets: the measured frame already holds this plugin's cost.
        int targetFps = Application.targetFrameRate;
        targetMs = targetFps > 0 && targetFps <= FoliageFXConfig.TargetFpsMax ? 1000f / targetFps : 0f;
    }

    float FeaturesAvg
    {
        get
        {
            float sum = 0f;
            for (int i = 0; i < FeatCount; i++) sum += featAvg[i];
            return sum;
        }
    }

    // The Overview card, two lines a user reads at a glance:
    //   0.4 ms/frame  ·  2% of 60 fps
    //   Features 0.1  ·  Shared 0.3  ·  Peak 3.2 ms
    // A frame rate measured because VaM set no target is marked with a tilde.
    public string TimingCaption()
    {
        bool known = targetMs > 0.5f;
        float frame = known ? targetMs : budgetMs;
        float fps = frame > 0.5f ? 1000f / frame : 0f;
        int share = frame > 0.5f ? Mathf.RoundToInt(msAverage / frame * 100f) : 0;
        string nl = ((char)10).ToString();
        return msAverage.ToString("F1") + " ms/frame" + Dot + share + "% of " + (known ? "" : "~") + Mathf.RoundToInt(fps) + " fps" + nl
            + "Features " + FeaturesAvg.ToString("F1") + Dot + "Shared " + sharedAvg.ToString("F1") + Dot + "Peak " + msWorst.ToString("F1") + " ms";
    }

    // "0.41 ms" for an inspector caption, empty while the effect costs nothing.
    public string CostText(string featureId)
    {
        for (int i = 0; i < FeatCount; i++)
            if (featureIds[i] == featureId)
                return featAvg[i] >= FoliageFXConfig.TimingShowMs ? featAvg[i].ToString("F2") + " ms" : "";
        return "";
    }

    // "0.42 ms per frame (3% of the 60 fps target), peak 3.1 ms"
    string TimingLine()
    {
        bool known = targetMs > 0.5f;
        float frame = known ? targetMs : budgetMs;
        float fps = frame > 0.5f ? 1000f / frame : 0f;
        int share = frame > 0.5f ? Mathf.RoundToInt(msAverage / frame * 100f) : 0;
        return msAverage.ToString("F2") + " ms per frame (" + share + "% of " + (known ? "the " : "a measured ") + Mathf.RoundToInt(fps)
            + " fps " + (known ? "target" : "frame") + "), peak " + msWorst.ToString("F1") + " ms";
    }

    // The effects that cost something this window, with a peak well above
    // the average (a tree applied, an emitter made) named apart.
    string FeatureTimingLine()
    {
        StringBuilder sb = new StringBuilder(96);
        for (int i = 0; i < FeatCount; i++)
        {
            if (featAvg[i] < FoliageFXConfig.TimingShowMs && featPeak[i] < FoliageFXConfig.TimingShowMs) continue;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(featureNames[i]).Append(' ').Append(featAvg[i].ToString("F2"));
            if (featPeak[i] > featAvg[i] * 3f && featPeak[i] > 0.5f) sb.Append(" (peak ").Append(featPeak[i].ToString("F1")).Append(')');
        }
        if (sb.Length > 0) sb.Append(" ms");
        return sb.ToString();
    }

    public void Describe(StringBuilder sb)
    {
        sb.Append(" | trees: ").Append(Trees.TreeCount);
        for (int i = 0; i < Trees.Trees.Count; i++)
        {
            sb.Append(i == 0 ? " (" : "; ");
            Trees.Trees[i].Describe(sb);
        }
        if (Trees.Trees.Count > 0) sb.Append(')');
        int copies = 0, leafMaterials = 0;
        for (int i = 0; i < Trees.Trees.Count; i++)
            for (int k = 0; k < Trees.Trees[i].Materials.Count; k++)
            {
                LeafMaterial m = Trees.Trees[i].Materials[k];
                if (m.Copy != null) copies++;
                if (m.FallingMaterial != null) leafMaterials++;
            }
        sb.Append(", copies ").Append(copies).Append(", leaf materials ").Append(leafMaterials);
        if (ShadowFix != null) ShadowFix.Describe(sb);
        if (Wind != null) Wind.Describe(sb);
        if (Falling != null) Falling.Describe(sb);
        sb.Append(" | shader: ").Append(Shader != null ? Shader.State() : "none");
        sb.Append(" | on ").Append(Trees.Self != null ? Trees.Self.uid + (onTree ? " (a CustomUnityAsset)" : "") : "no atom (session plugin)");
        if (Looks != null) sb.Append(", customised looks ").Append(Looks.CustomizedCount);
        sb.Append(" | master ").Append(master ? "on" : "off");
        if (held) sb.Append(" | held");
        sb.Append(" | scans ").Append(scans).Append(", gathers ").Append(gathers).Append(", MSAA ").Append(msaa ? "on" : "off");
        sb.Append(" | cpu ").Append(TimingLine()).Append(": features ").Append(FeaturesAvg.ToString("F2"))
          .Append(", shared ").Append(sharedAvg.ToString("F2")).Append(" ms");
        string costs = FeatureTimingLine();
        if (costs.Length > 0) sb.Append(" | per effect: ").Append(costs);
    }
}
