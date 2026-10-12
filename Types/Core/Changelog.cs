// What's New: the release notes the Overview shows, newest first, and the
// text the packager puts in the var's description. One Entry per release,
// keyed by the var number: the release build for v3 refuses to package
// until the entry for 3 exists, so the notes are never forgotten (the packager
// looks for the entry's opening by its text, so no comment here spells it).
// Docs are never shipped; this is the user-facing text.
//
// Every entry is a plain list of features, one short line each, as plain
// text (the var's description shows it as typed, so no tags): "- " and
// what the user can do or gets. No history and no internals. The
// Overview's Full Changelog modal reads it back as bullets
// (OverviewPanel.ChangelogRows), and would still read a section name on a
// line above its bullets, or a short lead ending in a full stop.
sealed class ChangelogEntry
{
    public int Version;
    public string Title;
    public string Body;
}

sealed class Changelog
{
    public static readonly ChangelogEntry[] Entries = new ChangelogEntry[] {
        Entry(1, "first release",
            "- Shadow fix: see-through leaves on tree and plant CustomUnityAssets cast and receive shadows.\n"
            + "- Soft leaf edges with MSAA on.\n"
            + "- Trees page: every CustomUnityAsset in the scene, its materials, and what was done to each and why.\n"
            + "- Per material: Mode (Auto, Convert, Keep Original), Tint, Hue Shift, Saturation, Brightness, Gloss, Alpha Cutoff, Double-sided, Translucency, Drop Falling Leaves.\n"
            + "- Wind sway on converted mode leaves.\n"
            + "- Falling leaves cut from each tree's own texture."),
    };

    static ChangelogEntry Entry(int version, string title, string body)
    {
        ChangelogEntry e = new ChangelogEntry();
        e.Version = version; e.Title = title; e.Body = body;
        return e;
    }

    public static bool Has(int version)
    {
        for (int i = 0; i < Entries.Length; i++) if (Entries[i].Version == version) return true;
        return false;
    }

    // The entries as one text: "Version 3  ·  title", the body, a blank line.
    public static string Text()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        string dot = "  " + ((char)0x00B7) + "  ";
        for (int i = 0; i < Entries.Length; i++)
        {
            ChangelogEntry e = Entries[i];
            if (i > 0) sb.Append('\n');
            sb.Append("Version ").Append(e.Version);
            if (!string.IsNullOrEmpty(e.Title)) sb.Append(dot).Append(e.Title);
            sb.Append("\n\n").Append((e.Body ?? "").TrimEnd('\n')).Append('\n');
        }
        return sb.ToString();
    }

    // The section that tells the user what updating changes in what they
    // already have: the modal draws it in the warning colour.
    public const string HeadsUp = "Heads Up";
    // The section readers look for first: the modal draws it in green.
    public const string Performance = "Performance";
    // A bullet's first sentence is its lead when it ends within this many
    // characters ("Landed leaf shapes." is 19).
    public const int LeadMax = 32;
}
