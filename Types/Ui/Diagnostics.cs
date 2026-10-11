using System;
using System.Collections.Generic;
using System.Text;
using MVR.FileManagementSecure;
using UnityEngine;

// The diagnostics report: everything a bug report needs, in sections of one
// fact per line. Diagnose puts it in VaM's Message Log, on the clipboard, and
// in a file under the plugin's data folder. The Trees section is the one a
// report about a tree that looks wrong is read for: every material with its
// shader, its queue, the verdict and why, and what was done to it.
sealed class Diagnostics
{
    readonly MVRScript script;
    readonly Lifecycle life;
    readonly OverlayLog log;
    readonly StringBuilder sb = new StringBuilder(4096);

    public ParamSet Params;

    public Diagnostics(MVRScript script, Lifecycle life, OverlayLog log)
    {
        this.script = script;
        this.life = life;
        this.log = log;
    }

    // The Diagnose button.
    public void Report()
    {
        string text = Build();
        string path = Write(text);
        bool copied = OverlayLog.Probe(delegate { GUIUtility.systemCopyBuffer = text; return (object)true; }) != null;
        log.Diagnostic(ForLog(text, path));
        log.Info("Diagnostics " + (copied ? "copied to the clipboard" : "in the Message Log")
            + (path != null ? " and saved to " + path : "") + ".");
    }

    // The report as VaM's Message Log gets it: whole when short, else its head
    // up to a line break and a line saying where the rest is. VaM draws its
    // whole log in one text box that shows nothing at all once it holds about
    // 16,000 characters, and a scene full of trees writes a long Trees section.
    static string ForLog(string text, string path)
    {
        if (text == null || text.Length <= FoliageFXConfig.DiagnoseLogChars) return text;
        int cut = text.LastIndexOf((char)10, FoliageFXConfig.DiagnoseLogChars);
        if (cut < FoliageFXConfig.DiagnoseLogChars / 2) cut = FoliageFXConfig.DiagnoseLogChars;
        return text.Substring(0, cut) + ((char)10).ToString() + "... " + (text.Length - cut) + " more characters: the whole report is on the clipboard"
            + (path != null ? " and in " + path : "") + ".";
    }

    string Write(string text)
    {
        return OverlayLog.Probe(delegate
        {
            PluginFiles.EnsureDir(PluginFiles.DiagnosticsFolder);
            Atom self = life.Trees != null ? life.Trees.Self : null;
            string who = self != null ? self.uid : "Session";
            StringBuilder safe = new StringBuilder();
            for (int i = 0; i < who.Length; i++)
            {
                char c = who[i];
                safe.Append(char.IsLetterOrDigit(c) ? c : '_');
            }
            string path = PluginFiles.DiagnosticsFolder + "/" + FoliageFXConfig.ProductName + "-" + safe + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt";
            FileManagerSecure.WriteAllText(path, text);
            return path;
        }) as string;
    }

    public string Build()
    {
        sb.Length = 0;
        Line(FoliageFXConfig.ProductName + " diagnostics");
        Section("Build");
        Line("version", BuildInfo.Stamp() + ", schema " + FoliageFXConfig.Schema);
        Line("installed", Installed());
        Line("bundle", life.Shader != null ? life.Shader.State() : "?");
        Line("report", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        Section("Machine");
        Line("gpu", Probe(delegate { return SystemInfo.graphicsDeviceName + ", " + SystemInfo.graphicsMemorySize + " MB, shader level " + SystemInfo.graphicsShaderLevel; }));
        Line("mode", Probe(delegate
        {
            SuperController sc = SuperController.singleton;
            return sc.isOVR ? "VR (Oculus)" : sc.isOpenVR ? "VR (OpenVR)" : "desktop";
        }));
        Line("msaa", Probe(delegate
        {
            int aa = QualitySettings.antiAliasing;
            return aa > 1 ? "x" + aa : "off (soft edges stay hard)";
        }));

        Section("Scene");
        Atom self = life.Trees.Self;
        Line("atom", self != null ? self.uid + " (" + self.type + ")" : "none (session plugin)");
        Line("scene", Probe(delegate
        {
            List<Atom> atoms = SuperController.singleton.GetAtoms();
            int assets = 0, others = 0;
            for (int i = 0; i < atoms.Count; i++)
            {
                Atom a = atoms[i];
                if (a == null) continue;
                if (a.type == FoliageFXConfig.TreeAtomType) assets++;
                if (a != self && TreeRegistry.HasOwnInstance(a)) others++;
            }
            return assets + (assets == 1 ? " CustomUnityAsset, " : " CustomUnityAssets, ") + atoms.Count + " atoms, "
                + others + " other " + FoliageFXConfig.ProductName + " instance" + (others == 1 ? "" : "s")
                + ", freeze " + (SuperController.singleton.freezeAnimation ? "on" : "off");
        }));

        Section("Settings (not default)");
        if (Params != null)
        {
            int at = sb.Length;
            int n = Params.NonDefault(sb, ParamSet.Look | ParamSet.Scene);
            if (n == 0) { sb.Length = at; Line("all at defaults"); }
        }

        // One line per tree, then one per material: what a report about a
        // tree that looks wrong is read for. Probed per tree, so an asset that
        // throws while it is read costs its own lines, not the report.
        Section("Trees");
        List<Tree> trees = life.Trees.Trees;
        if (trees.Count == 0) Line("no CustomUnityAsset atoms");
        for (int i = 0; i < trees.Count; i++)
        {
            Tree t = trees[i];
            string lines = OverlayLog.Probe(delegate { return TreeLines(t); }) as string;
            if (lines != null) sb.Append(lines);
            else Line(t.Uid + ": unavailable");
        }

        Section("Runtime");
        int start = sb.Length;
        life.Describe(sb);
        // The Describe fragments are " | name: facts" joined; one per line here.
        string runtime = sb.ToString(start, sb.Length - start);
        sb.Length = start;
        string[] parts = runtime.Split(new string[] { " | " }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++) Line(parts[i].Trim());

        Section("Recent log");
        log.History(sb);
        return sb.ToString();
    }

    //   Sakura 1: 3 of 5 converted
    //     g2Mat 1: Standard, queue 3000, converted. Standard shader in Fade mode: never receives shadows.
    static string TreeLines(Tree t)
    {
        StringBuilder s = new StringBuilder(256);
        s.Append(t.Uid);
        if (t.Key != t.Uid) s.Append(" [").Append(t.Key).Append(']');
        s.Append(": ").Append(t.State());
        if (!t.Enabled) s.Append(", switched off on the Trees page");
        s.Append('\n');
        for (int i = 0; i < t.Materials.Count; i++)
        {
            LeafMaterial m = t.Materials[i];
            s.Append("  ").Append(m.Name).Append(": ");
            if (m.Source != null && m.Source.shader != null) s.Append(m.Source.shader.name).Append(", queue ").Append(m.Source.renderQueue);
            else s.Append("no source");
            s.Append(", ").Append(m.StateLabel()).Append(". ").Append(m.Reason ?? "");
            if (m.Look != null && m.Look.Customized) s.Append(" Look ").Append(m.Look.Write().ToString());
            s.Append('\n');
        }
        return s.ToString();
    }

    string Installed()
    {
        string dir = PluginFiles.PluginDir(script);
        if (string.IsNullOrEmpty(dir)) return "?";
        if (!PluginFiles.IsPackaged(dir)) return "loose folder " + dir;
        return "package " + dir.Substring(0, dir.IndexOf(":/", StringComparison.Ordinal));
    }

    void Section(string title) { sb.Append('\n').Append('[').Append(title).Append("]\n"); }
    void Line(string text) { sb.Append(text).Append('\n'); }
    void Line(string key, string value) { sb.Append(key).Append(": ").Append(value ?? "?").Append('\n'); }

    static string Probe(OverlayLog.ProbeFn fn)
    {
        string s = OverlayLog.Probe(fn) as string;
        return s ?? "unavailable";
    }
}
