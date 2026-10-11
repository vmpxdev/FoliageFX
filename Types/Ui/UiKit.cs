using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Brand-consistent UI primitives on top of VaM's stock plugin controls, the
// Live Decals palette and typography. Every control is created through VaM's
// own Create* call (so Timeline, triggers, and presets see plain JSONStorables)
// and then routed: with a SplitPane open, `right` decides the column of that
// pane; with a section (card) open, the control goes inside that card; with
// nothing open, the element is host-level (the category bar, the panes
// themselves). UiHost does the actual reparenting. See docs/UI_DESIGN.md.
sealed class UiKit
{
    public delegate void IdFn(string id);

    public Font Font;
    public OverlayLog Log;                                          // boundary for UI callbacks
    public SplitPane Split;                                         // the pane being built, or null
    public UiSection Section;                                       // the card being built, or null
    public string Prefix;                                           // stripped from control labels (the feature name)
    public readonly List<GameObject> HostItems = new List<GameObject>();
    public readonly List<UiSection> Sections = new List<UiSection>();
    readonly Dictionary<GameObject, UiSection> owner = new Dictionary<GameObject, UiSection>();
    // Wrapped labels and the pads that hold them. Heights are estimated at
    // build from a character count; the fit pass replaces the estimate with
    // the height the text actually renders at, so a caption or hint that wraps
    // further than guessed grows its pad (and its card) instead of spilling.
    readonly List<FitBox> fits = new List<FitBox>();
    readonly MVRScript script;

    public UiKit(MVRScript script)
    {
        this.script = script;
    }

    // A button's action as a VaM action too, so a trigger or Timeline can
    // fire it: "Goosebumps Reseed", "Vellus Hair Reseed". Only buttons a
    // scene has a reason to fire; setup and reset buttons stay buttons.
    // The action runs inside the log boundary like the button does.
    public JSONStorableAction Action(string name, string what, OverlayLog.Fn fn)
    {
        OverlayLog boundary = Log;
        JSONStorableAction a = new JSONStorableAction(name, delegate
        {
            if (boundary != null) boundary.Run(what, fn);
            else fn();
        });
        script.RegisterAction(a);
        return a;
    }

    public Font EnsureFont(Font prefer)
    {
        if (prefer != null)
        {
            Font = prefer;
            return Font;
        }
        Font = OverlayLog.Probe(delegate
        {
            return Resources.GetBuiltinResource(typeof(Font), "Arial.ttf");
        }) as Font;
        return Font;
    }

    // ---- labels ----

    // Storable names stay unique and stable ("Goosebumps Rise s") because
    // Timeline, triggers, and scenes address them. The label a control shows
    // inside its own inspector drops the feature prefix and writes units the
    // usual way: "Rise (s)".
    public string Pretty(string name)
    {
        return Pretty(Prefix, name);
    }

    // The same, for a given feature prefix (a load report names a setting
    // outside its inspector's build).
    public static string Pretty(string prefix, string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        string s = name;
        if (!string.IsNullOrEmpty(prefix) && s.Length > prefix.Length + 1
            && s.StartsWith(prefix + " ", System.StringComparison.OrdinalIgnoreCase))
            s = s.Substring(prefix.Length + 1);
        if (s.EndsWith(" (diagnostic)")) s = s.Substring(0, s.Length - 13);
        for (int i = 0; i < Units.Length; i++)
        {
            string u = Units[i];
            if (s.Length > u.Length && s.EndsWith(u))
                return s.Substring(0, s.Length - u.Length) + " (" + u.Trim() + ")";
        }
        return s;
    }

    static readonly string[] Units = { " mm", " ms", " m/s", " s", " Hz", " deg", " %", " cm", " m" };

    // A colour as rich text wants it: "RRGGBB".
    public static string Hex(Color c)
    {
        int r = Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f), g = Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f), b = Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f);
        return r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
    }

    // A label's bracketed tail is one of the units Pretty writes ("mm", "m"),
    // not words ("White to Yellow", "thousands").
    public static bool IsUnit(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (int i = 0; i < Units.Length; i++) if (Units[i].Trim() == text) return true;
        return false;
    }

    // What the panel shows for each storable it built a control for, so a
    // load report names a setting exactly as the panel does. The first
    // control wins.
    readonly Dictionary<string, string> shownLabel = new Dictionary<string, string>();

    void Remember(string name, string label)
    {
        if (string.IsNullOrEmpty(name) || shownLabel.ContainsKey(name)) return;
        shownLabel[name] = label;
    }

    public string ShownLabel(string name)
    {
        string s;
        return name != null && shownLabel.TryGetValue(name, out s) ? s : null;
    }

    // ---- stock controls ----

    public UIDynamicSlider Slider(JSONStorableFloat js, bool right, List<GameObject> bag)
    {
        UIDynamicSlider s = script.CreateSlider(js, right);
        if (s != null) OverlayLog.Probe(delegate { s.label = Pretty(js.name); return null; });
        Remember(js.name, Pretty(js.name));
        Track(s, bag, right);
        return s;
    }

    // A slider with a one-line hint under it: what it does and which way is
    // more, in the user's words. Only for a control whose label does not
    // already say it; the hint lines can be hidden together (Show Hints).
    public UIDynamicSlider Slider(JSONStorableFloat js, bool right, List<GameObject> bag, string hint)
    {
        UIDynamicSlider s = Slider(js, right, bag);
        Hint(hint, right, bag);
        return s;
    }

    // A slider with no real ceiling: the storable is unconstrained and VaM's
    // range buttons on the slider let the user multiply its range, so a
    // value past the end of the bar is a drag away rather than a typed one.
    public UIDynamicSlider OpenSlider(JSONStorableFloat js, bool right, List<GameObject> bag, string hint)
    {
        UIDynamicSlider s = Slider(js, right, bag, hint);
        if (s != null) OverlayLog.Probe(delegate { s.rangeAdjustEnabled = true; return null; });
        return s;
    }

    public UIDynamicToggle Toggle(JSONStorableBool js, bool right, List<GameObject> bag)
    {
        UIDynamicToggle t = script.CreateToggle(js, right);
        if (t != null) OverlayLog.Probe(delegate { t.label = Pretty(js.name); return null; });
        Remember(js.name, Pretty(js.name));
        Track(t, bag, right);
        return t;
    }

    public UIDynamicToggle Toggle(JSONStorableBool js, bool right, List<GameObject> bag, string hint)
    {
        UIDynamicToggle t = Toggle(js, right, bag);
        Hint(hint, right, bag);
        return t;
    }

    // The hint line itself: muted italic, one line (two at most), fitted like
    // every caption. Kept in a list so Show Hints can hide them all at once.
    readonly List<GameObject> hintLines = new List<GameObject>();

    public Text Hint(string text, bool right, List<GameObject> bag)
    {
        if (string.IsNullOrEmpty(text)) return null;
        Text tx = Caption(FoliageFXConfig.HintPrefix + text, right, bag, 1);
        if (tx == null) return null;
        tx.fontSize = 21;
        tx.fontStyle = FontStyle.Italic;
        // The line belongs to the control above it, and must read so: an
        // elbow from above leads into it, the text sits at the top of its
        // pad, indented, with all the spare room below it, so it hugs its
        // slider and a clear gap follows before the next control.
        tx.alignment = TextAnchor.UpperLeft;
        RectTransform rt = tx.rectTransform;
        rt.offsetMin = new Vector2(FoliageFXConfig.HintIndent, FoliageFXConfig.HintGapBelow);
        rt.offsetMax = new Vector2(-2f, -2f);
        if (fits.Count > 0)
        {
            FitBox fit = fits[fits.Count - 1];
            fit.Extra = FoliageFXConfig.HintGapBelow + 2f;
            fit.MinHeight = FoliageFXConfig.LineH + fit.Extra;
        }
        GameObject root = tx.transform.parent != null ? tx.transform.parent.gameObject : tx.gameObject;
        hintLines.Add(root);
        return tx;
    }

    // Applied after the bags are shown (a bag shows every item it holds), so
    // with Show Hints off the hint lines go back out of sight.
    public void ApplyHints(bool show)
    {
        if (show) return;
        for (int i = 0; i < hintLines.Count; i++)
        {
            GameObject g = hintLines[i];
            if (g != null && g.activeSelf) g.SetActive(false);
        }
    }

    // VaM's HSV colour picker, tracked into the column like any control.
    // VaM places the swatch cursor in pixels, the box's size times the colour's
    // saturation and value, and only at the moment the colour changes. So a
    // colour set while the card is hidden (the box has no size: the picker
    // being built, a preset or a default loading, a reset) or a box that
    // changes size afterwards leaves the cursor where it was, outside the box
    // or just off the colour. Every picker is kept and RefreshColorPickers
    // puts the cursor where the colour says, whenever the two disagree.
    readonly List<UIDynamicColorPicker> colorPickers = new List<UIDynamicColorPicker>();

    public UIDynamicColorPicker ColorPicker(JSONStorableColor js, bool right, List<GameObject> bag)
    {
        UIDynamicColorPicker p = script.CreateColorPicker(js, right);
        if (p != null) OverlayLog.Probe(delegate { p.label = Pretty(js.name); return null; });
        Remember(js.name, Pretty(js.name));
        Track(p, bag, right);
        if (p != null) colorPickers.Add(p);
        // VaM's colour picker sits its Copy / Default Value / Paste row right at
        // its bottom edge, so the next control butts against it; a small spacer
        // gives it room without a full empty row.
        UIDynamic gap = Pad(right, 18f);
        if (gap != null) Track(gap, bag, right);
        return p;
    }

    // Every shown colour picker's cursor, held where its colour says: the
    // box's size times the picker's own saturation and value, the same sum VaM
    // makes when the colour changes. Checked on every Apply and slow tick (a
    // handful of pickers, a compare each); written only when it is off by more
    // than half a pixel, so a picker being dragged is never fought. The colour
    // itself is not touched, so no callback runs and nothing is marked changed.
    public void RefreshColorPickers()
    {
        for (int i = colorPickers.Count - 1; i >= 0; i--)
        {
            UIDynamicColorPicker p = colorPickers[i];
            if (p == null) { colorPickers.RemoveAt(i); continue; }
            if (!p.gameObject.activeInHierarchy) continue;
            HSVColorPicker hp = p.colorPicker;
            if (hp == null || hp.Selector == null) continue;
            RectTransform box = hp.Container != null ? hp.Container : hp.transform as RectTransform;
            if (box == null) continue;
            Rect r = box.rect;
            if (r.width < 1f || r.height < 1f) continue;   // not laid out yet: the next pass has it
            RectTransform cursor = hp.Selector.rectTransform;
            Vector2 want = new Vector2(r.width * hp.xVal, r.height * hp.yVal);
            if ((cursor.anchoredPosition - want).sqrMagnitude > 0.25f) cursor.anchoredPosition = want;
        }
    }

    // A feature's master toggle: the stock control painted in the accent so
    // the on/off switch is the one thing that stands out in an inspector.
    public UIDynamicToggle MasterToggle(JSONStorableBool js, bool right, List<GameObject> bag)
    {
        return MasterToggle(js, right, bag, null);
    }

    public UIDynamicToggle MasterToggle(JSONStorableBool js, bool right, List<GameObject> bag, string label)
    {
        UIDynamicToggle t = Toggle(js, right, bag);
        if (t != null && label != null) OverlayLog.Probe(delegate { t.label = label; return null; });
        if (label != null && js != null && js.name != null) shownLabel[js.name] = label;
        PaintToggle(t, FoliageFXConfig.Accent, FoliageFXConfig.OnAccent);
        return t;
    }

    public UIDynamicPopup Popup(JSONStorableStringChooser js, bool right, List<GameObject> bag)
    {
        UIDynamicPopup p = script.CreateScrollablePopup(js, right);
        if (p != null) OverlayLog.Probe(delegate { p.label = Pretty(js.name); return null; });
        Remember(js.name, Pretty(js.name));
        Track(p, bag, right);
        return p;
    }

    public UIDynamicPopup Popup(JSONStorableStringChooser js, bool right, List<GameObject> bag, string hint)
    {
        UIDynamicPopup p = Popup(js, right, bag);
        Hint(hint, right, bag);
        return p;
    }

    // Every click runs inside a boundary so a failing action reports to the
    // status well instead of vanishing into Unity's event system.
    public void Wire(UIDynamicButton btn, string what, OverlayLog.Fn action)
    {
        if (btn == null || btn.button == null || action == null) return;
        OverlayLog boundary = Log;
        btn.button.onClick.AddListener(delegate { OverlayLog.SafeRun(boundary, what, action); });
    }

    // ---- layout primitives ----

    public UIDynamic Pad(bool right, float height)
    {
        UIDynamic pad = script.CreateSpacer(right);
        if (pad == null) return null;
        pad.height = height;
        LayoutElement le = pad.gameObject.GetComponent<LayoutElement>();
        if (le == null) le = pad.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        le.flexibleHeight = 0f;
        le.flexibleWidth = 1f;
        Ghost(pad);
        return pad;
    }

    public void Ghost(UIDynamic pad)
    {
        if (pad == null) return;
        Image img = pad.gameObject.GetComponent<Image>();
        if (img == null) return;
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = false;
    }

    public UIDynamic Title(string text, bool right, List<GameObject> bag)
    {
        UIDynamic pad = Pad(right, FoliageFXConfig.WellTitleH);
        if (pad == null) return null;
        MakeLabel(pad.transform, text, 32, FontStyle.Bold, FoliageFXConfig.Title, TextAnchor.MiddleLeft, false);
        Track(pad, bag, right);
        return pad;
    }

    // Captions wrap; the pad grows by the estimated line count so two-line
    // captions never spill into the next element.
    public Text Caption(string text, bool right, List<GameObject> bag)
    {
        return Caption(text, right, bag, 0);
    }

    // `lines` reserves room for a live caption that will be longer than its
    // initial text (the inspector caption is created empty).
    public Text Caption(string text, bool right, List<GameObject> bag, int lines)
    {
        int n = WrapLines(text);
        if (lines > n) n = lines;
        float h = Mathf.Max(FoliageFXConfig.CaptionH, n * FoliageFXConfig.LineH + 12f);
        UIDynamic pad = Pad(right, h);
        if (pad == null) return null;
        Track(pad, bag, right);
        Text tx = MakeLabel(pad.transform, text, 24, FontStyle.Normal, FoliageFXConfig.Caption, TextAnchor.MiddleLeft, true);
        FitBox fit = new FitBox();
        fit.Pad = pad;
        fit.MinHeight = Mathf.Max(FoliageFXConfig.CaptionH, lines * FoliageFXConfig.LineH + 12f);   // never below what was reserved
        fit.Extra = 12f;
        fit.Add(tx, null);
        fits.Add(fit);
        return tx;
    }

    // Each entry is a bullet line, except one beginning with "> ", which is
    // plain text (a lead-in such as "Point the cursor at the body and:").
    // Public so a line whose text changes later is set the same way.
    public static string HintLine(string line)
    {
        if (line != null && line.StartsWith("> ", System.StringComparison.Ordinal)) return line.Substring(2);
        return "·  " + line;
    }

    // A card's story box: one to three short lines under the controls. It
    // goes with Show Hints like the lines under the controls; `always` keeps
    // it up regardless (the brush keys, which are controls, not explanation).
    public UIDynamic HintBox(List<string> lines, bool right, List<GameObject> bag)
    {
        return HintBox(lines, right, bag, false);
    }

    public UIDynamic HintBox(List<string> lines, bool right, List<GameObject> bag, bool always)
    {
        return HintBox(lines, right, bag, always, null);
    }

    // As above; `texts`, when given, receives each line's Text in order, for
    // a line whose text changes later (the brush card's key line): set it
    // through HintLine, and the box's fit re-measures it like a caption.
    public UIDynamic HintBox(List<string> lines, bool right, List<GameObject> bag, bool always, List<Text> texts)
    {
        int n = lines != null ? lines.Count : 0;
        if (n < 1) return null;
        float gap = 4f;
        float h = FoliageFXConfig.WellPadTop + FoliageFXConfig.WellPadBot + (n - 1) * gap;
        for (int i = 0; i < n; i++) h += WrapLines(HintLine(lines[i])) * FoliageFXConfig.LineH;
        UIDynamic box = Pad(right, h);
        if (box == null) return null;
        // Inside a card the well is a shade lighter so it still reads as a hint.
        Well(box.gameObject, gap, TextAnchor.UpperLeft, false, Section != null ? FoliageFXConfig.Surface : FoliageFXConfig.PreviewBg);
        FitBox fit = new FitBox();
        fit.Pad = box;
        fit.Extra = FoliageFXConfig.WellPadTop + FoliageFXConfig.WellPadBot;
        fit.Gap = gap;
        for (int i = 0; i < n; i++)
        {
            string text = HintLine(lines[i]);
            float lineH = WrapLines(text) * FoliageFXConfig.LineH;
            GameObject line = new GameObject("hint");
            line.transform.SetParent(box.transform, false);
            LayoutElement le = line.AddComponent<LayoutElement>();
            le.minHeight = lineH;
            le.preferredHeight = lineH;
            le.flexibleWidth = 1f;
            le.flexibleHeight = 0f;
            Text tx = MakeLabel(line.transform, text, 24, FontStyle.Normal, FoliageFXConfig.Caption, TextAnchor.MiddleLeft, true);
            fit.Add(tx, le);
            if (texts != null) texts.Add(tx);
        }
        fits.Add(fit);
        Track(box, bag, right);
        if (!always) hintLines.Add(box.gameObject);
        return box;
    }

    // ---- influence line: "Scaled by Tissue Depth and Rigidity." under a card title ----

    readonly List<InfluenceRow> influenceRows = new List<InfluenceRow>();

    // One muted sentence naming the features that scale this card's settings
    // (or, on a map's inspector, the features that read the map). On the slow
    // tick each name carries its feature's state, "Tissue Depth (off)",
    // "Rigidity (empty)", so a slider that seems to do nothing says why, and
    // features the Experimental switch hides drop out of the sentence.
    public InfluenceRow Influences(string verb, List<Influence> items, bool right, List<GameObject> bag)
    {
        List<string> verbs = new List<string>();
        verbs.Add(verb);
        List<List<Influence>> groups = new List<List<Influence>>();
        groups.Add(items);
        return Influences(verbs, groups, right, bag);
    }

    // Several clauses on one line, each with its own verb, when one feature
    // is shaped by different maps in different ways:
    //   Where: the Soft Tissue Area Map.  Mass and Sag: Tissue Depth Map.  Scaled by: Rigidity Map.
    public InfluenceRow Influences(List<string> verbs, List<List<Influence>> groups, bool right, List<GameObject> bag)
    {
        InfluenceRow row = new InfluenceRow();
        if (verbs == null || groups == null || verbs.Count == 0 || verbs.Count != groups.Count) return row;
        for (int c = 0; c < verbs.Count; c++)
        {
            if (groups[c] == null || groups[c].Count == 0) continue;
            InfluenceClause clause = new InfluenceClause();
            clause.Verb = verbs[c];
            for (int i = 0; i < groups[c].Count; i++) clause.Items.Add(groups[c][i]);
            row.Clauses.Add(clause);
        }
        if (row.Clauses.Count == 0) return row;
        row.Text = Caption(row.Compose(null), right, bag, row.Clauses.Count);
        if (row.Text != null)
        {
            row.Text.fontSize = 21;
            row.Text.fontStyle = FontStyle.Italic;
            row.Root = row.Text.transform.parent != null ? row.Text.transform.parent.gameObject : row.Text.gameObject;
        }
        influenceRows.Add(row);
        return row;
    }

    public void RefreshInfluences(HiddenFn hidden)
    {
        for (int r = 0; r < influenceRows.Count; r++)
        {
            InfluenceRow row = influenceRows[r];
            if (row.Text == null || row.Root == null || !row.Root.activeInHierarchy) continue;
            string text = row.Compose(hidden);
            if (row.Text.text != text) row.Text.text = text;
        }
    }

    public delegate bool HiddenFn(string featureId);

    // A muted line whose text comes from a delegate on the slow tick: a fact
    // about the card's settings that changes while they are used ("Placed
    // 182,452 of the 200,000 cap, limited by Spacing.").
    public delegate string LineFn();
    readonly List<LiveLine> liveLines = new List<LiveLine>();

    public Text LiveLine(LineFn fn, bool right, List<GameObject> bag, int lines)
    {
        if (fn == null) return null;
        LiveLine line = new LiveLine();
        line.Fn = fn;
        string initial = "";
        object first = OverlayLog.Probe(delegate { return fn(); });
        if (first != null) initial = (string)first;
        line.Text = Caption(initial, right, bag, lines);
        if (line.Text == null) return null;
        line.Text.fontSize = 21;
        line.Text.fontStyle = FontStyle.Italic;
        line.Root = line.Text.transform.parent != null ? line.Text.transform.parent.gameObject : line.Text.gameObject;
        liveLines.Add(line);
        return line.Text;
    }

    public void RefreshLiveLines()
    {
        for (int i = 0; i < liveLines.Count; i++)
        {
            LiveLine line = liveLines[i];
            if (line.Text == null || line.Root == null || !line.Root.activeInHierarchy) continue;
            object o = OverlayLog.Probe(delegate { return line.Fn(); });
            string text = o != null ? (string)o : "";
            if (line.Text.text != text) line.Text.text = text;
        }
    }

    // ---- tooltip: one shared panel, parented at the top of the plugin's UI ----

    public Transform TooltipRoot;      // set by the host once VaM has built the panel
    GameObject tooltip;
    Text tooltipText;
    Transform tooltipAnchor;

    public void ShowTooltip(string text, Transform anchor)
    {
        if (string.IsNullOrEmpty(text) || anchor == null) return;
        Transform parent = TooltipRoot != null ? TooltipRoot : anchor.root;
        if (tooltip == null || tooltip.transform.parent != parent)
        {
            if (tooltip != null) Object.Destroy(tooltip);
            tooltip = new GameObject("FoliageFXTooltip");
            tooltip.transform.SetParent(parent, false);
            RectTransform rt = tooltip.AddComponent<RectTransform>();
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(FoliageFXConfig.TooltipW, 60f);
            LayoutElement le = tooltip.AddComponent<LayoutElement>();
            le.ignoreLayout = true;   // floats over the layout it is parented to
            Image bg = tooltip.AddComponent<Image>();
            bg.color = FoliageFXConfig.TooltipBg;
            bg.raycastTarget = false;
            VerticalLayoutGroup v = tooltip.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(12, 12, 8, 8);
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            ContentSizeFitter fit = tooltip.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            tooltipText = MakeLabel(tooltip.transform, "", 22, FontStyle.Normal, FoliageFXConfig.OnAccentMuted, TextAnchor.MiddleLeft, true);
            LayoutElement tle = tooltipText.gameObject.AddComponent<LayoutElement>();
            tle.flexibleWidth = 1f;
        }
        tooltipText.text = text;
        tooltipAnchor = anchor;
        // Above and to the left of the badge, in the badge's own units so the
        // canvas scale does not matter; drawn last so it sits over the cards.
        RectTransform art = anchor as RectTransform;
        Vector3 up = anchor.TransformVector(new Vector3(0f, art != null ? art.rect.height * 0.5f + 8f : 24f, 0f));
        tooltip.transform.position = anchor.position + up;
        tooltip.transform.SetAsLastSibling();
        tooltip.SetActive(true);
    }

    public void HideTooltip()
    {
        tooltipAnchor = null;
        if (tooltip != null && tooltip.activeSelf) tooltip.SetActive(false);
    }

    // The slow tick: a tooltip stays while its badge is still on screen, and
    // goes when the row hid or the page changed under the pointer.
    public void HideTooltipIfStale()
    {
        if (tooltip == null || !tooltip.activeSelf) return;
        if (tooltipAnchor == null || !tooltipAnchor.gameObject.activeInHierarchy) HideTooltip();
    }

    // Measure every visible wrapped label at the width it renders at and grow
    // (or shrink back to its reserve) the pad that holds it. Returns true when
    // any height changed, so the caller re-sizes the cards and columns. Runs on
    // the slow tick; a Text's preferred height is a cheap cached layout query.
    public bool FitTexts()
    {
        bool changed = false;
        for (int i = fits.Count - 1; i >= 0; i--)
        {
            FitBox f = fits[i];
            if (f.Pad == null || f.Pad.gameObject == null) { fits.RemoveAt(i); continue; }
            if (!f.Pad.gameObject.activeInHierarchy) continue;
            if (f.Apply()) changed = true;
        }
        return changed;
    }

    // Lines a 24 px wrapped label needs in one column. An estimate from the
    // character count; a column fits about FoliageFXConfig.CharsPerLine.
    static int WrapLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 1;
        // Explicit line breaks count as lines of their own, each wrapped by length.
        string[] parts = text.Split((char)10);
        int lines = 0;
        for (int i = 0; i < parts.Length; i++)
            lines += parts[i].Length == 0 ? 1 : 1 + (parts[i].Length - 1) / FoliageFXConfig.CharsPerLine;
        return lines < 1 ? 1 : lines;
    }

    // A fixed-height well of read-only text that scrolls by wheel or drag,
    // with a thin scrollbar at its right edge that shows while there is more
    // than fits: release notes, long notices. The text is the scroll content,
    // sized to its own preferred height, so it grows with what it says; the
    // wheel eases (SmoothScroll) rather than stepping.
    public Text ScrollBox(bool right, List<GameObject> bag, float height, string text)
    {
        UIDynamic box = Pad(right, height);
        if (box == null) return null;
        Track(box, bag, right);
        Image bg = box.gameObject.GetComponent<Image>();
        if (bg == null) bg = box.gameObject.AddComponent<Image>();
        bg.color = FoliageFXConfig.PreviewBg;
        bg.raycastTarget = false;

        GameObject viewport = new GameObject("viewport");
        viewport.transform.SetParent(box.transform, false);
        RectTransform vrt = viewport.AddComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = new Vector2(FoliageFXConfig.WellPadX, FoliageFXConfig.WellPadBot);
        vrt.offsetMax = new Vector2(-(FoliageFXConfig.WellPadX + 18f), -FoliageFXConfig.WellPadTop);
        viewport.AddComponent<RectMask2D>();
        // The wheel and the drag land on the viewport and climb to the scroll
        // rect on the box; the text itself never catches the pointer.
        Image catcher = viewport.AddComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 0f);
        catcher.raycastTarget = true;

        Text tx = MakeLabel(viewport.transform, text, 22, FontStyle.Normal, FoliageFXConfig.Caption, TextAnchor.UpperLeft, true);
        tx.gameObject.name = "content";
        RectTransform crt = tx.rectTransform;
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.offsetMin = new Vector2(2f, 0f);
        crt.offsetMax = new Vector2(-2f, 0f);
        ContentSizeFitter fit = tx.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject sbGo = new GameObject("scrollbar");
        sbGo.transform.SetParent(box.transform, false);
        RectTransform srt = sbGo.AddComponent<RectTransform>();
        srt.anchorMin = new Vector2(1f, 0f);
        srt.anchorMax = new Vector2(1f, 1f);
        srt.pivot = new Vector2(1f, 0.5f);
        srt.sizeDelta = new Vector2(8f, -(FoliageFXConfig.WellPadTop + FoliageFXConfig.WellPadBot));
        srt.anchoredPosition = new Vector2(-6f, 0f);
        Image track = sbGo.AddComponent<Image>();
        track.color = FoliageFXConfig.Surface;
        track.raycastTarget = true;
        GameObject handleGo = new GameObject("handle");
        handleGo.transform.SetParent(sbGo.transform, false);
        RectTransform hrt = handleGo.AddComponent<RectTransform>();
        hrt.anchorMin = Vector2.zero;
        hrt.anchorMax = Vector2.one;
        hrt.offsetMin = Vector2.zero;
        hrt.offsetMax = Vector2.zero;
        Image handle = handleGo.AddComponent<Image>();
        handle.color = FoliageFXConfig.Caption;
        handle.raycastTarget = true;
        Scrollbar sb = sbGo.AddComponent<Scrollbar>();
        sb.handleRect = hrt;
        sb.targetGraphic = handle;
        sb.direction = Scrollbar.Direction.BottomToTop;

        ScrollRect scroll = box.gameObject.AddComponent<ScrollRect>();
        scroll.content = crt;
        scroll.viewport = vrt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 12f;   // only the scrollbar's own wheel path uses this; the viewport eases
        scroll.inertia = true;
        scroll.verticalScrollbar = sb;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        // The wheel over the text eases instead of jumping a notch at a time.
        SmoothScroll smooth = viewport.AddComponent<SmoothScroll>();
        smooth.Scroll = scroll;
        return tx;
    }

    public Transform Row(bool right, float height, List<GameObject> bag)
    {
        UIDynamic pad = Pad(right, height);
        if (pad == null) return null;
        Track(pad, bag, right);
        return Strip(pad.transform, "FoliageFXRow", 6f, 0f, 0f);
    }

    // One line of plain text at a fixed height (a row label above a button row).
    public Text Label(string text, bool right, List<GameObject> bag, float height)
    {
        UIDynamic pad = Pad(right, height);
        if (pad == null) return null;
        Track(pad, bag, right);
        return MakeLabel(pad.transform, text, 24, FontStyle.Normal, FoliageFXConfig.Caption, TextAnchor.LowerLeft, false);
    }

    public UIDynamicButton Button(Transform parent, string label, Color bg, Color fg)
    {
        if (parent == null || script.manager == null) return null;
        Transform prefab = script.manager.configurableButtonPrefab;
        if (prefab == null) return null;
        GameObject go = Object.Instantiate(prefab.gameObject);
        go.transform.SetParent(parent, false);
        ContentSizeFitter fit = go.GetComponent<ContentSizeFitter>();
        if (fit != null) Object.DestroyImmediate(fit);
        LayoutElement le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.ignoreLayout = false;
        le.flexibleWidth = 1f;
        le.minWidth = 36f;
        le.preferredWidth = 80f;
        le.minHeight = 44f;
        le.preferredHeight = 44f;
        le.flexibleHeight = 0f;
        UIDynamicButton btn = go.GetComponent<UIDynamicButton>();
        if (btn == null) return null;
        btn.label = label;
        Paint(btn, bg, fg);
        return btn;
    }

    public void Paint(UIDynamicButton btn, Color bg, Color fg)
    {
        if (btn == null) return;
        OverlayLog.Probe(delegate
        {
            btn.buttonColor = bg;
            btn.textColor = fg;
            if (btn.button != null)
            {
                ColorBlock cb = btn.button.colors;
                cb.normalColor = Color.white;
                cb.highlightedColor = Color.white;
                cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
                cb.colorMultiplier = 1f;
                btn.button.colors = cb;
                if (btn.button.targetGraphic != null)
                    btn.button.targetGraphic.color = bg;
            }
            return null;
        });
    }

    public void PaintToggle(UIDynamicToggle t, Color bg, Color fg)
    {
        if (t == null) return;
        OverlayLog.Probe(delegate
        {
            t.backgroundColor = bg;
            t.textColor = fg;
            return null;
        });
    }

    // ---- sections (cards) ----

    // A card: a dark well that groups the controls of one functionality area
    // (Look, Drive, Area, Advanced) so the areas are easy to spot. Everything
    // the kit creates until EndSection goes inside it. The card is one item
    // of its column and one item of `bag`; its children are tracked into the
    // card and into the same bag, so hiding the bag hides all of it.
    public UiSection BeginSection(string title, bool right, List<GameObject> bag)
    {
        return BeginSection(title, right, bag, 26);
    }

    // `titleSize` 32 makes a header card (the page title, an inspector's
    // title with its Enabled toggle and caption); 26 is a section card.
    public UiSection BeginSection(string title, bool right, List<GameObject> bag, int titleSize)
    {
        EndSection();
        UIDynamic pad = Pad(right, 80f);
        if (pad == null) return null;
        Track(pad, bag, right);
        UiSection s = new UiSection();
        s.Title = title;
        s.Pad = pad;
        Image bg = pad.gameObject.GetComponent<Image>();
        if (bg == null) bg = pad.gameObject.AddComponent<Image>();
        bg.color = FoliageFXConfig.CardBg;
        bg.raycastTarget = false;
        VerticalLayoutGroup v = pad.gameObject.GetComponent<VerticalLayoutGroup>();
        if (v == null) v = pad.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = FoliageFXConfig.CardGap;
        v.padding = new RectOffset(FoliageFXConfig.WellPadX, FoliageFXConfig.WellPadX, FoliageFXConfig.CardPadTop, FoliageFXConfig.CardPadBot);
        v.childAlignment = TextAnchor.UpperLeft;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        Sections.Add(s);
        Section = s;
        if (!string.IsNullOrEmpty(title))
        {
            GameObject tg = new GameObject("section_title");
            LayoutElement le = tg.AddComponent<LayoutElement>();
            float th = titleSize >= 30 ? FoliageFXConfig.WellTitleH : FoliageFXConfig.CardTitleH;
            le.minHeight = th;
            le.preferredHeight = th;
            le.flexibleWidth = 1f;
            le.flexibleHeight = 0f;
            Text tx = FreeLabel(tg.transform, title, titleSize, true, TextAnchor.MiddleLeft, FoliageFXConfig.Title);
            s.TitleText = tx;
            RectTransform rt = tx.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(2f, 0f);
            rt.offsetMax = new Vector2(-2f, 0f);
            s.Items.Add(tg);
            owner[tg] = s;
        }
        return s;
    }

    public void EndSection()
    {
        Section = null;
    }

    // True for a control that lives inside a card (the card, not the control,
    // is the column item).
    public bool InSection(GameObject go)
    {
        return go != null && owner.ContainsKey(go);
    }

    // Pointer enter and exit on an object that already handles pointer
    // events (a Button or Toggle): the trigger sits on the same GameObject,
    // so the event system runs both.
    public void OnHover(GameObject go, OverlayLog.Fn enter, OverlayLog.Fn exit)
    {
        if (go == null) return;
        EventTrigger trigger = go.GetComponent<EventTrigger>();
        if (trigger == null) trigger = go.AddComponent<EventTrigger>();
        OverlayLog boundary = Log;
        EventTrigger.Entry on = new EventTrigger.Entry();
        on.eventID = EventTriggerType.PointerEnter;
        on.callback.AddListener(delegate (BaseEventData d) { OverlayLog.SafeRun(boundary, "hover", enter); });
        trigger.triggers.Add(on);
        EventTrigger.Entry off = new EventTrigger.Entry();
        off.eventID = EventTriggerType.PointerExit;
        off.callback.AddListener(delegate (BaseEventData d) { OverlayLog.SafeRun(boundary, "unhover", exit); });
        trigger.triggers.Add(off);
    }

    public void OnClick(GameObject go, string what, OverlayLog.Fn action)
    {
        if (go == null) return;
        EventTrigger trigger = go.GetComponent<EventTrigger>();
        if (trigger == null) trigger = go.AddComponent<EventTrigger>();
        OverlayLog boundary = Log;
        EventTrigger.Entry e = new EventTrigger.Entry();
        e.eventID = EventTriggerType.PointerClick;
        e.callback.AddListener(delegate (BaseEventData d) { OverlayLog.SafeRun(boundary, what, action); });
        trigger.triggers.Add(e);
    }

    // ---- category bar (host-level, full width) ----

    public CategoryBar CategoryBar(List<string> ids, List<string> labels, List<string> hints, IdFn onSelect)
    {
        CategoryBar bar = new CategoryBar();
        UIDynamic pad = Pad(false, FoliageFXConfig.TabBarH);
        if (pad == null) return bar;
        bar.Root = pad.gameObject;
        TrackHostItem(pad);
        Image bg = pad.gameObject.GetComponent<Image>();
        if (bg == null) bg = pad.gameObject.AddComponent<Image>();
        bg.color = FoliageFXConfig.Surface;
        bg.raycastTarget = false;

        Transform strip = Strip(pad.transform, "FoliageFXCategories", FoliageFXConfig.BarGap, FoliageFXConfig.BarInset, FoliageFXConfig.BarInset);
        for (int i = 0; i < ids.Count; i++)
        {
            string id = ids[i];
            string label = i < labels.Count ? labels[i] : id;
            string hint = i < hints.Count ? hints[i] : "";
            AddCategoryButton(strip, bar, id, label, hint, onSelect);
        }
        return bar;
    }

    void AddCategoryButton(Transform parent, CategoryBar bar, string id, string label, string hint, IdFn onSelect)
    {
        GameObject go = new GameObject("cat_" + id);
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = FoliageFXConfig.TabIdle;
        img.raycastTarget = true;
        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.None;

        GameObject col = new GameObject("labels");
        col.transform.SetParent(go.transform, false);
        RectTransform colRt = col.AddComponent<RectTransform>();
        colRt.anchorMin = Vector2.zero;
        colRt.anchorMax = Vector2.one;
        colRt.offsetMin = new Vector2(4f, 4f);
        colRt.offsetMax = new Vector2(-4f, -4f);
        VerticalLayoutGroup v = col.AddComponent<VerticalLayoutGroup>();
        v.spacing = 0f;
        v.childAlignment = TextAnchor.MiddleCenter;
        v.childForceExpandHeight = false;
        v.childForceExpandWidth = true;

        Text tx = FreeLabel(col.transform, label, 28, true, TextAnchor.MiddleCenter, FoliageFXConfig.OnTabIdle);
        Text hintTx = FreeLabel(col.transform, hint, 15, false, TextAnchor.MiddleCenter, FoliageFXConfig.OnTabIdle);

        bar.Ids.Add(id);
        bar.Bg.Add(img);
        bar.Label.Add(tx);
        bar.Hint.Add(hintTx);
        string catId = id;
        OverlayLog boundary = Log;
        btn.onClick.AddListener(delegate { OverlayLog.SafeRun(boundary, "select category " + catId, delegate { onSelect(catId); }); });
        int slot = bar.Ids.Count - 1;
        OnHover(go, delegate { bar.Hover(slot, true); }, delegate { bar.Hover(slot, false); });
    }

    // ---- rack ----

    // A small header between groups of rack rows ("CONTACT", "BODY").
    public UIDynamic RackHeader(string text, bool right, List<GameObject> bag)
    {
        UIDynamic pad = Pad(right, FoliageFXConfig.RackHeaderH);
        if (pad == null) return null;
        Text tx = MakeLabel(pad.transform, text.ToUpperInvariant(), 18, FontStyle.Bold, FoliageFXConfig.Caption, TextAnchor.LowerLeft, false);
        tx.rectTransform.offsetMin = new Vector2(6f, 2f);
        Track(pad, bag, right);
        return pad;
    }

    // A subtitle inside a card: between the card's title (32) and a row label
    // (24), in the title's colour, under a thin rule that separates it from
    // the group above. For a card that holds several families of controls
    // (the brush card's Maps and View, the Map Presets card's features).
    public UIDynamic SubHeader(string text, bool right, List<GameObject> bag)
    {
        UIDynamic pad = Pad(right, FoliageFXConfig.SubHeaderH);
        if (pad == null) return null;
        GameObject rule = new GameObject("rule");
        rule.transform.SetParent(pad.transform, false);
        Image line = rule.AddComponent<Image>();
        Color c = FoliageFXConfig.Caption;
        line.color = new Color(c.r, c.g, c.b, 0.25f);
        line.raycastTarget = false;
        RectTransform rr = line.rectTransform;
        rr.anchorMin = new Vector2(0f, 1f);
        rr.anchorMax = new Vector2(1f, 1f);
        rr.offsetMin = new Vector2(4f, -8f);
        rr.offsetMax = new Vector2(-4f, -6f);
        Text tx = MakeLabel(pad.transform, text, 26, FontStyle.Bold, FoliageFXConfig.Title, TextAnchor.LowerLeft, false);
        tx.rectTransform.offsetMin = new Vector2(6f, 2f);
        Track(pad, bag, right);
        return pad;
    }

    // One feature in the rack: a select button (flexible), a live state label,
    // and, for features with a master, an on/off toggle bound as the storable's
    // second toggle (the inspector owns the first). A storable has one such
    // alternate toggle; a dashboard row (bindAlt false) wires its toggle by
    // hand instead and syncs it on refresh.
    public RackRow RackRow(string id, string label, JSONStorableBool master, List<GameObject> bag, IdFn onSelect)
    {
        return RackRow(id, label, master, bag, onSelect, true);
    }

    public RackRow RackRow(string id, string label, JSONStorableBool master, List<GameObject> bag, IdFn onSelect, bool bindAlt)
    {
        RackRow row = new RackRow();
        row.Id = id;
        row.Master = master;
        UIDynamic pad = Pad(false, FoliageFXConfig.RowH);
        if (pad == null) return row;
        row.Root = pad.gameObject;
        Track(pad, bag, false);
        Transform strip = Strip(pad.transform, "rack_" + id, 6f, 0f, 0f);

        row.Select = Button(strip, label, FoliageFXConfig.TabIdle, FoliageFXConfig.OnTabIdle);
        if (row.Select != null)
        {
            LayoutElement le = row.Select.gameObject.GetComponent<LayoutElement>();
            if (le != null) { le.flexibleWidth = 1f; le.preferredWidth = 220f; le.minWidth = 140f; }
            string rowId = id;
            OverlayLog boundary = Log;
            row.Select.button.onClick.AddListener(delegate { OverlayLog.SafeRun(boundary, "select " + rowId, delegate { onSelect(rowId); }); });
            RackRow hoverRow = row;
            OnHover(row.Select.gameObject, delegate { hoverRow.Hovered = true; Repaint(hoverRow); },
                delegate { hoverRow.Hovered = false; Repaint(hoverRow); });
        }

        GameObject stateGo = new GameObject("state");
        stateGo.transform.SetParent(strip, false);
        LayoutElement sle = stateGo.AddComponent<LayoutElement>();
        sle.preferredWidth = 190f;
        sle.minWidth = 150f;
        sle.flexibleWidth = 0f;
        row.State = FreeLabel(stateGo.transform, "", 20, false, TextAnchor.MiddleRight, FoliageFXConfig.Caption);
        // The state stays inside its own box whatever it says: it wraps within
        // the box, shrinks its type to fit, and clips rather than running left
        // into the label button when the column is narrow.
        row.State.horizontalOverflow = HorizontalWrapMode.Wrap;
        row.State.verticalOverflow = VerticalWrapMode.Truncate;
        row.State.resizeTextForBestFit = true;
        row.State.resizeTextMinSize = 13;
        row.State.resizeTextMaxSize = 20;
        // The text fills its box up to a right margin, so its right edge and
        // the badge's right edge are the same line.
        RectTransform srt = row.State.rectTransform;
        srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
        srt.offsetMin = new Vector2(0f, 0f); srt.offsetMax = new Vector2(-FoliageFXConfig.RowStateMargin, 0f);
        // A dependency ("needs Contact Dent") is a badge, not a sentence: an
        // amber "!" at the right of the box, the sentence on hover.
        GameObject badge = new GameObject("warn");
        badge.transform.SetParent(stateGo.transform, false);
        RectTransform brt = badge.AddComponent<RectTransform>();
        brt.anchorMin = new Vector2(1f, 0.5f);
        brt.anchorMax = new Vector2(1f, 0.5f);
        brt.pivot = new Vector2(1f, 0.5f);
        brt.sizeDelta = new Vector2(26f, 26f);
        brt.anchoredPosition = new Vector2(-FoliageFXConfig.RowStateMargin, 0f);
        Image bimg = badge.AddComponent<Image>();
        bimg.color = FoliageFXConfig.Warn;
        bimg.raycastTarget = true;
        Text bt = FreeLabel(badge.transform, "!", 20, true, TextAnchor.MiddleCenter, FoliageFXConfig.OnWarn);
        RectTransform btr = bt.rectTransform;
        btr.anchorMin = Vector2.zero; btr.anchorMax = Vector2.one; btr.offsetMin = Vector2.zero; btr.offsetMax = Vector2.zero;
        row.Warn = badge;
        badge.SetActive(false);
        RackRow warnRow = row;
        OnHover(badge, delegate { ShowTooltip(warnRow.WarnText, warnRow.Warn.transform); }, delegate { HideTooltip(); });

        if (master != null && script.manager != null && script.manager.configurableTogglePrefab != null)
        {
            GameObject tgo = Object.Instantiate(script.manager.configurableTogglePrefab.gameObject);
            tgo.transform.SetParent(strip, false);
            ContentSizeFitter fit = tgo.GetComponent<ContentSizeFitter>();
            if (fit != null) Object.DestroyImmediate(fit);
            LayoutElement tle = tgo.GetComponent<LayoutElement>();
            if (tle == null) tle = tgo.AddComponent<LayoutElement>();
            tle.ignoreLayout = false;
            tle.flexibleWidth = 0f;
            tle.preferredWidth = 84f;
            tle.minWidth = 84f;
            tle.minHeight = 44f;
            tle.preferredHeight = 44f;
            UIDynamicToggle dyn = tgo.GetComponent<UIDynamicToggle>();
            if (dyn != null)
            {
                dyn.label = "";
                PaintToggle(dyn, FoliageFXConfig.TabIdle, FoliageFXConfig.OnTabIdle);
                // A rack row's switch also opens the feature, so switching
                // something on lands the user on its settings (a click, not a
                // programmatic change: scene loads and Experimental do not navigate).
                if (bindAlt && dyn.toggle != null)
                {
                    string clickId = id;
                    OnClick(dyn.toggle.gameObject, "open " + id, delegate { onSelect(clickId); });
                }
                if (bindAlt) master.toggleAlt = dyn.toggle;
                else if (dyn.toggle != null)
                {
                    dyn.toggle.isOn = master.val;
                    JSONStorableBool target = master;
                    OverlayLog boundary = Log;
                    dyn.toggle.onValueChanged.AddListener(delegate (bool v)
                    {
                        OverlayLog.SafeRun(boundary, "toggle " + id, delegate { if (target.val != v) target.val = v; });
                    });
                }
                row.Toggle = dyn;
            }
        }
        return row;
    }

    public void PaintRow(RackRow row, bool selected)
    {
        PaintRow(row, selected, false);
    }

    // Experimental rows carry the muted violet so they read apart from the
    // settled ones wherever they appear (rack and dashboard); a row under the
    // pointer lifts one step so it reads as clickable.
    public void PaintRow(RackRow row, bool selected, bool experimental)
    {
        if (row == null) return;
        row.Selected = selected;
        row.Experimental = experimental;
        Repaint(row);
    }

    void Repaint(RackRow row)
    {
        if (row == null || row.Select == null) return;
        Color bg;
        if (row.Selected) bg = row.Hovered ? FoliageFXConfig.Accent : FoliageFXConfig.AccentPressed;
        else if (row.Hovered) bg = row.Experimental ? FoliageFXConfig.RowHoverExperimental : FoliageFXConfig.RowHover;
        else bg = row.Experimental ? FoliageFXConfig.Selected : FoliageFXConfig.TabIdle;
        Paint(row.Select, bg, row.Selected ? FoliageFXConfig.OnAccent : FoliageFXConfig.OnTabIdle);
    }

    // ---- bags and routing ----

    public static void SetBag(List<GameObject> bag, bool on)
    {
        if (bag == null) return;
        for (int i = 0; i < bag.Count; i++)
        {
            GameObject go = bag[i];
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }
    }

    // Host-level element: the category bar and the split panes themselves.
    public void TrackHostItem(UIDynamic d)
    {
        if (d == null || d.gameObject == null) return;
        if (!HostItems.Contains(d.gameObject)) HostItems.Add(d.gameObject);
    }

    // ---- internals ----

    Transform Strip(Transform parent, string name, float spacing, float padX, float padY)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent, false);
        RectTransform rt = row.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, padY);
        rt.offsetMax = new Vector2(-padX, -padY);
        HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = spacing;
        h.padding = new RectOffset(0, 0, 0, 0);
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleCenter;
        return row.transform;
    }

    void Well(GameObject go, float spacing, TextAnchor align, bool expandHeight, Color color)
    {
        Image bg = go.GetComponent<Image>();
        if (bg == null) bg = go.AddComponent<Image>();
        bg.color = color;
        bg.raycastTarget = false;
        VerticalLayoutGroup v = go.GetComponent<VerticalLayoutGroup>();
        if (v == null) v = go.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.padding = new RectOffset(FoliageFXConfig.WellPadX, FoliageFXConfig.WellPadX, FoliageFXConfig.WellPadTop, FoliageFXConfig.WellPadBot);
        v.childAlignment = align;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = expandHeight;
    }

    Text FreeLabel(Transform parent, string text, int size, bool bold, TextAnchor align, Color color)
    {
        GameObject tg = new GameObject("label");
        tg.transform.SetParent(parent, false);
        Text tx = tg.AddComponent<Text>();
        tx.text = text;
        tx.font = Font;
        tx.fontSize = size;
        tx.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        tx.alignment = align;
        tx.color = color;
        tx.raycastTarget = false;
        return tx;
    }

    Text MakeLabel(Transform parent, string text, int size, FontStyle style, Color color, TextAnchor align, bool wrap)
    {
        GameObject go = new GameObject("label");
        go.transform.SetParent(parent, false);
        Text tx = go.AddComponent<Text>();
        tx.text = text;
        tx.font = Font;
        tx.fontSize = size;
        tx.fontStyle = style;
        tx.color = color;
        tx.alignment = align;
        tx.raycastTarget = false;
        tx.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
        tx.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform rt = tx.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(2f, 0f);
        rt.offsetMax = new Vector2(-2f, 0f);
        return tx;
    }

    // Visibility bag plus routing: into the open card, else into the open
    // split pane's column, else host-level.
    void Track(UIDynamic d, List<GameObject> bag, bool right)
    {
        if (d == null || d.gameObject == null) return;
        if (bag != null) bag.Add(d.gameObject);
        if (Section != null)
        {
            if (!Section.Items.Contains(d.gameObject)) Section.Items.Add(d.gameObject);
            owner[d.gameObject] = Section;
            return;
        }
        if (Split == null) { TrackHostItem(d); return; }
        List<GameObject> col = right ? Split.RightItems : Split.LeftItems;
        if (!col.Contains(d.gameObject)) col.Add(d.gameObject);
    }
}

// One feature that scales a card's settings, or reads a map: which feature
// (a chip opens it), its label, and its state for the chip text ("" when it
// is in force, else "off" or "empty").
sealed class Influence
{
    public delegate string StateFn();
    public string FeatureId;
    public string Label;
    public StateFn State;

    public Influence(string featureId, string label, StateFn state)
    {
        FeatureId = featureId;
        Label = label;
        State = state;
    }
}

sealed class InfluenceClause
{
    public string Verb;
    public readonly List<Influence> Items = new List<Influence>();
}

sealed class LiveLine
{
    public GameObject Root;
    public Text Text;
    public UiKit.LineFn Fn;
}

sealed class InfluenceRow
{
    public GameObject Root;
    public Text Text;
    public readonly List<InfluenceClause> Clauses = new List<InfluenceClause>();

    // "Scaled by Tissue Depth (off) and Rigidity." per clause, one clause per
    // line. Hidden features are left out; a clause with nothing left says so
    // rather than going blank.
    public string Compose(UiKit.HiddenFn hidden)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        List<string> names = new List<string>();
        for (int c = 0; c < Clauses.Count; c++)
        {
            InfluenceClause clause = Clauses[c];
            names.Clear();
            for (int i = 0; i < clause.Items.Count; i++)
            {
                Influence inf = clause.Items[i];
                if (hidden != null && hidden(inf.FeatureId)) continue;
                string state = inf.State != null ? inf.State() : "";
                names.Add(string.IsNullOrEmpty(state) ? inf.Label : inf.Label + " (" + state + ")");
            }
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(clause.Verb).Append(' ');
            if (names.Count == 0) { sb.Append("nothing that is shown."); continue; }
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) sb.Append(i == names.Count - 1 ? (names.Count > 2 ? ", and " : " and ") : ", ");
                sb.Append(names[i]);
            }
            sb.Append('.');
        }
        return sb.ToString();
    }
}

// A pad holding one or more wrapped labels (a caption, a hint box's lines).
// Apply measures each label at its rendered width and sets the line and pad
// heights to what the text needs, never below the reserve.
sealed class FitBox
{
    public UIDynamic Pad;
    public float MinHeight;    // the pad never shrinks below this
    public float Extra;        // padding above and below the lines
    public float Gap;          // between lines
    readonly List<Text> texts = new List<Text>();
    readonly List<LayoutElement> lines = new List<LayoutElement>();   // per-line element, or null when the pad is the line

    public void Add(Text tx, LayoutElement line)
    {
        texts.Add(tx);
        lines.Add(line);
    }

    public bool Apply()
    {
        bool changed = false;
        float total = Extra;
        int counted = 0;
        for (int i = 0; i < texts.Count; i++)
        {
            Text tx = texts[i];
            if (tx == null) continue;
            LayoutElement le = lines[i];
            if (le != null && !le.gameObject.activeSelf) continue;   // a hidden line takes no room
            float have = le != null ? le.preferredHeight : 0f;
            // Before the first layout pass the rect has no width and the
            // measure would wrap every word; keep the estimate until it does.
            float width = tx.rectTransform.rect.width;
            float need = have;
            if (width > 20f)
            {
                float pref = tx.preferredHeight;
                // A line that ends right at the edge can measure one line
                // shorter than it is drawn (the brush card's "Mirror paints
                // both sides of the body at once."), and its row then spills
                // into its neighbours, every tick alike. Take the line count
                // it is drawn with when that is more.
                int measured = tx.cachedTextGeneratorForLayout.lineCount;
                int drawn = tx.cachedTextGenerator.lineCount;
                if (measured > 0 && drawn > measured) pref = pref * drawn / measured;
                need = Mathf.Ceil(pref) + 6f;
                if (le != null && need < FoliageFXConfig.LineH) need = FoliageFXConfig.LineH;
            }
            if (le != null && Mathf.Abs(need - have) > 1f)
            {
                le.minHeight = need;
                le.preferredHeight = need;
                changed = true;
            }
            if (counted > 0) total += Gap;
            total += need;
            counted++;
        }
        if (total < MinHeight) total = MinHeight;
        LayoutElement padLe = Pad.gameObject.GetComponent<LayoutElement>();
        float current = padLe != null ? padLe.preferredHeight : Pad.height;
        if (Mathf.Abs(total - current) > 1f)
        {
            Pad.height = total;
            if (padLe == null) padLe = Pad.gameObject.AddComponent<LayoutElement>();
            padLe.minHeight = total;
            padLe.preferredHeight = total;
            changed = true;
        }
        return changed;
    }
}

// A card: one functionality area's controls on a dark well. Its height is
// computed from its visible children, like a split pane's columns.
sealed class UiSection
{
    public string Title;
    public Text TitleText;
    public UIDynamic Pad;
    public readonly List<GameObject> Items = new List<GameObject>();

    public GameObject Root { get { return Pad != null ? Pad.gameObject : null; } }

    public void Layout()
    {
        if (Pad == null) return;
        UiHost.ParentItems(Items, Pad.transform);
    }

    public void Size()
    {
        if (Pad == null || Pad.gameObject == null) return;
        float hgt = UiHost.ColumnHeight(Items, FoliageFXConfig.CardGap);
        hgt += FoliageFXConfig.CardPadTop + FoliageFXConfig.CardPadBot;
        if (hgt < 8f) hgt = 8f;
        LayoutElement le = Pad.gameObject.GetComponent<LayoutElement>();
        if (le != null && Mathf.Abs(Pad.height - hgt) < 1f && Mathf.Abs(le.minHeight - hgt) < 1f) return;
        Pad.height = hgt;
        if (le == null) le = Pad.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.minHeight = hgt;
        le.preferredHeight = hgt;
        le.flexibleHeight = 0f;
    }
}

// Painted state of the category bar.
sealed class CategoryBar
{
    public GameObject Root;
    public readonly List<string> Ids = new List<string>();
    public readonly List<Image> Bg = new List<Image>();
    public readonly List<Text> Label = new List<Text>();
    public readonly List<Text> Hint = new List<Text>();
    string active;
    int hovered = -1;

    public void Paint(string activeId)
    {
        active = activeId;
        for (int i = 0; i < Ids.Count; i++)
        {
            bool on = Ids[i] == activeId;
            bool hover = i == hovered;
            if (Bg[i] != null) Bg[i].color = on ? (hover ? FoliageFXConfig.Accent : FoliageFXConfig.AccentPressed)
                : (hover ? FoliageFXConfig.RowHover : FoliageFXConfig.TabIdle);
            if (Label[i] != null) Label[i].color = on ? FoliageFXConfig.OnAccent : FoliageFXConfig.OnTabIdle;
            if (Hint[i] != null) Hint[i].color = on ? FoliageFXConfig.OnAccentMuted : FoliageFXConfig.Caption;
        }
    }

    public void Hover(int slot, bool on)
    {
        hovered = on ? slot : (hovered == slot ? -1 : hovered);
        Paint(active);
    }
}

// The widgets of one rack row.
sealed class RackRow
{
    public string Id;
    public GameObject Root;
    public JSONStorableBool Master;
    public UIDynamicButton Select;
    public Text State;
    public GameObject Warn;          // the "!" badge; shown for a "needs X" state
    public string WarnText;          // its hover sentence
    public UIDynamicToggle Toggle;
    public bool Selected;
    public bool Experimental;
    public bool Hovered;

    // A state that begins with "needs " is a dependency: the badge shows and
    // the sentence goes to its tooltip; anything else is printed as the state.
    public void SetState(string s)
    {
        if (State == null) return;
        bool needs = s != null && s.StartsWith("needs ", System.StringComparison.Ordinal);
        if (needs)
        {
            string rest = s.Substring(6);
            // "needs Contact Dent" is a feature to switch on; "needs a hand touching"
            // is already a sentence.
            WarnText = rest.StartsWith("a ", System.StringComparison.Ordinal) || rest.StartsWith("an ", System.StringComparison.Ordinal)
                ? "Needs " + rest + "." : "Needs " + rest + " switched on.";
            if (State.text.Length > 0) State.text = "";
        }
        else if (State.text != (s ?? "")) State.text = s ?? "";
        if (Warn != null && Warn.activeSelf != needs) Warn.SetActive(needs);
    }

    // A badge with its own sentence, for a state that is not a dependency
    // (a tree another FoliageFX on that atom looks after): the state text
    // stays, the badge shows beside it.
    public void SetBadge(string tooltip)
    {
        bool on = !string.IsNullOrEmpty(tooltip);
        if (on) WarnText = tooltip;
        if (Warn != null && Warn.activeSelf != on) Warn.SetActive(on);
    }

    // Dashboard rows are not the storable's alternate toggle; mirror it.
    public void Sync()
    {
        if (Toggle == null || Toggle.toggle == null || Master == null) return;
        if (Toggle.toggle.isOn != Master.val) Toggle.toggle.isOn = Master.val;
    }
}
