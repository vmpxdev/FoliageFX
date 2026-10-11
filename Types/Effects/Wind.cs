using System.Text;
using UnityEngine;

// A sway on every converted leaf, and the direction the falling leaves
// drift. The motion is the leaf shader's own (a flutter from each leaf's
// position plus a slow sway along the direction), so this effect owns
// nothing: it writes its values onto ShadowFix's leaf copies, and the
// falling leaves take them from Lifecycle as arguments.
sealed class Wind
{
    public const string Id = FoliageFXConfig.WindId;

    static readonly int StrengthId = Shader.PropertyToID("_WindStrength");
    static readonly int SpeedId = Shader.PropertyToID("_WindSpeed");
    static readonly int DirectionId = Shader.PropertyToID("_WindDirection");

    public JSONStorableBool Enabled;
    public JSONStorableFloat Strength;
    public JSONStorableFloat Speed;
    public JSONStorableFloat Direction;

    public void Create(ParamSet set)
    {
        Enabled = set.Bool(Id, ParamSet.Look, "enabled", new JSONStorableBool("Wind", false));
        Strength = set.Float(Id, ParamSet.Look, "strength", new JSONStorableFloat("Wind Strength", 0.3f, 0f, 1f, true));
        Speed = set.Float(Id, ParamSet.Look, "speed", new JSONStorableFloat("Wind Speed", 1f, 0.05f, 4f, true));
        Direction = set.Float(Id, ParamSet.Look, "direction_deg", new JSONStorableFloat("Wind Direction deg", 0f, 0f, 360f, true));
    }

    // The way the leaves are pushed, on the ground plane: 0 degrees is the
    // scene's +Z, 90 its +X. The falling leaves drift along the same angle.
    public Vector4 DirectionVector
    {
        get
        {
            float radians = Direction.val * Mathf.Deg2Rad;
            return new Vector4(Mathf.Sin(radians), 0f, Mathf.Cos(radians), 0f);
        }
    }

    // Off writes a strength of 0, which the shader takes as no sway at all.
    public void Apply(LeafMaterial m)
    {
        if (m == null || m.Kind != LeafMaterial.KindLeaf || m.Copy == null) return;
        m.Copy.SetFloat(StrengthId, Enabled.val ? Strength.val : 0f);
        m.Copy.SetFloat(SpeedId, Speed.val);
        m.Copy.SetVector(DirectionId, DirectionVector);
    }

    // ---- UI text ----

    public string State()
    {
        if (!Enabled.val) return "off";
        return Strength.val.ToString("0.##") + " from " + Mathf.RoundToInt(Direction.val) + "\u00B0";
    }

    public void Describe(StringBuilder sb)
    {
        sb.Append(" | wind: ").Append(Enabled.val ? "on" : "off")
          .Append(", strength ").Append(Strength.val.ToString("0.##"))
          .Append(", speed ").Append(Speed.val.ToString("0.##"))
          .Append(", direction ").Append(Mathf.RoundToInt(Direction.val)).Append(" deg");
    }
}
