using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Status + VaM log helper, one copy for the whole plugin. Info/Warn/Error/ErrorOnce/Diagnostic plus
// Run/RunOnce/RunWarn/SafeRun/Probe so call sites never write a raw try/catch
// and every failure carries a specific reason.
sealed class OverlayLog
{
    public delegate void Fn();
    public delegate object ProbeFn();

    // A once-key is the call-site text alone, never the exception message: a
    // hot-path throw whose message carries a changing number (a vertex index,
    // a time) would otherwise add a fresh key every frame and never suppress.
    // The list is capped and drops the oldest, so it cannot grow without end
    // and the linear scan stays short.
    const int OnceKeyMax = 64;

    readonly JSONStorableString status;
    readonly List<string> onceKeys = new List<string>();
    // The last lines, oldest first, for the diagnostics report: what happened
    // before the problem. Repeats of the previous line are not kept.
    readonly List<string> history = new List<string>();
    readonly float started = Time.realtimeSinceStartup;
    string lastKept;
    // The text of the last error reported (with the exception message), for
    // the fault record a switched-off feature keeps.
    public string LastReport;
    Text view;
    Color viewColor = FoliageFXConfig.Caption;
    string lastStatus;
    string lastWarn;

    public OverlayLog(JSONStorableString statusField)
    {
        status = statusField;
    }

    public string Current { get { return lastStatus ?? ""; } }

    // Every line VaM logs carries the build stamp, so a pasted log names the version.
    static string Prefix() { return FoliageFXConfig.ProductName + " " + BuildInfo.Stamp() + ": "; }

    public void Info(string text)
    {
        SetStatus(text, FoliageFXConfig.Caption);
        Keep("info", text);
    }

    // Warning: shown in the focus color and logged once per distinct text.
    public void Warn(string text)
    {
        SetStatus(text, FoliageFXConfig.Focus);
        Keep("warn", text);
        if (text == lastWarn) return;
        lastWarn = text;
        SuperController.LogMessage(Prefix() + text);
    }

    public void Error(string text)
    {
        Report(text, null, false);
    }

    public void Error(string text, Exception e)
    {
        Report(text, e, false);
    }

    public void ErrorOnce(string text)
    {
        Report(text, null, true);
    }

    // The diagnostics report into VaM's Message Log (the selectable one); the
    // status well is left to the caller, the report being many lines.
    public void Diagnostic(string text)
    {
        SuperController.LogMessage(FoliageFXConfig.ProductName + " Diagnose:\n" + text);
    }

    void Keep(string kind, string text)
    {
        if (string.IsNullOrEmpty(text) || text == lastKept) return;
        lastKept = text;
        history.Add((Time.realtimeSinceStartup - started).ToString("F1") + "s " + kind + ": " + text);
        if (history.Count > FoliageFXConfig.LogHistory) history.RemoveAt(0);
    }

    // The kept lines, one per line, oldest first.
    public void History(System.Text.StringBuilder sb)
    {
        for (int i = 0; i < history.Count; i++) sb.Append(history[i]).Append('\n');
    }

    void Report(string text, Exception e, bool once)
    {
        string full = e != null ? text + " - " + e.Message : text;
        LastReport = full;
        SetStatus(full, FoliageFXConfig.Danger);
        Keep("error", full);
        if (once && Seen(text)) return;
        if (once) Remember(text);
        string body = full;
        if (e != null && !string.IsNullOrEmpty(e.StackTrace))
            body = full + "\n" + e.StackTrace;
        SuperController.LogError(Prefix() + body);
    }

    bool Seen(string key)
    {
        for (int i = 0; i < onceKeys.Count; i++)
        {
            if (onceKeys[i] == key) return true;
        }
        return false;
    }

    // The oldest key goes when the cap is reached: an old call site earning a
    // second log line is a far smaller harm than a list that never stops.
    void Remember(string key)
    {
        onceKeys.Add(key);
        if (onceKeys.Count > OnceKeyMax) onceKeys.RemoveAt(0);
    }

    // User action: report every failure.
    public bool Run(string what, Fn action)
    {
        try { action(); return true; }
        catch (Exception e) { Error(what, e); return false; }
    }

    // Hot path: a stuck throw cannot flood the log.
    public bool RunOnce(string what, Fn action)
    {
        try { action(); return true; }
        catch (Exception e) { Report(what, e, true); return false; }
    }

    // The same, for a call site whose text is two parts ("contact dent" +
    // "apply"). The parts are joined only when something throws, so the
    // guarded call itself allocates no string per frame.
    public bool RunOnce(string what, string part, Fn action)
    {
        try { action(); return true; }
        catch (Exception e) { Report(what + " " + part, e, true); return false; }
    }

    // Recoverable: status only, no error log.
    public bool RunWarn(string what, Fn action)
    {
        try { action(); return true; }
        catch (Exception e) { Warn(what + " (" + e.Message + ")"); return false; }
    }

    public static void SafeRun(OverlayLog log, string what, Fn action)
    {
        if (log != null) { log.Run(what, action); return; }
        try { action(); }
        catch (Exception e) { SuperController.LogError(Prefix() + what + " - " + e.Message); }
    }

    // Optional API probe: a throw means "not available", never an error.
    public static object Probe(ProbeFn fn)
    {
        try { return fn(); }
        catch (Exception) { return null; }
    }

    void SetStatus(string text, Color color)
    {
        if (text == null) text = "";
        viewColor = color;
        if (status != null && text == lastStatus && view != null && view.text == text)
            return;
        lastStatus = text;
        if (status != null) status.valNoCallback = text;
        PaintView();
    }

    void PaintView()
    {
        if (view == null) return;
        view.text = lastStatus ?? "";
        view.color = viewColor;
    }
}
