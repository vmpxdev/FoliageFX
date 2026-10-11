// Renders CustomUnityAsset tree bundles before and after FoliageFX's leaf
// conversion, so a change to the leaf shader can be checked without VaM. Each
// tree stands on a grey ground under the sun, with a roof between the sun and
// one half of the canopy, far enough up the light's ray to stay out of the
// picture. Before, the alpha-blended leaves under the roof are as bright as the
// ones in the sun, since nothing shadows the Transparent queue; after, they
// darken with the ground around them.
//
// Run by tools/render/render.ps1, in batch mode:
//   -executeMethod Editor_RenderTrees.Run -shaderBundle <FoliageFX.assetbundle>
//   -outDir <folder> -trees "<bundle>::<prefab>::<label>|..."
// writes <label>_before.png and <label>_after.png for each tree.
//
// The conversion is the plugin's with its defaults: LeafRules' verdicts with
// Convert Other Shaders off, ShadowFix's copy, and an untouched look with soft
// edges on (the render has MSAA). It is written out here so the tool needs
// nothing from VaM; a change to those rules belongs here too.
// (Editor script: normal Unity file APIs are fine here; this is not the VaM sandbox.)
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class Editor_RenderTrees
{
    const string LeafShaderName = "Vampix Collections/FoliageFX/Leaves";
    const int Width = 960;
    const int Height = 720;
    const int Samples = 8;

    // FoliageFXConfig's numbers for LeafRules and the copy.
    const int TransparentQueueFirst = 2501;
    const int OverlayQueueFirst = 4000;
    const float SeeThroughAlpha = 0.9f;
    const float StandardFadeMode = 2f;
    const int LeafQueue = 2450;
    const string CopySuffix = " (FoliageFX)";

    public static void Run()
    {
        string shaderPath = Arg("-shaderBundle");
        string outDir = Arg("-outDir");
        string trees = Arg("-trees");
        if (shaderPath == null || outDir == null || trees == null)
            throw new ArgumentException("RenderTrees: Run takes -shaderBundle <bundle> -outDir <folder> -trees \"<bundle>::<prefab>::<label>|...\"");
        Directory.CreateDirectory(outDir);

        // VaM renders in linear space, and the bundle's shader is compiled for it.
        if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;

        AssetBundle shaderBundle = AssetBundle.LoadFromFile(shaderPath);
        if (shaderBundle == null) throw new Exception("RenderTrees: could not load " + shaderPath);
        Shader leaves = null;
        Shader[] shaders = shaderBundle.LoadAllAssets<Shader>();
        for (int i = 0; i < shaders.Length; i++) if (shaders[i].name == LeafShaderName) leaves = shaders[i];
        if (leaves == null) throw new Exception("RenderTrees: " + shaderPath + " holds no " + LeafShaderName);
        if (!leaves.isSupported) throw new Exception("RenderTrees: " + LeafShaderName + " is not supported on this GPU");
        Debug.Log("RenderTrees: shader " + leaves.name);

        string[] specs = trees.Split('|');
        for (int i = 0; i < specs.Length; i++)
        {
            string[] parts = specs[i].Split(new string[] { "::" }, StringSplitOptions.None);
            if (parts.Length < 2) throw new ArgumentException("RenderTrees: a tree is <bundle>::<prefab>::<label>, got " + specs[i]);
            string label = parts.Length > 2 && parts[2].Length > 0 ? parts[2] : Path.GetFileNameWithoutExtension(parts[1]);
            RenderTree(parts[0], parts[1], label, leaves, outDir);
        }
        shaderBundle.Unload(true);
    }

    static void RenderTree(string bundlePath, string prefabPath, string label, Shader leaves, string outDir)
    {
        AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
        if (bundle == null) throw new Exception("RenderTrees: could not load " + bundlePath);
        GameObject prefab = bundle.LoadAsset<GameObject>(prefabPath);
        if (prefab == null)
        {
            string names = string.Join(", ", bundle.GetAllAssetNames());
            bundle.Unload(true);
            throw new Exception("RenderTrees: no " + prefabPath + " in " + bundlePath + " (it holds " + names + ")");
        }

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject tree = UnityEngine.Object.Instantiate(prefab);
        Renderer[] renderers = tree.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) throw new Exception("RenderTrees: " + prefabPath + " has no renderers");
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        Camera cam = BuildStage(bounds);

        Save(cam, Path.Combine(outDir, label + "_before.png"));
        List<Material> copies = Convert(renderers, leaves);
        string[] copied = new string[copies.Count];
        for (int i = 0; i < copies.Count; i++) copied[i] = copies[i].name;
        Debug.Log("RenderTrees: " + label + ": " + copies.Count + " converted (" + string.Join(", ", copied) + ")");
        Save(cam, Path.Combine(outDir, label + "_after.png"));

        UnityEngine.Object.DestroyImmediate(tree);
        for (int i = 0; i < copies.Count; i++) UnityEngine.Object.DestroyImmediate(copies[i]);
        bundle.Unload(true);
    }

    // The sun, the ground, the roof and the camera around a tree's bounds.
    static Camera BuildStage(Bounds b)
    {
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
        QualitySettings.shadowCascades = 4;
        QualitySettings.shadowProjection = ShadowProjection.StableFit;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.16f, 0.18f, 0.22f);
        RenderSettings.skybox = null;
        RenderSettings.fog = false;
        float size = b.size.magnitude;
        QualitySettings.shadowDistance = size * 6f;

        GameObject sunGo = new GameObject("Sun");
        Light sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.96f, 0.9f);
        sun.intensity = 1.3f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 1f;
        sunGo.transform.rotation = Quaternion.Euler(50f, 35f, 0f);

        Material grey = new Material(Shader.Find("Standard"));
        grey.color = new Color(0.45f, 0.45f, 0.45f);
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.position = new Vector3(b.center.x, b.min.y, b.center.z);
        ground.transform.localScale = Vector3.one * size;
        ground.GetComponent<Renderer>().sharedMaterial = grey;

        // Split across the light, so its shadow falls on one half of the
        // canopy whichever way the tree is built.
        GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roof.GetComponent<Renderer>().sharedMaterial = grey;
        roof.transform.rotation = sunGo.transform.rotation;
        roof.transform.localScale = new Vector3(size * 2f, size * 4f, 0.2f);
        roof.transform.position = b.center - sunGo.transform.forward * size * 2.5f - sunGo.transform.right * size;

        GameObject camGo = new GameObject("Camera");
        Camera cam = camGo.AddComponent<Camera>();
        cam.renderingPath = RenderingPath.Forward;
        cam.allowHDR = false;
        cam.allowMSAA = true;
        cam.stereoTargetEye = StereoTargetEyeMask.None;
        cam.fieldOfView = 40f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = size * 20f;
        Vector3 view = Quaternion.Euler(8f, 0f, 0f) * Vector3.forward;
        float dist = b.extents.magnitude / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        camGo.transform.position = b.center - view * dist;
        camGo.transform.rotation = Quaternion.LookRotation(view);
        return cam;
    }

    // Every slot holding a leaf that never receives shadows takes the copy;
    // one copy per source material, as in the plugin.
    static List<Material> Convert(Renderer[] renderers, Shader leaves)
    {
        Dictionary<Material, Material> copyOf = new Dictionary<Material, Material>();
        List<Material> made = new List<Material>();
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer renderer = renderers[r];
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
            Material[] slots = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!IsShadowlessLeaf(slots[i], leaves)) continue;
                Material copy;
                if (!copyOf.TryGetValue(slots[i], out copy))
                {
                    copy = MakeCopy(slots[i], leaves);
                    copyOf[slots[i]] = copy;
                    made.Add(copy);
                }
                slots[i] = copy;
                changed = true;
            }
            if (changed) renderer.sharedMaterials = slots;
        }
        return made;
    }

    // LeafRules.Judge with Convert Other Shaders off.
    static bool IsShadowlessLeaf(Material m, Shader leaves)
    {
        if (m == null || m.shader == null || m.shader == leaves || m.name.EndsWith(CopySuffix)) return false;
        if (m.renderQueue < TransparentQueueFirst || m.renderQueue >= OverlayQueueFirst) return false;
        if (!m.HasProperty("_MainTex") || m.GetTexture("_MainTex") == null) return false;
        if (m.HasProperty("_Color") && m.GetColor("_Color").a < SeeThroughAlpha) return false;
        string s = m.shader.name;
        if (s.StartsWith("Standard")) return m.HasProperty("_Mode") && m.GetFloat("_Mode") >= StandardFadeMode;
        return s.Contains("Transparent/") || s.StartsWith("Nature/");
    }

    // ShadowFix's copy with LeafLook's defaults: the tree's own material on
    // the leaf shader, its texture and values carried over by the Standard names.
    static Material MakeCopy(Material source, Shader leaves)
    {
        bool standard = source.shader.name.StartsWith("Standard");
        bool bump = source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap") != null
            && (!standard || source.IsKeywordEnabled("_NORMALMAP"));
        Texture2D main = source.GetTexture("_MainTex") as Texture2D;
        Material m = new Material(source);
        m.name = source.name + CopySuffix;
        m.shader = leaves;
        m.shaderKeywords = bump ? new string[] { "_NORMALMAP" } : new string[0];
        m.SetOverrideTag("RenderType", "TransparentCutout");
        m.renderQueue = LeafQueue;
        m.SetFloat("_MipCoverage", main != null && main.mipmapCount > 1 ? 0.25f : 0f);
        m.SetFloat("_HueShift", 0f);
        m.SetFloat("_Saturation", 1f);
        m.SetFloat("_Brightness", 1f);
        m.SetFloat("_Cutoff", 0.5f);
        m.SetFloat("_Glossiness", source.HasProperty("_Glossiness") ? source.GetFloat("_Glossiness") : 0.2f);
        m.SetFloat("_Translucency", 0f);
        m.SetFloat("_Cull", 0f);
        m.SetFloat("_SoftEdges", 1f);
        m.SetFloat("_WindStrength", 0f);
        return m;
    }

    static void Save(Camera cam, string path)
    {
        RenderTexture rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        rt.antiAliasing = Samples;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        Texture2D image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(image);
        Debug.Log("RenderTrees: wrote " + path);
    }

    static string Arg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return null;
    }
}
