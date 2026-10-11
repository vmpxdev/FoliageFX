using MVR.FileManagementSecure;
using SimpleJSON;

// Which build this is, in two parts. The release number is
// FoliageFXConfig.Version: "dev" in the repository, stamped to the var number
// ("3") by the packager, so a release can never carry a hand-edited version.
// The build identity (branch, commit, modified) comes from build.json beside
// the cslist: a git hook writes it into the loose folder after every commit
// and checkout, the packager writes it into a dev var only. A release var
// carries its number and nothing else. Missing file = unknown.
sealed class BuildInfo
{
    public const string Dev = "dev";
    public const string FileName = "build.json";

    public static string Branch = "";
    public static string Commit = "";
    public static string Date = "";
    public static bool Dirty;
    public static int NextRelease;   // the release this development build is building (0: unknown)
    public static bool Loaded;

    public static bool IsRelease { get { return FoliageFXConfig.Version != Dev; } }

    public static void Load(MVRScript script, OverlayLog log)
    {
        Loaded = false; Branch = ""; Commit = ""; Date = ""; Dirty = false; NextRelease = 0;
        OverlayLog.SafeRun(log, "read " + FileName, delegate
        {
            string dir = PluginFiles.PluginDir(script);
            if (dir == null) return;
            string path = dir + "/" + FileName;
            if (!FileManagerSecure.FileExists(path)) return;
            JSONNode n = Settings.ParseBytes(FileManagerSecure.ReadAllBytes(path));
            if (n == null) return;
            Branch = n["branch"] != null ? n["branch"].Value : "";
            Commit = n["commit"] != null ? n["commit"].Value : "";
            Date = n["date"] != null ? n["date"].Value : "";
            Dirty = n["dirty"] != null && n["dirty"].AsBool;
            NextRelease = n["release"] != null ? n["release"].AsInt : 0;
            Loaded = true;
        });
    }

    // The Overview line after the brand:
    //   "Version 3"
    //   "Development Build  ·  main abc1234  ·  modified"
    public static string Line()
    {
        if (IsRelease) return "Version " + FoliageFXConfig.Version;
        string dot = "  " + ((char)0x00B7) + "  ";
        string s = "Development Build";
        if (Loaded && (Branch.Length > 0 || Commit.Length > 0)) s += dot + (Branch + " " + Commit).Trim();
        if (Loaded && Dirty) s += dot + "modified";
        return s;
    }

    // What scenes, logs, and Diagnose record: "3", or "dev 3 main abc1234
    // modified" from a development build of release 3 (the release being
    // built: the newest vN tag plus one, SaveHistory.Current), or "dev 3"
    // when nothing else is known. The release dates a development build's
    // scene the way a release's number does.
    public static string Stamp()
    {
        if (IsRelease) return FoliageFXConfig.Version;
        bool dev;
        string s = Dev + " " + SaveHistory.Current(out dev);
        if (Loaded && (Branch.Length > 0 || Commit.Length > 0)) s += " " + (Branch + " " + Commit).Trim();
        if (Loaded && Dirty) s += " modified";
        return s;
    }
}
