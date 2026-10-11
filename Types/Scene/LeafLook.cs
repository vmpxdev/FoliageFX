using System.Globalization;
using SimpleJSON;
using UnityEngine;

// One material's settings: whether it is converted (Mode), its colour, how
// its leaves are cut, and whether they fall. Every material a tree shows has
// a look, made with Reset's defaults, and a look is saved only once the user
// changed it (Customized), so the scene holds the looks someone made rather
// than one per material in the scene.
//
// The node's keys are the schema: snake case, never renamed. Floats are
// written in the invariant culture and read invariant first, then in the
// machine's own, and a value outside the editor's range comes back at the
// nearest end, the way ParamEntry reads a setting.
sealed class LeafLook
{
    // The editor's ranges, the same the leaf shader declares for the values
    // it is handed.
    const float HueMax = 180f;
    const float ColourMax = 2f;          // saturation, brightness and translucency
    const float CutoffMin = 0.05f;
    const float CutoffMax = 0.95f;
    // A source without _Glossiness starts here: a leaf with a faint sheen.
    const float DefaultSmoothness = 0.2f;

    static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");

    public string Mode = FoliageFXConfig.ModeAuto;
    public HSVColor Tint;
    public float Hue, Saturation, Brightness, Cutoff, Smoothness, Translucency;
    public bool DoubleSided, Falling;
    public bool Customized;

    public LeafLook()
    {
        Reset(null);
    }

    // Smoothness starts at the source's own, so a converted leaf keeps the
    // sheen the asset gave it.
    public void Reset(Material source)
    {
        Mode = FoliageFXConfig.ModeAuto;
        Tint = White();
        Hue = 0f;
        Saturation = 1f;
        Brightness = 1f;
        Cutoff = 0.5f;
        Smoothness = source != null && source.HasProperty(GlossinessId) ? Mathf.Clamp01(source.GetFloat(GlossinessId)) : DefaultSmoothness;
        Translucency = 0f;
        DoubleSided = true;
        Falling = true;
        Customized = false;
    }

    // A copied look is the user's choice for the target, so it saves.
    public void CopyFrom(LeafLook other, bool includeMode)
    {
        if (other == null) return;
        if (includeMode) Mode = other.Mode;
        Tint = other.Tint;
        Hue = other.Hue;
        Saturation = other.Saturation;
        Brightness = other.Brightness;
        Cutoff = other.Cutoff;
        Smoothness = other.Smoothness;
        Translucency = other.Translucency;
        DoubleSided = other.DoubleSided;
        Falling = other.Falling;
        Customized = true;
    }

    public bool Tinted
    {
        get { return Tint.S > 0.001f || Tint.V < 0.999f; }
    }

    public JSONClass Write()
    {
        JSONClass n = new JSONClass();
        n.Add("mode", new JSONData(Mode ?? FoliageFXConfig.ModeAuto));
        n.Add("tint", new JSONData(ParamEntry.Num(Tint.H) + "," + ParamEntry.Num(Tint.S) + "," + ParamEntry.Num(Tint.V)));
        n.Add("hue", new JSONData(ParamEntry.Num(Hue)));
        n.Add("saturation", new JSONData(ParamEntry.Num(Saturation)));
        n.Add("brightness", new JSONData(ParamEntry.Num(Brightness)));
        n.Add("cutoff", new JSONData(ParamEntry.Num(Cutoff)));
        n.Add("smoothness", new JSONData(ParamEntry.Num(Smoothness)));
        n.Add("translucency", new JSONData(ParamEntry.Num(Translucency)));
        n.Add("double_sided", new JSONData(DoubleSided));
        n.Add("falling", new JSONData(Falling));
        return n;
    }

    // A look read from a scene was saved because someone made it, so it is
    // customized. A key the node lacks keeps the default.
    public static LeafLook Read(JSONClass node)
    {
        LeafLook look = new LeafLook();
        look.Customized = true;
        if (node == null) return look;
        string mode = Text(node, "mode");
        if (mode == FoliageFXConfig.ModeAuto || mode == FoliageFXConfig.ModeConvert || mode == FoliageFXConfig.ModeKeep) look.Mode = mode;
        string tint = Text(node, "tint");
        if (tint != null)
        {
            string[] parts = tint.Split(',');
            float h, s, v;
            if (parts.Length == 3 && ParseNum(parts[0], out h) && ParseNum(parts[1], out s) && ParseNum(parts[2], out v))
            {
                HSVColor c = new HSVColor();
                c.H = Mathf.Clamp01(h); c.S = Mathf.Clamp01(s); c.V = Mathf.Clamp01(v);
                look.Tint = c;
            }
        }
        look.Hue = Number(node, "hue", look.Hue, -HueMax, HueMax);
        look.Saturation = Number(node, "saturation", look.Saturation, 0f, ColourMax);
        look.Brightness = Number(node, "brightness", look.Brightness, 0f, ColourMax);
        look.Cutoff = Number(node, "cutoff", look.Cutoff, CutoffMin, CutoffMax);
        look.Smoothness = Number(node, "smoothness", look.Smoothness, 0f, 1f);
        look.Translucency = Number(node, "translucency", look.Translucency, 0f, ColourMax);
        look.DoubleSided = Flag(node, "double_sided", look.DoubleSided);
        look.Falling = Flag(node, "falling", look.Falling);
        return look;
    }

    static HSVColor White()
    {
        HSVColor c = new HSVColor();
        c.H = 0f; c.S = 0f; c.V = 1f;
        return c;
    }

    static string Text(JSONClass node, string key)
    {
        if (!node.HasKey(key)) return null;
        JSONNode n = node[key];
        return n != null ? n.Value : null;
    }

    static float Number(JSONClass node, string key, float fallback, float min, float max)
    {
        float v;
        if (!ParseNum(Text(node, key), out v) || float.IsNaN(v) || float.IsInfinity(v)) return fallback;
        return Mathf.Clamp(v, min, max);
    }

    static bool Flag(JSONClass node, string key, bool fallback)
    {
        string text = Text(node, key);
        bool b;
        return text != null && bool.TryParse(text.Trim(), out b) ? b : fallback;
    }

    static bool ParseNum(string text, out float v)
    {
        v = 0f;
        if (string.IsNullOrEmpty(text)) return false;
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return true;
        return float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out v);
    }
}
