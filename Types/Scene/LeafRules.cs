using System;
using UnityEngine;

// Which materials of a tree are leaves that never receive shadows, and the
// sentence that says why, for the tree editor. Unity's built-in pipeline
// shadows nothing in the Transparent queue, so the verdict starts from the
// queue: below it a material already takes shadows, from the overlays up it
// is never foliage, and in between only an alpha-blended material with a
// texture to cut leaves from qualifies. A shader is converted on its name
// alone only when it is known to be plain alpha blending (Standard in Fade or
// Transparent mode, the legacy Transparent family, Unity's Nature shaders);
// any other transparent shader may be glass, water or a hand-written effect,
// and is converted only with Convert Other Shaders on.
sealed class LeafRules
{
    static readonly int MainTexId = Shader.PropertyToID(FoliageFXConfig.LeafTextureProperty);
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int ModeId = Shader.PropertyToID("_Mode");

    // The Standard shader's _Mode for Transparent; Fade is StandardFadeMode.
    const float StandardTransparentMode = 3f;

    public static void Judge(Material m, Shader leafShader, bool otherShaders, out bool candidate, out string reason)
    {
        candidate = false;
        if (m == null)
        {
            reason = "No material in this slot.";
            return;
        }
        // Checked first: a copy another FoliageFX made sits in the AlphaTest
        // queue and would otherwise read as a cutout the asset shipped with.
        if (IsOurs(m, leafShader))
        {
            reason = "Already converted by another FoliageFX.";
            return;
        }
        string shader = m.shader != null ? m.shader.name : "";
        int queue = m.renderQueue;
        if (queue < FoliageFXConfig.TransparentQueueFirst)
        {
            reason = "Opaque or cutout: already receives shadows.";
            return;
        }
        if (queue >= FoliageFXConfig.OverlayQueueFirst)
        {
            reason = "Overlay: not foliage.";
            return;
        }
        if (!m.HasProperty(MainTexId) || m.GetTexture(MainTexId) == null)
        {
            reason = "Transparent, but it has no texture to cut leaves from.";
            return;
        }
        if (m.HasProperty(ColorId))
        {
            float alpha = m.GetColor(ColorId).a;
            if (alpha < FoliageFXConfig.SeeThroughAlpha)
            {
                reason = "Transparent with colour alpha " + alpha.ToString("0.00") + ": looks like glass or water.";
                return;
            }
        }
        if (shader.StartsWith("Standard", StringComparison.Ordinal))
        {
            float mode = m.HasProperty(ModeId) ? m.GetFloat(ModeId) : 0f;
            if (mode >= FoliageFXConfig.StandardFadeMode)
            {
                candidate = true;
                reason = "Standard shader in " + (mode >= StandardTransparentMode ? "Transparent" : "Fade") + " mode: never receives shadows.";
            }
            else reason = "Standard shader in Opaque or Cutout mode with a transparent queue. Set Mode to Convert if it is foliage.";
            return;
        }
        if (shader.IndexOf("Transparent/", StringComparison.Ordinal) >= 0 || shader.StartsWith("Nature/", StringComparison.Ordinal))
        {
            candidate = true;
            reason = "Alpha-blended shader: never receives shadows.";
            return;
        }
        if (shader.StartsWith("Particles/", StringComparison.Ordinal) || shader.IndexOf("/Particles/", StringComparison.Ordinal) >= 0
            || shader.StartsWith("Sprites/", StringComparison.Ordinal) || shader.StartsWith("UI/", StringComparison.Ordinal))
        {
            reason = "Particle, sprite or UI shader: left alone.";
            return;
        }
        if (otherShaders)
        {
            candidate = true;
            reason = "A transparent shader Auto does not know, converted because Convert Other Shaders is on.";
            return;
        }
        reason = "A transparent shader Auto does not know. Set Mode to Convert if it is foliage.";
    }

    // A material some FoliageFX made: every copy's name ends with the suffix,
    // and a copy renamed by something else is still on the leaf shader. The
    // name test covers an instance whose bundle is another copy of the shader.
    public static bool IsOurs(Material m, Shader leafShader)
    {
        if (m == null) return false;
        string name = m.name;
        if (name != null && name.EndsWith(FoliageFXConfig.CopySuffix, StringComparison.Ordinal)) return true;
        Shader s = m.shader;
        if (s == null) return false;
        if (leafShader != null && s == leafShader) return true;
        return s.name == FoliageFXConfig.LeafShaderName;
    }
}
