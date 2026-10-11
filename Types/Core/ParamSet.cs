using System;
using System.Collections.Generic;
using System.Globalization;
using SimpleJSON;

// One registered JSONStorable with its owning feature, persistence scope, and
// stable key. Exactly one of F / B / S / C / K is set.
//
// The key ("falling.size_mm") is the schema: it is what scenes store, and it
// never changes once shipped. The storable name ("Falling Leaves Size mm") is
// what VaM, Timeline, and triggers address and may be renamed; the label the
// panel shows is a third thing again (UiKit.Pretty).
sealed class ParamEntry
{
    public string Feature;
    public string Key;          // "<feature>.<param>", lowercase snake case
    public int Scope;
    public JSONStorableFloat F;
    public JSONStorableBool B;
    public JSONStorableString S;
    public JSONStorableStringChooser C;
    public JSONStorableColor K;
    // Chooser values that were renamed: a scene holding the old value loads
    // as the new one instead of falling back to the default.
    public Dictionary<string, string> Aliases;
    // A chooser whose choices are filled at runtime (the scene's atoms): a
    // saved value is taken as it is, since the list is still empty when a
    // scene restores.
    public bool OpenChoices;

    public string Name
    {
        get
        {
            if (F != null) return F.name;
            if (B != null) return B.name;
            if (S != null) return S.name;
            if (C != null) return C.name;
            return K != null ? K.name : "";
        }
    }

    public void Write(JSONClass into)
    {
        if (into.HasKey(Key)) into.Remove(Key);
        // Floats through Num, not SimpleJSON's own float text, which follows
        // the machine's culture.
        if (F != null) into.Add(Key, new JSONData(Num(F.val)));
        else if (B != null) into.Add(Key, new JSONData(B.val));
        else if (S != null) into.Add(Key, new JSONData(S.val ?? ""));
        else if (C != null) into.Add(Key, new JSONData(C.val ?? ""));
        else if (K != null)
        {
            // Colours as "h,s,v" so files stay plain text. Invariant culture,
            // always: a comma-decimal locale would otherwise write "0,5" and
            // turn one colour into six fields no build could read back.
            HSVColor c = K.val;
            into.Add(Key, new JSONData(Num(c.H) + "," + Num(c.S) + "," + Num(c.V)));
        }
    }

    // The stored node for this entry: by key, else by its storable name, the
    // way VaM itself stores a value, so a node written without the block
    // still reads. Null when neither is present.
    JSONNode Find(JSONClass from)
    {
        if (from == null) return null;
        if (from.HasKey(Key)) return from[Key];
        string name = Name;
        if (!string.IsNullOrEmpty(name) && from.HasKey(name)) return from[name];
        return null;
    }

    public bool Present(JSONClass from)
    {
        return Find(from) != null;
    }

    // A float for a file: invariant culture, so a file written on a
    // comma-decimal machine reads the same everywhere.
    public static string Num(float v)
    {
        return v.ToString("R", CultureInfo.InvariantCulture);
    }

    // A float out of a file: invariant first (what this build writes), then the
    // machine's own culture, so a value SimpleJSON formatted on a comma-decimal
    // machine still reads. False when it is neither - the caller then falls
    // back to the default and counts it.
    static bool ParseNum(string text, out float v)
    {
        v = 0f;
        if (string.IsNullOrEmpty(text)) return false;
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return true;
        return float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out v);
    }

    // What Read did with the stored value. Only an unreadable one counts as
    // a failure; the others say why the value now differs from the file's.
    public const int ReadAbsent = 0;        // the file does not carry it
    public const int ReadOk = 1;
    public const int ReadUnreadable = 2;    // carried but not readable: the default instead
    public const int ReadClamped = 3;       // outside the slider range: the nearest end
    public const int ReadNotOffered = 4;    // a choice not offered: the default instead
    public const int ReadAliased = 5;       // a renamed choice: loaded as the new one

    // Applies the stored value through the normal setter so callbacks fire.
    // Values are validated on the way in: a float is clamped to its slider
    // range (and a non-number becomes the default), a chooser value outside
    // its choices becomes the default, a bool that is neither true nor false
    // becomes the default. Returns the outcome (ReadAbsent when nothing was
    // stored).
    public int Read(JSONClass from)
    {
        JSONNode n = Find(from);
        if (n == null) return ReadAbsent;
        int outcome = ReadOk;
        if (F != null)
        {
            float v;
            if (!ParseNum(n.Value, out v) || float.IsNaN(v) || float.IsInfinity(v)) { v = F.defaultVal; outcome = ReadUnreadable; }
            if (F.constrained)
            {
                if (v < F.min) { v = F.min; if (outcome == ReadOk) outcome = ReadClamped; }
                if (v > F.max) { v = F.max; if (outcome == ReadOk) outcome = ReadClamped; }
            }
            F.val = v;
        }
        else if (B != null)
        {
            bool b;
            if (bool.TryParse((n.Value ?? "").Trim(), out b)) B.val = b;
            else { B.val = B.defaultVal; outcome = ReadUnreadable; }
        }
        else if (S != null) S.val = n.Value ?? "";
        else if (C != null)
        {
            string v = n.Value ?? "";
            string renamed;
            if (Aliases != null && Aliases.TryGetValue(v, out renamed)) { v = renamed; outcome = ReadAliased; }
            List<string> choices = C.choices;
            bool known = OpenChoices || choices == null || choices.Count == 0 || choices.Contains(v);
            if (!known) outcome = ReadNotOffered;
            C.val = known ? v : C.defaultVal;
        }
        else if (K != null)
        {
            string[] parts = (n.Value ?? "").Split(',');
            float h = 0f, s = 0f, v = 0f;
            bool ok = parts.Length == 3 && ParseNum(parts[0], out h) && ParseNum(parts[1], out s) && ParseNum(parts[2], out v);
            if (!ok) { K.SetValToDefault(); return ReadUnreadable; }
            HSVColor c = new HSVColor();
            c.H = FoliageFXConfig.Clamp01(h); c.S = FoliageFXConfig.Clamp01(s); c.V = FoliageFXConfig.Clamp01(v);
            K.val = c;
        }
        return outcome;
    }

    // The value is the entry's default (a float within a hair of it).
    public bool IsDefault()
    {
        if (F != null) return Math.Abs(F.val - F.defaultVal) < 0.0005f;
        if (B != null) return B.val == B.defaultVal;
        if (S != null) return (S.val ?? "") == (S.defaultVal ?? "");
        if (C != null) return (C.val ?? "") == (C.defaultVal ?? "");
        if (K != null)
        {
            HSVColor c = K.val, d = K.defaultVal;
            return Math.Abs(c.H - d.H) < 0.0005f && Math.Abs(c.S - d.S) < 0.0005f && Math.Abs(c.V - d.V) < 0.0005f;
        }
        return true;
    }

    public void Reset()
    {
        if (F != null) F.SetValToDefault();
        else if (B != null) B.SetValToDefault();
        else if (S != null) S.SetValToDefault();
        else if (C != null) C.SetValToDefault();
        else if (K != null) K.SetValToDefault();
    }
}

// The plugin's parameter registry. Every feature registers its storables here
// with a feature id, a scope, and a key, so scene persistence, per-feature
// reset, Diagnose, and migrations all work from one list.
//
// Scopes (bit flags):
//   Look   every effect's values: what an effect's Reset puts back; saved
//          in the scene
//   Scene  this scene's setup (the master switch, which atoms are looked
//          after), which an effect's Reset leaves alone; saved in the scene
//   Ui     panel state (category, selection, advanced open, Technical
//          Info, Show Hints); scene only
//   Volatile  the tree editor's controls, which mirror the selected
//          material's look (the trees node saves that); never saved
//   Private   (a flag beside a scope) kept off VaM's storable lists
//
// Persistence is the plugin's own: every storable is registered with VaM for
// Timeline and triggers but marked non-storable, and the facade writes one
// keyed block (Settings.BlockKey) into the scene instead. Reading is
// forgiving in both directions: unknown keys are ignored, missing keys take
// the plugin default, and a node without the block is read by storable name.
sealed class ParamSet
{
    public const int Look = 1;
    public const int Scene = 2;
    public const int Ui = 4;
    public const int Volatile = 8;
    // Private: not registered with VaM, so Timeline and trigger lists never
    // show it (panel state, the tree editor's controls). Saving and the
    // panel work as for any other entry.
    public const int Private = 16;
    public const int Saved = Look | Scene | Ui;

    readonly MVRScript script;
    readonly List<ParamEntry> entries = new List<ParamEntry>();
    readonly Dictionary<string, ParamEntry> byKey = new Dictionary<string, ParamEntry>();

    public ParamSet(MVRScript script)
    {
        this.script = script;
    }

    public int Count { get { return entries.Count; } }

    // VaM never stores our values by name; the facade's keyed block does.
    static void ApplyScope(JSONStorableParam js)
    {
        js.isStorable = false;
        js.isRestorable = false;
    }

    // A chooser value renamed in a release: scenes that hold the old value
    // load as the new one.
    public void AliasChoice(JSONStorableStringChooser js, string oldValue, string newValue)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            ParamEntry e = entries[i];
            if (e.C != js) continue;
            if (e.Aliases == null) e.Aliases = new Dictionary<string, string>();
            e.Aliases[oldValue] = newValue;
            return;
        }
    }

    // A chooser whose choices the scene fills at runtime (atom uids): a saved
    // value is applied as it is rather than refused while the list is still
    // empty at restore time.
    public void OpenChoices(JSONStorableStringChooser js)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].C == js) { entries[i].OpenChoices = true; return; }
    }

    public ParamEntry Entry(string key)
    {
        ParamEntry e;
        return key != null && byKey.TryGetValue(key, out e) ? e : null;
    }

    // Whether any key of `from` is one of ours (by key or storable name): a
    // blockless restore node that claims nothing is not our data.
    public bool ClaimsAny(JSONClass from)
    {
        if (from == null) return false;
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].Present(from)) return true;
        return false;
    }

    // "<feature>.<param>": lowercase letters, digits, and underscores only,
    // unique across the plugin. A bad or duplicate key is a programming
    // error and stops Init, which the facade reports.
    ParamEntry Register(string feature, int scope, string param)
    {
        if (string.IsNullOrEmpty(feature) || string.IsNullOrEmpty(param))
            throw new InvalidOperationException("Parameter key missing for feature '" + feature + "'.");
        string key = feature + "." + param;
        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '.';
            if (!ok) throw new InvalidOperationException("Parameter key '" + key + "' must be lowercase snake case.");
        }
        if (byKey.ContainsKey(key)) throw new InvalidOperationException("Parameter key '" + key + "' registered twice.");
        ParamEntry e = new ParamEntry();
        e.Feature = feature; e.Scope = scope; e.Key = key;
        entries.Add(e);
        byKey[key] = e;
        return e;
    }

    public JSONStorableFloat Float(string feature, int scope, string param, JSONStorableFloat js)
    {
        ApplyScope(js);
        if ((scope & Private) == 0) script.RegisterFloat(js);
        Register(feature, scope, param).F = js;
        return js;
    }

    public JSONStorableBool Bool(string feature, int scope, string param, JSONStorableBool js)
    {
        ApplyScope(js);
        if ((scope & Private) == 0) script.RegisterBool(js);
        Register(feature, scope, param).B = js;
        return js;
    }

    public JSONStorableString String(string feature, int scope, string param, JSONStorableString js)
    {
        ApplyScope(js);
        if ((scope & Private) == 0) script.RegisterString(js);
        Register(feature, scope, param).S = js;
        return js;
    }

    public JSONStorableStringChooser Chooser(string feature, int scope, string param, JSONStorableStringChooser js)
    {
        ApplyScope(js);
        if ((scope & Private) == 0) script.RegisterStringChooser(js);
        Register(feature, scope, param).C = js;
        return js;
    }

    public JSONStorableColor Color(string feature, int scope, string param, JSONStorableColor js)
    {
        ApplyScope(js);
        if ((scope & Private) == 0) script.RegisterColor(js);
        Register(feature, scope, param).K = js;
        return js;
    }

    // Writes every entry whose scope is in the mask. Feature null = all features.
    public int Write(JSONClass into, int scopeMask, string feature)
    {
        int n = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            ParamEntry e = entries[i];
            if ((e.Scope & scopeMask) == 0) continue;
            if (feature != null && e.Feature != feature) continue;
            e.Write(into);
            n++;
        }
        return n;
    }

    // Applies every stored value whose scope is in the mask. Unknown keys in
    // `from` are ignored. A missing key takes the plugin default when
    // `missingToDefault`, so a scene is a complete state and a value the
    // saving build did not have is never left stale; false overlays the node
    // onto the current values instead. Returns how many values were applied.
    //
    // One entry never takes the load down with it: a setter callback that
    // throws, or a value the file spells wrong, is caught, counted in
    // LastReadFailures, and the rest of the node still applies - a scene
    // half-applied in silence is the worse failure.
    public int Read(JSONClass from, int scopeMask, string feature, bool missingToDefault)
    {
        int n = 0;
        int failures = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            ParamEntry e = entries[i];
            if ((e.Scope & scopeMask) == 0) continue;
            if (feature != null && e.Feature != feature) continue;
            int outcome = ParamEntry.ReadAbsent;
            bool threw = false;
            try { if (from != null) outcome = e.Read(from); }
            catch (Exception) { threw = true; }
            bool failed = threw || outcome == ParamEntry.ReadUnreadable;
            if (outcome != ParamEntry.ReadAbsent) n++;
            // A read that threw already ran the setter; resetting would run it
            // again, so only an absent value falls back to its default here.
            else if (missingToDefault && !threw)
            {
                try { e.Reset(); }
                catch (Exception) { failed = true; }
            }
            if (failed) failures++;
        }
        LastReadFailures = failures;
        return n;
    }

    // Entries the last Read could not apply (a throwing setter, or a value the
    // file spells wrong).
    public int LastReadFailures;

    // Every value that is not its default, "key = value" one per line, for
    // the diagnostics report. Strings (panel state) are left out.
    public int NonDefault(System.Text.StringBuilder sb, int scopeMask)
    {
        int n = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            ParamEntry e = entries[i];
            if ((e.Scope & scopeMask) == 0) continue;
            string v = null;
            if (e.F != null) { if (e.F.val != e.F.defaultVal) v = ParamEntry.Num(e.F.val); }
            else if (e.B != null) { if (e.B.val != e.B.defaultVal) v = e.B.val ? "on" : "off"; }
            else if (e.C != null) { if ((e.C.val ?? "") != (e.C.defaultVal ?? "")) v = e.C.val; }
            else if (e.K != null)
            {
                // Colours in the same "h,s,v" shape the scene block uses, so a
                // report line can be compared with a saved scene directly.
                HSVColor c = e.K.val, d = e.K.defaultVal;
                if (c.H != d.H || c.S != d.S || c.V != d.V)
                    v = ParamEntry.Num(c.H) + "," + ParamEntry.Num(c.S) + "," + ParamEntry.Num(c.V);
            }
            if (v == null) continue;
            sb.Append(e.Key).Append(" = ").Append(v).Append('\n');
            n++;
        }
        return n;
    }

    public int Reset(int scopeMask, string feature)
    {
        int n = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            ParamEntry e = entries[i];
            if ((e.Scope & scopeMask) == 0) continue;
            if (feature != null && e.Feature != feature) continue;
            e.Reset();
            n++;
        }
        return n;
    }

}
