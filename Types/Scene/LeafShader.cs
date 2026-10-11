using MeshVR;
using MVR.FileManagementSecure;
using UnityEngine;

// Loads FoliageFX.assetbundle next to the cslist through VaM's AssetLoader
// queue and finds its one shader by name. Without it nothing is converted:
// every leaf keeps its original material, and Error says why in words the
// Overview shows. One request per load and one release per request (Release,
// on destroy): VaM counts the holders of a bundle path and unloads it at
// zero, and Unity keeps one copy of a bundle's contents per session, so a
// copy still held from another folder or package makes this load fail until
// that one is let go.
sealed class LeafShader
{
    readonly MVRScript script;
    readonly OverlayLog log;
    bool queued;
    bool answered;   // VaM processed the request (and counted this holder), loaded or not
    bool released;   // Release ran; an answer that arrives later hands the count back at once
    string path;

    public Shader Shader;
    public bool Loaded;
    public bool Failed;
    public string Error;

    public LeafShader(MVRScript script, OverlayLog log)
    {
        this.script = script;
        this.log = log;
    }

    public void Request()
    {
        if (queued) return;
        queued = true;
        string dir = PluginFiles.PluginDir(script);
        if (string.IsNullOrEmpty(dir))
        {
            Fail("The plugin's folder could not be found, so the leaf shader cannot load and leaves stay as they are. Remove FoliageFX and add it again.");
            return;
        }
        string candidate = dir + "/" + FoliageFXConfig.BundleFileName;
        if (!FileManagerSecure.FileExists(candidate))
        {
            Fail("No " + FoliageFXConfig.BundleFileName + " next to the cslist, so leaves stay as they are. Reinstall the package to restore it.");
            return;
        }
        path = candidate;
        OverlayLog.SafeRun(log, "queue the asset bundle", delegate
        {
            AssetLoader.AssetBundleFromFileRequest req = new AssetLoader.AssetBundleFromFileRequest();
            req.path = path;
            req.callback = OnBundle;
            AssetLoader.QueueLoadAssetBundleFromFile(req);
        });
    }

    // Hands this holder's count back to VaM; at zero VaM unloads the bundle
    // and the shader with it, so every material made from it must be gone
    // first (Lifecycle.Release runs before this on destroy).
    public void Release()
    {
        if (released || path == null) return;
        released = true;
        Shader = null;
        Loaded = false;
        if (!answered) return;   // the answer will release when it arrives
        OverlayLog.SafeRun(log, "release the asset bundle", delegate { AssetLoader.DoneWithAssetBundleFromFile(path); });
    }

    void OnBundle(AssetLoader.AssetBundleFromFileRequest r)
    {
        answered = true;
        if (released)
        {
            OverlayLog.SafeRun(log, "release the asset bundle", delegate { AssetLoader.DoneWithAssetBundleFromFile(path); });
            return;
        }
        OverlayLog.SafeRun(log, "read the asset bundle", delegate
        {
            if (r == null || r.assetBundle == null)
            {
                Fail(FoliageFXConfig.BundleFileName + " did not load, so leaves stay as they are. If FoliageFX also ran from another "
                    + "folder or package in this session (the loose folder and a var, or two vars), VaM still holds that copy of the "
                    + "bundle: remove that plugin, then reload this one. Otherwise reinstall the package.");
                return;
            }
            Shader[] shaders = OverlayLog.Probe(delegate { return r.assetBundle.LoadAllAssets<Shader>(); }) as Shader[];
            Shader found = null;
            if (shaders != null)
                for (int i = 0; i < shaders.Length && found == null; i++)
                    if (shaders[i] != null && shaders[i].name == FoliageFXConfig.LeafShaderName) found = shaders[i];
            if (found == null)
            {
                Fail(FoliageFXConfig.BundleFileName + " has no " + FoliageFXConfig.LeafShaderName
                    + " shader, so leaves stay as they are. The bundle is from another version: reinstall the package.");
                return;
            }
            if (!found.isSupported)
            {
                Fail("The leaf shader does not run on this graphics card, so leaves stay as they are.");
                return;
            }
            Shader = found;
            Loaded = true;
            log.Info("Loaded the leaf shader.");
        });
    }

    void Fail(string error)
    {
        Failed = true;
        Error = error;
        log.Warn(error);
    }

    // One line for the diagnostics report.
    public string State()
    {
        if (Failed) return "failed: " + Error;
        if (path == null) return queued ? "absent" : "not requested";
        if (released) return "released";
        if (!answered) return "loading";
        return Loaded ? "loaded: " + FoliageFXConfig.LeafShaderName : "loaded, no shader";
    }
}
