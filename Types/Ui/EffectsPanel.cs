using System.Collections.Generic;
using UnityEngine;

// Effects: what applies to every tree. Rows: Shadow Fix, Wind, Falling
// Leaves. Each inspector is a header (title, Enabled, live caption) and its
// cards; Shadow Fix adds the Scope card, shown only when the plugin sits on a
// CustomUnityAsset, and an Advanced card. Reset to Defaults puts an effect's
// Look values back and has every tree applied again; Scene values (All
// Atoms) stay, being about the scene rather than the look.
sealed class EffectsPanel
{
    FeatureView shadowFix;
    FeatureView wind;
    FeatureView falling;

    public void AddFeatures(UiShell shell, ShadowFix fix, Wind w, FallingLeaves fall)
    {
        shadowFix = shell.AddFeature(FoliageFXConfig.PageEffects, ShadowFix.Id, "Shadow Fix", fix.Enabled);
        wind = shell.AddFeature(FoliageFXConfig.PageEffects, Wind.Id, "Wind", w.Enabled);
        falling = shell.AddFeature(FoliageFXConfig.PageEffects, FallingLeaves.Id, "Falling Leaves", fall.Enabled);
        // The sentence under Status on each inspector: what the effect is for.
        shadowFix.Guide = "Alpha-blended leaves never receive shadows in VaM. Converted to cutout leaves, they cast and receive shadows like the rest of the scene.";
        wind.Guide = "A gentle sway on every converted leaf, and the direction falling leaves drift.";
        falling.Guide = "Leaves cut from each tree's own texture drop from the canopy, drift with the wind and lie on the ground for a while.";
    }

    public void BuildInspectors(UiKit kit, UiShell shell, Lifecycle life, ParamSet set, ShadowFix fix, Wind w, FallingLeaves fall)
    {
        // ---- Shadow Fix ----
        shell.BeginInspector(shadowFix, "Shadow Fix");
        kit.BeginSection("Look", true, shadowFix.InspectorBag);
        kit.Toggle(fix.SoftEdges, true, shadowFix.InspectorBag, "Smooth leaf outlines when MSAA is on in VaM's settings; without MSAA the edges stay hard.");
        List<string> fixStory = new List<string>();
        fixStory.Add("Fog and depth of field see converted leaves too.");
        fixStory.Add("Nothing in the asset is changed: switch the fix off and the original materials are back.");
        kit.HintBox(fixStory, true, shadowFix.InspectorBag);
        // Only the card rides on the condition; the toggle and its hint ride in
        // the inspector's bag, inside the card. A condition re-shows everything
        // in its bag on each pass, which would bring the hint back with Show
        // Hints off; hidden with the card, they are hidden all the same.
        List<GameObject> scope = shadowFix.When(delegate { return OnTree(life); });
        kit.BeginSection("Scope", true, scope);
        kit.Toggle(fix.AllAtoms, true, shadowFix.InspectorBag, "Fixes every CustomUnityAsset in the scene, not only this one.");
        shell.BeginAdvanced(shadowFix);
        kit.Toggle(fix.OtherShaders, true, shadowFix.AdvancedBag, "Also converts transparent materials on shaders Auto does not know. Check the Trees page for anything it should not have touched.");
        shadowFix.OnState = delegate { return fix.State(life.Trees); };
        shadowFix.OnReset = delegate { set.Reset(ParamSet.Look, ShadowFix.Id); life.TouchAll(); };
        shell.EndInspector(shadowFix);

        // ---- Wind ----
        shell.BeginInspector(wind, "Wind");
        kit.BeginSection("Wind", true, wind.InspectorBag);
        kit.Slider(w.Strength, true, wind.InspectorBag, "How far the leaves move.");
        kit.Slider(w.Speed, true, wind.InspectorBag, "How fast they flutter.");
        kit.Slider(w.Direction, true, wind.InspectorBag, "Which way the wind blows, in degrees around the scene.");
        wind.OnState = w.State;
        wind.OnReset = delegate { set.Reset(ParamSet.Look, Wind.Id); life.TouchAll(); };
        shell.EndInspector(wind);

        // ---- Falling Leaves ----
        shell.BeginInspector(falling, "Falling Leaves");
        kit.BeginSection("Leaves", true, falling.InspectorBag);
        kit.Slider(fall.Rate, true, falling.InspectorBag, "How many leaves fall each second from each tree.");
        // The size allocates nothing and scales linearly, so its slider is open.
        kit.OpenSlider(fall.SizeMm, true, falling.InspectorBag, "How big each falling leaf is.");
        kit.Slider(fall.FallSpeed, true, falling.InspectorBag, "How fast they drop; wind and flutter are on top.");
        kit.Slider(fall.RestSeconds, true, falling.InspectorBag, "How long a leaf lies where it lands before it fades.");
        kit.Toggle(fall.CastShadows, true, falling.InspectorBag);
        List<string> fallStory = new List<string>();
        fallStory.Add("A texture without separate leaves drops rounded pieces of it instead.");
        fallStory.Add("Each tree's materials choose with Drop Falling Leaves on the Trees page.");
        kit.HintBox(fallStory, true, falling.InspectorBag);
        falling.OnState = fall.State;
        falling.OnReset = delegate { set.Reset(ParamSet.Look, FallingLeaves.Id); life.TouchAll(); };
        shell.EndInspector(falling);
    }

    // The plugin sits on a CustomUnityAsset: only then is there a choice
    // between its own tree and every tree.
    static bool OnTree(Lifecycle life)
    {
        Atom self = life.Trees != null ? life.Trees.Self : null;
        return self != null && self.type == FoliageFXConfig.TreeAtomType;
    }
}
