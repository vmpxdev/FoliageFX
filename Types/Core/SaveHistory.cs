using System.Globalization;

// What each release changed in saved data: which settings are new since a
// scene was saved (they sit at their defaults until changed), which keys this
// version no longer reads, and which values now mean something else. Kept
// from the first release on, so the record is whole on the day a load has to
// tell a user what a scene from another version means here. Immutable data:
// Current reads it, and the packager refuses a release with a line past its
// tag; nothing writes it.
//
// One line per change, in release order, in the same commit as the change:
//   Look / Scene    a saved setting added in that release
//   Panel           a Ui-scope key added (panel state; it dates a scene)
//   Removed         a key no longer read, with why (null: panel state)
//   Changed         keys whose meaning changed with no rewrite: the scene's
//                   values still apply, and Text says what they do now.
//                   `Marker` is a key that arrived with the change: a scene
//                   that carries it was saved after the change.
// A key that is renamed or rewritten goes through Settings.MigrateStep (and
// a schema bump) instead. Volatile keys are never saved and get no line.
sealed class SaveLine
{
    public int Release;
    public int Kind;
    public string Key;        // a param key
    public string Text;       // Removed: why; Changed: what the values do now
    public string Marker;     // Changed: a key saved only after the change
    public string[] Keys;     // Changed: the keys whose meaning changed
}

sealed class SaveHistory
{
    public const int KindLook = 1;
    public const int KindScene = 2;
    public const int KindPanel = 3;
    public const int KindRemoved = 4;
    public const int KindChanged = 5;

    // The first release adds nothing: everything it saves is the baseline the
    // lines of later releases are counted from.
    public static readonly SaveLine[] Lines = new SaveLine[] {
    };

    static SaveLine Line(int release, int kind, string key, string text)
    {
        SaveLine l = new SaveLine();
        l.Release = release; l.Kind = kind; l.Key = key; l.Text = text;
        return l;
    }

    static SaveLine Look(int release, string key) { return Line(release, KindLook, key, null); }
    static SaveLine Scene(int release, string key) { return Line(release, KindScene, key, null); }
    static SaveLine Panel(int release, string key) { return Line(release, KindPanel, key, null); }
    static SaveLine Removed(int release, string key, string why) { return Line(release, KindRemoved, key, why); }

    static SaveLine Changed(int release, string marker, string text, string[] keys)
    {
        SaveLine l = Line(release, KindChanged, null, text);
        l.Marker = marker; l.Keys = keys;
        return l;
    }

    // The newest release with a line, or 1 when there is none.
    public static int Newest()
    {
        int n = 1;
        for (int i = 0; i < Lines.Length; i++) if (Lines[i].Release > n) n = Lines[i].Release;
        return n;
    }

    // This build's release, as the stamp writes it: the var number in a
    // release build; in a development build the release being built, with
    // `dev` true: the newest vN tag behind it plus one (build.json; every save
    // after a release tag counts toward the next version), else the newest
    // release the history names, else 1.
    public static string Current(out bool dev)
    {
        if (BuildInfo.IsRelease)
        {
            dev = false;
            return FoliageFXConfig.Version;
        }
        dev = true;
        int n = BuildInfo.NextRelease > 0 ? BuildInfo.NextRelease : Newest();
        return n.ToString(CultureInfo.InvariantCulture);
    }
}
