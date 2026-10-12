using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Everything the shell knows about one feature's UI: its rack row, its
// inspector bag, its advanced bag, and the delegates that produce its live text.
// Features fill the inspector through the shell's Begin/End helpers.
sealed class FeatureView
{
    public delegate string TextFn();
    public delegate void Fn();
    public delegate bool BoolFn();

    public string Id;
    public string Category;
    public string Label;
    public JSONStorableBool Master;          // null for tool rows
    public bool Hidden;                      // row and inspector withheld (experimental features)

    public readonly List<GameObject> RowBag = new List<GameObject>();
    public readonly List<GameObject> InspectorBag = new List<GameObject>();
    public readonly List<GameObject> AdvancedBag = new List<GameObject>();
    public JSONStorableBool AdvancedOpen;    // Ui-scope storable, created by the shell when the feature has advanced controls
    public readonly List<UiCondition> Conditions = new List<UiCondition>();  // controls shown only while a predicate holds

    public RackRow Row;
    public Text Caption;                     // inspector live caption
    public TextFn OnState;                   // rack row state text
    public string OffState;                  // the dashboard's word for off, when "off" would mislead (Soft Body Takeover: "default")
    public TextFn OnCaption;                 // inspector caption text (used when Guide is null)
    public string Guide;                     // the static sentence under Status: what the feature is for
    public Fn OnReset;                       // reset to defaults

    string lastState;
    string lastCaption;

    public bool IsOn
    {
        get { return Master == null || Master.val; }
    }

    // Register a bag that shows only while `when` holds (and the feature is
    // selected): sliders that only matter once a toggle above them is on.
    public List<GameObject> When(BoolFn when)
    {
        UiCondition c = new UiCondition();
        c.Bag = new List<GameObject>();
        c.When = when;
        Conditions.Add(c);
        return c.Bag;
    }

    // Applies every condition; true when any bag changed state.
    public bool ApplyConditions(bool selected)
    {
        bool changed = false;
        for (int i = 0; i < Conditions.Count; i++)
        {
            UiCondition c = Conditions[i];
            bool on = selected && c.When != null && c.When();
            if (c.Bag.Count > 0 && c.Bag[0] != null && c.Bag[0].activeSelf != on) changed = true;
            UiKit.SetBag(c.Bag, on);
        }
        return changed;
    }

    public void RefreshRow()
    {
        if (Row == null || Row.State == null) return;
        string s = OnState != null ? OnState() : (Master != null ? (Master.val ? "on" : "off") : "");
        if (s == lastState) return;
        lastState = s;
        Row.SetState(s);
    }

    // The caption under Enabled has one shape for every feature:
    //   Status: 2 touching
    //   Touching the skin with 2 contacts.
    // The first line is the row's own state, so it reads the same here and
    // on the rack; the second is the feature's sentence, with any leading
    // "Off." / "On," / "Needs X on:" the sentence used to carry stripped, since
    // the status line now says it.
    public void RefreshCaption()
    {
        if (Caption == null || (OnCaption == null && Guide == null)) return;
        string state = OnState != null ? OnState() : (Master != null ? (Master.val ? "on" : "off") : "");
        // A static guide says what the feature is for; the live state is the
        // Status line, and anything that changes has its own place (the rack
        // badge, the brush card, the influence lines, Diagnose).
        string s = Guide != null ? Guide : Normalize(OnCaption());
        string text = "Status: " + Capitalize(state) + "\n" + s;
        if (text == lastCaption) return;
        lastCaption = text;
        Caption.text = text;
    }

    static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Trim();
        // "Off. ...", "On. ...", "On, waiting ...", "On: ..." -> the sentence after.
        if (s.StartsWith("Off.", System.StringComparison.Ordinal) || s.StartsWith("On.", System.StringComparison.Ordinal)
            || s.StartsWith("On,", System.StringComparison.Ordinal) || s.StartsWith("On:", System.StringComparison.Ordinal))
            s = s.Substring(s.IndexOfAny(new char[] { '.', ',', ':' }) + 1).Trim();
        // "Needs Contact Dent on: the waves start ..." -> "The waves start ..."
        if (s.StartsWith("Needs ", System.StringComparison.Ordinal))
        {
            int colon = s.IndexOf(':');
            if (colon > 0 && colon < s.Length - 1) s = s.Substring(colon + 1).Trim();
        }
        return Capitalize(s);
    }

    static string Capitalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}

// A group of controls shown only while a predicate holds.
sealed class UiCondition
{
    public List<GameObject> Bag;
    public FeatureView.BoolFn When;
}

// A category: fixed forever, holds a rack of features and optional custom content.
sealed class UiCategory
{
    public string Id;
    public string Label;
    public string Hint;
    public readonly List<GameObject> Bag = new List<GameObject>();       // custom content (Overview, Presets)
    public readonly List<GameObject> EmptyBag = new List<GameObject>();  // shown when the rack has no features
    public readonly List<FeatureView> Features = new List<FeatureView>();
    public readonly List<string> HeaderBefore = new List<string>();      // feature id -> a rack header printed above it
    public readonly List<string> HeaderText = new List<string>();
    public readonly List<UiSection> Groups = new List<UiSection>();      // rack cards, built by the shell
    public readonly List<List<FeatureView>> GroupMembers = new List<List<FeatureView>>();
    public string LastSelected;
    public SplitPane Split;                                             // the category's two columns

    public void Header(string beforeFeatureId, string text)
    {
        HeaderBefore.Add(beforeFeatureId);
        HeaderText.Add(text);
    }
}
