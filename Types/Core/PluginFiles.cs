using System;
using MVR.FileManagementSecure;
using SimpleJSON;

// The plugin's own folder and the one folder it writes to. Settings travel in
// the scene and nowhere else, so the only file FoliageFX writes is a Diagnose
// report, under Saves/PluginData/Vampix Collections/FoliageFX, through
// FileManagerSecure only.
sealed class PluginFiles
{
    public const string DiagnosticsFolder = FoliageFXConfig.DataFolder + "/Diagnostics";

    // True for a path inside a var ("Creator.Package.1:/...").
    public static bool IsPackaged(string path)
    {
        return !string.IsNullOrEmpty(path) && path.IndexOf(":/", StringComparison.Ordinal) > 0;
    }

    // The plugin's own folder, VaM-relative, from the manager's plugin list.
    public static string PluginDir(MVRScript script)
    {
        return OverlayLog.Probe(delegate
        {
            string nm = script.name;
            int us = nm.IndexOf('_');
            if (us < 0) return (string)null;
            string id = nm.Substring(0, us);
            JSONClass pj = script.manager.GetJSON()["plugins"] as JSONClass;
            if (pj == null) return (string)null;
            string file = pj[id].Value;
            if (string.IsNullOrEmpty(file)) return (string)null;
            int slash = file.LastIndexOfAny(new char[] { '/', '\\' });
            return slash >= 0 ? file.Substring(0, slash) : file;
        }) as string;
    }

    public static void EnsureDir(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return;
        if (!FileManagerSecure.DirectoryExists(dir)) FileManagerSecure.CreateDirectory(dir);
    }
}
