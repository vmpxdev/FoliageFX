// What's New: the release notes the Overview shows, newest first, and the
// text the packager puts in the var's description. One Entry per release,
// keyed by the var number: the release build for v3 refuses to package
// until the entry for 3 exists, so the notes are never forgotten (the packager
// looks for the entry's opening by its text, so no comment here spells it).
// Docs are never shipped; this is the user-facing text.
//
// Every entry is written the same way, as plain text (the var's description
// shows it as typed, so no tags):
//   one sentence that says what the release is
//   then these sections, in this order, each left out when it has nothing:
//     Heads Up       what updating changes in what the user already has: saves
//                    converted, a default moved, a setting gone, a look that differs
//     New Features   what can be done now that could not before; a rework goes
//                    here, as what the user can now do
//     Performance    what costs less than it did, on the GPU or the CPU. Its own
//                    section because it is what readers look for first; a figure
//                    goes in only when it was measured for the build that ships
//                    and says what it measured ("the leaves' GPU time, in my
//                    tests"), never a frame rate promise
//     Adjustments    what behaves or looks differently
//     Bugfixes       what was broken
//     Known Issues   what ships broken and is known
//   then the sign-off.
// A section is its name on a line, then "- " bullets. A bullet opens with a
// short lead and a full stop naming the thing ("Falling leaves. Leaves cut
// from each tree's own texture..."), then one or two sentences; about five
// bullets to a section, related items merged. Thanks go on the bullet that
// earned them. The Overview's Full Changelog modal reads this shape back
// (OverviewPanel.ChangelogRows): a line with a bullet right under it is a
// section, a bullet's first sentence is its bold lead when it ends within
// LeadMax characters.
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
            "The first release of FoliageFX: the leaves of your tree and plant CustomUnityAssets stop glowing in the shade and take shadows like the rest of the scene.\n\n"
            + "New Features\n"
            + "- Shadows on leaves. Many tree assets draw their leaves see-through, and those never receive shadows in VaM, so they stay lit in the shade and seem to glow. FoliageFX converts them into leaves that cast and receive shadows, and switching it off brings the asset's own materials back.\n"
            + "- The Trees page. Every CustomUnityAsset in the scene is listed there, each material with what was done to it and why. Set a material to Convert when Auto missed it, or to Keep original to leave it as it was.\n"
            + "- Recolouring. Tint, Hue Shift, Saturation and Brightness on each material, to turn a summer tree autumn without touching the asset. Copy a look to the whole tree, or to the same material on every tree.\n"
            + "- Wind. A gentle sway on every converted leaf, with its own strength, speed and direction.\n"
            + "- Falling leaves. Leaves cut from each tree's own texture drop from the canopy, drift with the wind and lie on the ground for a while. Each material on the Trees page chooses whether its leaves fall.\n"
            + "- Soft edges. With MSAA on in VaM's settings, converted leaves keep smooth outlines instead of the hard, jagged edge of a cutout.\n\n"
            + "Known Issues\n"
            + "- Uneven ground. Falling leaves land on a flat plane at the tree's base, not on uneven ground, so on a slope they can float above it or sink into it.\n"
            + "- Landed leaf shapes. A falling leaf may land as a different shape from the one that fell.\n\n"
            + "This is the first release, so treat everything here as experimental to some degree. Tree and plant assets are built in many different ways and I could only try so many of them: if one looks wrong, the Trees page shows what was done to each material and lets you change it.\n\n"
            + "Keep Show Hints on for a short description of what each setting does.\n\n"
            + "If you run into any unexpected behaviour or bugs or you have a suggestion, feel free to share your thoughts with me.\n\n"
            + "Thank you for trying out FoliageFX! Can't wait to see what you create using it."),
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
