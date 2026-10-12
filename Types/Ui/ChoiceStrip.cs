using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// A row of buttons standing in for a chooser: one cell per id, the chosen one
// painted in the rack-row scheme (pressed accent, idle tab, hover lift). Used
// where a popup would hide what the choice is, such as a material's Mode, and
// where its list would open under the cards below it.
// Built from UiKit buttons only; repaints on change, never per tick.
sealed class ChoiceStrip
{
    public delegate void Fn();
    public Fn OnPick;   // after a click changed the value
    // The cell painted as chosen when the stored value is not the one in
    // effect (a hidden choice standing in for another); null = the value.
    public string Effective;

    readonly UiKit kit;
    readonly JSONStorableStringChooser js;
    readonly List<string> ids = new List<string>();
    readonly List<UIDynamicButton> cells = new List<UIDynamicButton>();
    readonly List<bool> hovered = new List<bool>();
    string paintedFor;   // value + hover state the cells were last painted for

    public ChoiceStrip(UiKit kit, Transform row, JSONStorableStringChooser js, List<string> ids, List<string> labels)
    {
        this.kit = kit;
        this.js = js;
        for (int i = 0; i < ids.Count; i++)
        {
            string id = ids[i];
            string label = labels != null && i < labels.Count ? labels[i] : id;
            UIDynamicButton b = kit.Button(row, label, FoliageFXConfig.TabIdle, FoliageFXConfig.OnTabIdle);
            if (b == null) continue;
            // A label wider than its cell shrinks its type to fit rather than
            // wrapping and losing its tail ("Keep Origi"), whatever the column width.
            Text tx = b.buttonText;
            if (tx != null)
            {
                tx.resizeTextMaxSize = tx.fontSize;
                tx.resizeTextMinSize = Mathf.Min(FoliageFXConfig.StripTextMin, tx.fontSize);
                tx.resizeTextForBestFit = true;
            }
            int slot = this.ids.Count;
            this.ids.Add(id);
            cells.Add(b);
            hovered.Add(false);
            kit.Wire(b, "pick " + id, delegate
            {
                if (js.val != id) js.val = id;
                Repaint();
                if (OnPick != null) OnPick();
            });
            kit.OnHover(b.gameObject, delegate { hovered[slot] = true; Repaint(); }, delegate { hovered[slot] = false; Repaint(); });
        }
        Repaint();
    }

    // Paint every cell for the current value and hover; a no-op when nothing changed.
    public void Repaint()
    {
        string v = Effective ?? (js != null ? js.val : null);
        string key = v ?? "";
        for (int i = 0; i < hovered.Count; i++) if (hovered[i]) key += "|" + i;
        if (key == paintedFor) return;
        paintedFor = key;
        for (int i = 0; i < cells.Count; i++)
        {
            bool on = ids[i] == v;
            Color bg = on ? (hovered[i] ? FoliageFXConfig.Accent : FoliageFXConfig.AccentPressed)
                : (hovered[i] ? FoliageFXConfig.RowHover : FoliageFXConfig.TabIdle);
            kit.Paint(cells[i], bg, on ? FoliageFXConfig.OnAccent : FoliageFXConfig.OnTabIdle);
        }
    }

    // Show or hide one cell; the row's layout closes the gap. True when it changed.
    public bool SetShown(string id, bool on)
    {
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] != id) continue;
            GameObject g = cells[i] != null ? cells[i].gameObject : null;
            if (g == null || g.activeSelf == on) return false;
            g.SetActive(on);
            return true;
        }
        return false;
    }
}
