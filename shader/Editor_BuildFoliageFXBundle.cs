// Put this file under Assets/Editor/ in a Unity 2018.1.9f1 project (VaM's engine
// line), with the shader under Assets/FoliageFX/. Menu: Vampix Collections > Build
// FoliageFX Bundle. Writes AssetBundles/FoliageFX.assetbundle, ready to copy next to
// FoliageFX.cslist. No tagging and no material assets needed: every .shader in
// Assets/FoliageFX goes into the bundle, and the plugin builds its own materials.
// (Editor script: normal Unity file APIs are fine here; this is not the VaM sandbox.)
//
// VR: VaM renders both eyes in one pass (single pass stereo). Unity compiles the
// leaf shader's stereo variants only while the project has VR on, so the builder
// enables VR with the Single Pass method (Player Settings > XR Settings) and
// reimports the shader before building.
//
// Colour space: VaM renders in linear space, and Unity compiles a shader for the
// building project's colour space (the Standard lighting's constants, and the
// leaf shader's trip to gamma space for its recolouring). The builder sets
// Linear too; a bundle built in a Gamma project lights the leaves wrongly in VaM.
using System.IO;
using UnityEditor;
using UnityEditorInternal.VR;
using UnityEngine;

public class Editor_BuildFoliageFXBundle
{
    const string SourceFolder = "Assets/FoliageFX";
    const string OutputFolder = "AssetBundles";
    const string BundleName = "FoliageFX.assetbundle";

    [MenuItem("Vampix Collections/Build FoliageFX Bundle")]
    public static void Build()
    {
        string[] shaders = Directory.GetFiles(SourceFolder, "*.shader", SearchOption.TopDirectoryOnly);
        if (shaders.Length == 0)
        {
            Debug.LogError("FoliageFX bundle: no .shader files under " + SourceFolder);
            return;
        }
        for (int i = 0; i < shaders.Length; i++) shaders[i] = shaders[i].Replace('\\', '/');

        MatchVaM();
        for (int i = 0; i < shaders.Length; i++) AssetDatabase.ImportAsset(shaders[i], ImportAssetOptions.ForceUpdate);

        AssetBundleBuild build = new AssetBundleBuild();
        build.assetBundleName = BundleName;
        build.assetNames = shaders;

        if (!Directory.Exists(OutputFolder)) Directory.CreateDirectory(OutputFolder);
        BuildPipeline.BuildAssetBundles(
            OutputFolder,
            new AssetBundleBuild[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64);
        Debug.Log("FoliageFX bundle built: " + OutputFolder + "/" + BundleName + " (" + shaders.Length + " shaders)");
    }

    // VaM's colour space and stereo method (single pass, double-wide).
    // Idempotent; logs what it changed.
    static void MatchVaM()
    {
        bool changed = false;
        if (PlayerSettings.colorSpace != ColorSpace.Linear) { PlayerSettings.colorSpace = ColorSpace.Linear; changed = true; }
        if (!PlayerSettings.virtualRealitySupported) { PlayerSettings.virtualRealitySupported = true; changed = true; }
        if (PlayerSettings.stereoRenderingPath != StereoRenderingPath.SinglePass) { PlayerSettings.stereoRenderingPath = StereoRenderingPath.SinglePass; changed = true; }
        string[] devices = VREditor.GetVREnabledDevicesOnTargetGroup(BuildTargetGroup.Standalone);
        if (devices == null || devices.Length == 0)
        {
            VREditor.SetVREnabledDevicesOnTargetGroup(BuildTargetGroup.Standalone, new string[] { "OpenVR", "Oculus" });
            changed = true;
        }
        if (changed) Debug.Log("FoliageFX bundle: linear colour space, VR enabled with Single Pass stereo (VaM's settings).");
    }
}
