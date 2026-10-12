using UnityEngine;

// Product identity, schema, cadences, string keys, and the Vampix UI palette.
// Sealed class with static members: the VaM sandbox forbids static classes and
// enums, so every "enum" in this plugin is a set of string or int constants here.
sealed class FoliageFXConfig
{
    public const string ProductName = "FoliageFX";
    public const string Brand = "Vampix Collections";
    // The release number, the var's number: "dev" here, stamped by the
    // packager ("3") and never edited by hand. BuildInfo reads it.
    public const string Version = "dev";
    // The Overview's credit: where to find more of the author's work. VaM
    // plugins cannot open a browser, so the link is copied to the clipboard.
    public const string PatreonUrl = "https://www.patreon.com/VampixCollections";
    public const string CopyPatreonLabel = "Copy Patreon Link";
    public const string CopiedPatreonLabel = "Copied to Clipboard";
    public const string CreditTagline = "Sponsor a coffee. Accidentally fund a feature.";
    public const float CopiedLabelSeconds = 2.5f;     // the button reads "Copied" this long
    public const float NoticeSeconds = 4f;            // a notice stays up this long, or until clicked
    public const int LogHistory = 30;                 // status lines the diagnostics report replays
    public const int DiagnoseLogChars = 6000;         // how much of a report goes to VaM's Message Log: its text box draws nothing at all past about 16,000 characters in total
    public const float WhatsNewH = 210f;              // the What's New scroll box height

    // Save-schema version. Parameter keys (ParamEntry.Key) are the schema.
    // Adding a parameter never bumps this; a key rename or a change of
    // meaning does, together with a Settings.MigrateStep case.
    public const int Schema = 1;

    public const string DataFolder = "Saves/PluginData/Vampix Collections/FoliageFX";
    public const string BundleFileName = "FoliageFX.assetbundle";
    public const string LeafShaderName = "Vampix Collections/FoliageFX/Leaves";
    public const string NormalMapKeyword = "_NORMALMAP";

    // The atoms this plugin looks after, and how another instance of it on
    // such an atom is recognised (VaM names a plugin's storable plugin#N_<class>).
    public const string TreeAtomType = "CustomUnityAsset";
    public const string StorableSuffix = "_" + ProductName;
    // The looks of the atom the plugin itself sits on are keyed by this, not
    // by its uid, so they survive a rename and travel with the atom.
    public const string SelfTreeKey = "self";

    // Cadences.
    public const float UiRefreshSeconds = 0.5f;      // live text, dashboard, the tree rack
    public const float SceneScanSeconds = 2f;        // new and gone CustomUnityAsset atoms, other instances, MSAA
    public const float SpriteJobTimeoutSeconds = 15f; // a leaf-shape extraction that never answers is dropped and retried

    // Which materials are leaves that never receive shadows (LeafRules).
    // Unity's built-in pipeline never shadows the Transparent queue; a copy on
    // the leaf shader goes into the AlphaTest queue instead.
    public const int TransparentQueueFirst = 2501;   // Geometry ends at 2500; AlphaTest sits at 2450
    public const int OverlayQueueFirst = 4000;       // overlays are never foliage
    public const float SeeThroughAlpha = 0.9f;       // a colour alpha below this is glass or water, not leaves
    public const int LeafQueue = 2450;
    public const string CopySuffix = " (FoliageFX)"; // on the name of every material this plugin made
    public const float StandardFadeMode = 2f;        // the Standard shader's _Mode: 2 Fade, 3 Transparent
    public const string LeafTextureProperty = "_MainTex";

    // What a material's Mode says (LeafLook.Mode), as the chooser's ids.
    public const string ModeAuto = "auto";
    public const string ModeConvert = "convert";
    public const string ModeKeep = "keep";

    // Falling leaves: the atlas the leaf shapes are packed into (LeafSprites)
    // and the particles that carry them (LeafEmitter).
    public const int AtlasSize = 512;
    public const int AtlasGrid = 4;                  // 4 x 4 tiles, one leaf shape each
    public const int AtlasPad = 4;                   // transparent margin inside a tile, in texels
    public const int AtlasMaxShapes = AtlasGrid * AtlasGrid;
    public const int AtlasReadSize = 1024;           // the leaf texture is read at most this big
    // A leaf fills the inner part of its tile, so a particle is scaled up by
    // this for its size setting to be the leaf's.
    public const float AtlasTileScale = (AtlasSize / AtlasGrid) / (float)(AtlasSize / AtlasGrid - AtlasPad * 2);
    // Leaf shapes are connected opaque regions of the texture: not cut by its
    // border, between these shares of its area, not long twigs, reasonably
    // solid, and not much darker than the typical shape (buds, berries, bark).
    public const float LeafAreaMinShare = 0.0003f;
    public const float LeafAreaMinTexels = 40f;
    public const float LeafAreaMaxShare = 0.06f;
    public const float LeafWholeMaxShare = 0.9f;     // past LeafAreaMaxShare, up to this, a region is one whole leaf when nearly convex
    public const float LeafWholeConvexMin = 0.92f;   // that region's area over its convex hull's: whole leaves measured 0.95 and up, a fence block 0.86
    public const float LeafConvexMin = 0.75f;        // a leaf's area over its convex hull's: leaves and buds 0.75 and up, sprigs and blade clumps under
    public const float LeafElongationMin = 0.15f;    // width over length along its axes: narrow leaves 0.2, grass blades 0.06 to 0.13
    public const float LeafHoleShareMax = 0.005f;    // enclosed see-through texels over area: leaves 0 to 0.002, needle clumps 0.003 and up
    public const float LeafFillMax = 0.88f;          // of its box: no leaf fills it like a block of wall or fence
    public const float LeafAspectMin = 0.2f;
    public const float LeafAspectMax = 5f;
    public const float LeafSolidityMin = 0.25f;
    public const float LeafDarkShare = 0.6f;          // of the area-weighted median luminance
    public const int LeafErodeMax = 2;               // the mask is thinned up to this many texels to cut leaves from twigs
    public const int LeafShapesMin = 3;              // fewer small shapes than this (and no whole leaf) are strays: no falling leaves
    public const float LeafClearMin = 0.35f;         // a leaf texture is at least this see-through: leaf textures measured 0.49 and up, an environment atlas 0.26
    public const int LeafParticlesMin = 16;
    public const int LeafParticlesMax = 2000;
    public const float LeafFallPathFactor = 1.6f;    // a leaf's lifetime covers this many times the straight drop
    public const float LeafWindDrift = 1.5f;         // metres per second of drift at wind strength 1
    public const float LeafCollisionRadius = 0.05f;  // of the particle size, so a leaf lands flat on the ground
    public const float LeafRestShrinkAt = 0.8f;      // a resting leaf shrinks away over its last fifth

    // Category ids for the panel bar. Fixed set; trees and effects are rack rows, not tabs.
    public const string PageOverview = "overview";
    public const string PageTrees = "trees";
    public const string PageEffects = "effects";

    // Feature ids: ParamSet keys are "<feature>.<param>", rack rows and the
    // dashboard address features by id. A tree's row id is TreeRowPrefix + uid.
    public const string PluginId = "plugin";
    public const string TreeId = "tree";            // the tree editor's own controls (never saved)
    public const string ShadowFixId = "shadowfix";
    public const string WindId = "wind";
    public const string FallingId = "falling";
    public const string TreeRowPrefix = "tree:";

    // No feature is experimental; the kit's Experimental mechanism is kept for
    // parity with Live Skin, so a feature that ships before it is settled has a home.
    public static readonly string[] ExperimentalFeatures = new string[0];
    public static bool IsExperimental(string id)
    {
        for (int i = 0; i < ExperimentalFeatures.Length; i++) if (ExperimentalFeatures[i] == id) return true;
        return false;
    }
    // Show Hints switch (Overview), set each UI tick: the one-line hints under
    // the controls show while it is on; off, the cards are the controls alone.
    public static bool ShowHints = true;

    // Layout metrics (VaM UI units).
    public const int WellPadX = 12;
    public const int WellPadTop = 10;
    public const int WellPadBot = 12;
    public const float WellTitleH = 40f;
    public const string HintPrefix = "↳  ";   // an elbow with an arrowhead, leading from the control into its hint
    public const float HintIndent = 10f;   // a hint line under a control is indented this much
    public const float HintGapBelow = 18f; // and leaves this much empty below it, so it reads as the control's, not the next one's
    public const float CaptionH = 48f;
    public const float LineH = 30f;       // one wrapped line of 24 px text
    public const int CharsPerLine = 42;   // wrapped 24 px text per column, estimate
    public const float RowH = 50f;
    public const float SubHeaderH = 50f;   // a card's subtitle: a thin rule, then 26 pt bold (UiKit.SubHeader)
    public const float RackHeaderH = 36f;  // small group header between rack rows
    public const int StripTextMin = 16;    // the smallest a choice strip's label shrinks to so it fits its cell
    // Cards: one dark well per functionality area inside an inspector.
    public const int CardPadTop = 8;
    public const int CardPadBot = 12;
    public const float CardGap = 6f;
    public const float CardTitleH = 36f;
    public const float TabBarH = 102f;
    public const float BarInset = 8f;      // strip inset inside the bar background, all sides
    public const float BarGap = 6f;        // gap between category buttons
    // Full-width host (VaM's fullWidthUIContent): our margins, same as Live Decals.
    public const int HostPadX = 4;
    public const int HostPadTop = 4;
    public const int HostPadBottom = 8;
    public const float HostSpacing = 10f;  // between the bar and a pane, and between panes
    public const float Gutter = 10f;       // between the two columns of a pane

    // Palette. Identical to Live Decals and Live Skin so the collection reads as one brand.
    public static readonly Color Accent = new Color(0.486f, 0.227f, 0.929f, 1f);
    public static readonly Color AccentPressed = new Color(0.357f, 0.129f, 0.714f, 1f);
    public static readonly Color OnAccent = Color.white;
    public static readonly Color OnAccentMuted = new Color(0.929f, 0.914f, 0.996f, 1f);
    public static readonly Color Selected = new Color(0.361f, 0.322f, 0.502f, 1f);
    public static readonly Color Warn = new Color(0.98f, 0.76f, 0.22f, 1f);        // the rack row badge
    public static readonly Color OnWarn = new Color(0.16f, 0.12f, 0.04f, 1f);
    public static readonly Color TooltipBg = new Color(0.10f, 0.09f, 0.14f, 0.97f);
    public const float TooltipW = 360f;
    public const float RowStateMargin = 10f;   // right margin shared by a row's state text and its badge
    public const float SwitchOffAlpha = 0.35f;  // a rack row's switch that does nothing here, greyed
    public const float RowLabelMargin = 16f;   // a rack row's label is cut to its button less this, clear of the edges
    public static readonly Color Danger = new Color(0.769f, 0.169f, 0.227f, 1f);
    public static readonly Color OnDanger = Color.white;
    public static readonly Color Button = new Color(0.875f, 0.875f, 0.875f, 1f);
    public static readonly Color OnButton = new Color(0.12f, 0.11f, 0.14f, 1f);
    public static readonly Color TabIdle = new Color(0.227f, 0.216f, 0.267f, 1f);
    public static readonly Color RowHover = new Color(0.361f, 0.322f, 0.502f, 1f);              // idle row or tab under the pointer
    public static readonly Color RowHoverExperimental = new Color(0.475f, 0.424f, 0.635f, 1f); // experimental idle row under the pointer
    public static readonly Color OnTabIdle = new Color(0.93f, 0.91f, 0.95f, 1f);
    public static readonly Color Surface = new Color(0.145f, 0.137f, 0.173f, 1f);
    public static readonly Color PreviewBg = new Color(0.071f, 0.063f, 0.094f, 1f);
    public static readonly Color CardBg = new Color(0.071f, 0.063f, 0.094f, 1f);     // section cards, same well as Live Decals
    public static readonly Color Title = new Color(0.95f, 0.94f, 0.97f, 1f);
    public static readonly Color Caption = new Color(0.78f, 0.76f, 0.82f, 1f);
    public static readonly Color Focus = new Color(0.769f, 0.710f, 0.992f, 1f);
    // The branded modal's row words and rules (the What's New notes).
    public static readonly Color ReportSkipped = new Color(1.00f, 0.47f, 0.47f, 1f);     // lost: soft red
    public static readonly Color ReportChanged = new Color(0.98f, 0.76f, 0.22f, 1f);     // looks different: the Warn amber
    public static readonly Color ReportConverted = new Color(0.769f, 0.710f, 0.992f, 1f); // rewritten: the Focus lavender
    public static readonly Color ReportNew = new Color(0.45f, 0.86f, 0.56f, 1f);         // new: green
    public static readonly Color ReportMuted = new Color(0.58f, 0.56f, 0.64f, 1f);       // labels, pages, releases, an item's ending
    public static readonly Color ReportBand = new Color(0.486f, 0.227f, 0.929f, 0.24f);  // the verdict's band: the accent, faint
    public static readonly Color ReportRule = new Color(0.20f, 0.19f, 0.25f, 1f);        // the hairline between items

    public static float Clamp01(float v)
    {
        if (v < 0f) return 0f;
        if (v > 1f) return 1f;
        return v;
    }
}
