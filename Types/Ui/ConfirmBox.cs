using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// A modal confirm in the plugin's own look, for a destructive action such as
// a save that would replace an existing file. VaM's media browser hands back
// a path without ever warning that it exists, and VaM's built-in AlertUser is
// styled as VaM, not as this collection, so this is a branded card: a dimmed
// backdrop, a title, a message, and Cancel / confirm buttons on our palette.
//
// One instance is reused; Show swaps the text and callbacks and raises it,
// Hide lowers it. The overlay is parented to the panel's scroll viewport, so
// it is fixed to the visible panel (it does not scroll with the content) and
// is destroyed with the panel, leaking nothing when the plugin is removed.
// The dim backdrop and the card are siblings: a click on the dim area cancels,
// a click on the card body is absorbed, and the two buttons carry the choice.
// Neither the backdrop nor the card is a Selectable: their clicks come through
// ModalClick, so a hover highlight that rings Selectables (VR Comfort's Hover
// Glow) rings the buttons alone, and nothing treats the whole card as a button.
//
// The same card doubles as a notice (Notice): the message alone, no buttons,
// a lighter backdrop, gone by itself after a moment or on any click; and as a
// report (InformRows): an alert whose message is a table of rows (a load
// report's summary) instead of one text.
sealed class ConfirmBox
{
    readonly UiKit kit;
    GameObject root;
    GameObject buttons;
    Image dimImage;
    Text titleText;
    Text messageText;
    MaxHeight msgCap;   // caps the message well's height; past it the text scrolls
    UIDynamicButton confirmBtn;
    UIDynamicButton cancelBtn;
    OverlayLog.Fn onConfirm;
    OverlayLog.Fn onCancel;
    ScrollRect msgScroll;
    SmoothScroll msgSmooth;
    bool relayoutPending;     // a table raised while hidden: laid out on the first tick it shows
    RectTransform cardRect;
    RectTransform rowsRoot;   // InformRows: the table, in the well in place of the message
    readonly List<GameObject> rowObjects = new List<GameObject>();
    float noticeUntil;   // realtime the notice lowers itself; 0 while a confirm is up
    ModalClick cardClick;   // a notice's dismiss on the card body; off otherwise

    public ConfirmBox(UiKit kit) { this.kit = kit; }

    public bool Showing { get { return root != null && root.activeSelf; } }

    // An alert or a confirm is up (a passing notice is not): a report waiting
    // to be shown must not replace it.
    public bool Busy { get { return root != null && root.activeSelf && noticeUntil <= 0f; } }

    // The plugin's panel is open, so a card raised now is seen (an Alert into
    // a panel never built shows nothing).
    public bool CanShow
    {
        get
        {
            Transform parent = ResolveParent();
            return parent != null && parent.gameObject.activeInHierarchy;
        }
    }

    // Raise the confirm. `confirm` runs on the confirm button; `cancel` (may
    // be null) on Cancel or the backdrop. With no panel to parent to there is
    // nobody to ask, and the question is always "may I destroy this?": the
    // cancel branch runs instead, so an unanswerable prompt never overwrites a
    // file or resets a look behind the user's back (the panel is gone once the
    // plugin is removed, while VaM's own file dialog may still be open).
    public void Show(string title, string message, string confirmLabel, OverlayLog.Fn confirm, OverlayLog.Fn cancel)
    {
        Transform parent = ResolveParent();
        Build(parent);
        if (root == null) { if (cancel != null) OverlayLog.SafeRun(kit.Log, "cancel", cancel); return; }
        onConfirm = confirm;
        onCancel = cancel;
        noticeUntil = 0f;
        SetText(title, message);
        if (buttons != null && !buttons.activeSelf) buttons.SetActive(true);
        // Alert and InformRows hide Cancel; a real confirm brings it back.
        if (cancelBtn != null && cancelBtn.gameObject != null && !cancelBtn.gameObject.activeSelf) cancelBtn.gameObject.SetActive(true);
        if (dimImage != null) dimImage.color = new Color(0f, 0f, 0f, 0.66f);
        if (confirmBtn != null) { confirmBtn.label = confirmLabel; kit.Paint(confirmBtn, FoliageFXConfig.Danger, FoliageFXConfig.OnDanger); Hover(confirmBtn, FoliageFXConfig.Danger); }
        SetMsgCap();
        root.transform.SetAsLastSibling();
        Raise();
    }

    // A blocking alert in the plugin's look: a title, a message, and a single
    // button that dismisses it. Unlike a Notice it never times out - a failure
    // the user has to read stays up until acknowledged (or a backdrop click).
    // The matching VaM error log keeps the record after it is dismissed.
    public void Alert(string title, string message, string dismissLabel)
    {
        Transform parent = ResolveParent();
        Build(parent);
        if (root == null) return;
        onConfirm = null;
        onCancel = null;
        noticeUntil = 0f;   // no auto-lower; it waits for the click
        SetText(title, message);
        if (buttons != null && !buttons.activeSelf) buttons.SetActive(true);
        if (cancelBtn != null && cancelBtn.gameObject != null) cancelBtn.gameObject.SetActive(false);
        if (dimImage != null) dimImage.color = new Color(0f, 0f, 0f, 0.66f);
        if (confirmBtn != null) { confirmBtn.label = dismissLabel; kit.Paint(confirmBtn, FoliageFXConfig.Accent, FoliageFXConfig.OnAccent); Hover(confirmBtn, FoliageFXConfig.Accent); }
        SetMsgCap();
        root.transform.SetAsLastSibling();
        Raise();
    }

    // An Alert whose message is a table (a load report's summary): one
    // button, or a backdrop click, dismisses it.
    public void InformRows(string title, List<ReportRow> rows, string dismissLabel)
    {
        Transform parent = ResolveParent();
        Build(parent);
        if (root == null) return;
        onConfirm = null;
        onCancel = null;
        noticeUntil = 0f;
        SetText(title, "");
        UseRows(true);
        FillRows(rows);
        if (buttons != null && !buttons.activeSelf) buttons.SetActive(true);
        if (cancelBtn != null && cancelBtn.gameObject != null) cancelBtn.gameObject.SetActive(false);
        if (dimImage != null) dimImage.color = new Color(0f, 0f, 0f, 0.66f);
        if (confirmBtn != null) { confirmBtn.label = dismissLabel; kit.Paint(confirmBtn, FoliageFXConfig.Accent, FoliageFXConfig.OnAccent); Hover(confirmBtn, FoliageFXConfig.Accent); }
        SetMsgCap();
        root.transform.SetAsLastSibling();
        Raise();
        Relayout();
    }

    // A passing notice ("Loaded belly.json onto Rigidity."): the card with
    // the message and no buttons, lowered after `seconds` (on the UI tick) or
    // on any click. A confirm that is up is never replaced by a notice.
    public void Notice(string title, string message, float seconds)
    {
        Transform parent = ResolveParent();
        Build(parent);
        if (root == null) return;
        if (root.activeSelf && noticeUntil <= 0f) return;
        onConfirm = null;
        onCancel = null;
        noticeUntil = Time.unscaledTime + Mathf.Max(0.2f, seconds);
        SetText(title, message);
        if (buttons != null && buttons.activeSelf) buttons.SetActive(false);
        if (dimImage != null) dimImage.color = new Color(0f, 0f, 0f, 0.25f);
        SetMsgCap();
        root.transform.SetAsLastSibling();
        Raise();
    }

    // UI tick: a notice past its time goes away.
    public void Tick()
    {
        if (noticeUntil > 0f && root != null && root.activeSelf && Time.unscaledTime >= noticeUntil) Hide();
        if (relayoutPending && rowsRoot != null && rowsRoot.gameObject.activeInHierarchy) Relayout();
    }

    // A click on the card body dismisses a notice; a confirm or alert (noticeUntil 0)
    // absorbs it, leaving the choice to the buttons or the backdrop.
    void OnCardClick()
    {
        if (noticeUntil > 0f) Hide();
    }

    // Cap the message well from the visible frame's height, so the whole card
    // (title + well + buttons) fits and only the well scrolls when it must.
    void SetMsgCap()
    {
        if (msgCap == null || root == null) return;
        float maxH = 560f;
        RectTransform pr = root.transform.parent as RectTransform;
        if (pr != null && pr.rect.height > 200f) maxH = Mathf.Max(240f, pr.rect.height - 320f);
        msgCap.Max = maxH;
    }

    void SetText(string title, string message)
    {
        if (titleText != null)
        {
            titleText.text = title ?? "";
            GameObject tg = titleText.gameObject;
            bool show = !string.IsNullOrEmpty(title);
            if (tg.activeSelf != show) tg.SetActive(show);
        }
        if (messageText != null)
        {
            messageText.text = message ?? "";
            // A new message starts at its top, not where the last one was scrolled.
            if (msgScroll != null) msgScroll.StopMovement();
            if (msgSmooth != null) msgSmooth.Stop();
            messageText.rectTransform.anchoredPosition = Vector2.zero;
        }
        UseRows(false);
    }

    // Up, with the card taking clicks only while it is a notice: a confirm,
    // an alert or a report leaves the card body without a click handler, so
    // a click there does nothing and the buttons and the backdrop decide.
    void Raise()
    {
        if (cardClick != null) cardClick.enabled = noticeUntil > 0f;
        root.SetActive(true);
    }

    public void Hide()
    {
        onConfirm = null;
        onCancel = null;
        noticeUntil = 0f;
        if (cardClick != null) cardClick.enabled = false;
        if (root != null && root.activeSelf) root.SetActive(false);
    }

    // The visible frame the card centres in: the plugin's own scroll viewport,
    // fixed as the content scrolls, so the card stays on screen wherever the
    // page is scrolled - not the scrolling content, which would centre it in the
    // whole page height. The search is bounded to the plugin's own UI (the
    // MVRScriptUI root), so the card never lands in VaM's outer pages when the
    // plugin's own UI is not the one open. The clip mask between the content and
    // that root is the viewport; with none found, the content itself (scoped,
    // if not perfectly centred). Null before the panel is built.
    Transform ResolveParent()
    {
        Transform tip = kit.TooltipRoot;
        if (tip == null) return null;
        MVRScriptUI ui = tip.GetComponentInParent<MVRScriptUI>();
        Transform bound = ui != null ? ui.transform : null;
        for (Transform t = tip; t != null; t = t.parent)
        {
            if (t.GetComponent<RectMask2D>() != null || t.GetComponent<Mask>() != null) return t;
            if (t == bound) break;   // do not climb past the plugin's own UI
        }
        return tip;
    }

    void Build(Transform parent)
    {
        if (parent == null) return;
        if (root != null && root.transform.parent != parent) { Object.Destroy(root); root = null; }
        if (root != null) return;

        root = new GameObject("FoliageFXConfirm");
        root.transform.SetParent(parent, false);
        Stretch(root.AddComponent<RectTransform>());
        LayoutElement rle = root.AddComponent<LayoutElement>();
        rle.ignoreLayout = true;   // floats over the layout it is parented to
        // Above the dim and the card, so a wheel or drag anywhere over the modal
        // is caught here and never reaches the plugin's scroll rect.
        root.AddComponent<ModalInputSink>();

        // Dim backdrop, behind the card: catches every click outside as Cancel.
        GameObject dim = new GameObject("dim");
        dim.transform.SetParent(root.transform, false);
        Stretch(dim.AddComponent<RectTransform>());
        Image dimg = dim.AddComponent<Image>();
        dimg.color = new Color(0f, 0f, 0f, 0.66f);
        dimg.raycastTarget = true;
        dimImage = dimg;
        ModalClick dimClick = dim.AddComponent<ModalClick>();
        dimClick.Log = kit.Log;
        dimClick.What = "cancel the modal";
        dimClick.Click = OnCancelClick;

        // Card, on top of the dim: the lighter surface tone so it lifts off the
        // dimmed page, height fit to content; the message sits in a darker well
        // inside it (below).
        GameObject card = new GameObject("card");
        card.transform.SetParent(root.transform, false);
        RectTransform crt = card.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(0.5f, 0.5f);
        crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(760f, 300f);
        cardRect = crt;
        Image cbg = card.AddComponent<Image>();
        cbg.color = FoliageFXConfig.Surface;
        cbg.raycastTarget = true;   // absorbs clicks on the card body
        // A notice dismisses on a click anywhere, the card included (Raise
        // switches this on for a notice alone); a confirm or alert absorbs
        // card-body clicks in the Image above.
        cardClick = card.AddComponent<ModalClick>();
        cardClick.Log = kit.Log;
        cardClick.What = "dismiss the notice";
        cardClick.Click = OnCardClick;
        cardClick.enabled = false;
        VerticalLayoutGroup v = card.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(32, 32, 28, 28);
        v.spacing = 20f;
        v.childAlignment = TextAnchor.UpperLeft;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        ContentSizeFitter fit = card.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        titleText = Label(card.transform, 32, FontStyle.Bold, FoliageFXConfig.Title, 42f);

        // The message in a recessed black well - the readout box used across the
        // plugin. It grows to fit its text, but never past a cap: beyond that it
        // scrolls, so the buttons stay on screen and nothing is ever hidden.
        GameObject msgBox = new GameObject("messageBox");
        msgBox.transform.SetParent(card.transform, false);
        Image mbg = msgBox.AddComponent<Image>();
        mbg.color = FoliageFXConfig.PreviewBg;
        mbg.raycastTarget = false;
        msgCap = msgBox.AddComponent<MaxHeight>();   // well height = min(text + padding, Max)
        msgCap.Extra = 40f;                          // the viewport's top + bottom padding

        GameObject vp = new GameObject("viewport", typeof(RectTransform));
        vp.transform.SetParent(msgBox.transform, false);
        RectTransform vprt = (RectTransform)vp.transform;
        vprt.anchorMin = Vector2.zero; vprt.anchorMax = Vector2.one;
        vprt.offsetMin = new Vector2(22f, 20f);
        vprt.offsetMax = new Vector2(-36f, -20f);    // 22 padding + 14 for the scrollbar
        vp.AddComponent<RectMask2D>();
        Image vpCatch = vp.AddComponent<Image>();    // catches the wheel/drag for the scroll rect
        vpCatch.color = new Color(0f, 0f, 0f, 0f);
        vpCatch.raycastTarget = true;

        messageText = Label(vp.transform, 24, FontStyle.Normal, FoliageFXConfig.Caption, 0f);
        messageText.lineSpacing = 1.1f;
        messageText.supportRichText = true;          // <b> round the vertex counts
        RectTransform mrt = messageText.rectTransform;
        mrt.anchorMin = new Vector2(0f, 1f); mrt.anchorMax = new Vector2(1f, 1f); mrt.pivot = new Vector2(0.5f, 1f);
        mrt.offsetMin = Vector2.zero; mrt.offsetMax = Vector2.zero;
        ContentSizeFitter mfit = messageText.gameObject.AddComponent<ContentSizeFitter>();
        mfit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        mfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        msgCap.Content = mrt;

        // The table InformRows lays out, beside the message (one shows at a time).
        GameObject rowsGo = new GameObject("rows", typeof(RectTransform));
        rowsGo.transform.SetParent(vp.transform, false);
        rowsRoot = (RectTransform)rowsGo.transform;
        rowsRoot.anchorMin = new Vector2(0f, 1f); rowsRoot.anchorMax = new Vector2(1f, 1f); rowsRoot.pivot = new Vector2(0.5f, 1f);
        rowsRoot.offsetMin = Vector2.zero; rowsRoot.offsetMax = Vector2.zero;
        VerticalLayoutGroup rv = rowsGo.AddComponent<VerticalLayoutGroup>();
        rv.childAlignment = TextAnchor.UpperLeft;
        rv.childControlWidth = true; rv.childControlHeight = true;
        rv.childForceExpandWidth = true; rv.childForceExpandHeight = false;
        ContentSizeFitter rfit = rowsGo.AddComponent<ContentSizeFitter>();
        rfit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        rfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        rowsGo.SetActive(false);

        GameObject sbGo = new GameObject("scrollbar", typeof(RectTransform));
        sbGo.transform.SetParent(msgBox.transform, false);
        RectTransform srt = (RectTransform)sbGo.transform;
        srt.anchorMin = new Vector2(1f, 0f); srt.anchorMax = new Vector2(1f, 1f); srt.pivot = new Vector2(1f, 0.5f);
        srt.sizeDelta = new Vector2(8f, -40f);
        srt.anchoredPosition = new Vector2(-6f, 0f);
        Image track = sbGo.AddComponent<Image>();
        track.color = FoliageFXConfig.Surface;
        GameObject handleGo = new GameObject("handle", typeof(RectTransform));
        handleGo.transform.SetParent(sbGo.transform, false);
        RectTransform hrt = (RectTransform)handleGo.transform;
        hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one;
        hrt.offsetMin = Vector2.zero; hrt.offsetMax = Vector2.zero;
        Image handle = handleGo.AddComponent<Image>();
        handle.color = FoliageFXConfig.Caption;
        Scrollbar sb = sbGo.AddComponent<Scrollbar>();
        sb.handleRect = hrt; sb.targetGraphic = handle; sb.direction = Scrollbar.Direction.BottomToTop;

        ScrollRect scroll = msgBox.AddComponent<ScrollRect>();
        scroll.content = mrt; scroll.viewport = vprt;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;
        scroll.verticalScrollbar = sb;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        msgScroll = scroll;
        // The wheel over the message eases a couple of lines a notch, as the
        // What's New box does, instead of the scroll rect's own raw step.
        SmoothScroll smooth = vp.AddComponent<SmoothScroll>();
        msgSmooth = smooth;
        smooth.Scroll = scroll;
        smooth.StepPixels = 56f;

        GameObject rowGo = new GameObject("buttons");
        rowGo.transform.SetParent(card.transform, false);
        buttons = rowGo;
        LayoutElement ble = rowGo.AddComponent<LayoutElement>();
        ble.minHeight = 60f; ble.preferredHeight = 60f; ble.flexibleWidth = 1f;
        HorizontalLayoutGroup h = rowGo.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 16f;
        h.childControlWidth = true; h.childControlHeight = true;
        // Buttons keep their own width and centre in the row, so a lone dismiss
        // button is a button, not a bar the width of the card.
        h.childForceExpandWidth = false; h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleCenter;

        cancelBtn = kit.Button(rowGo.transform, "Cancel", FoliageFXConfig.Button, FoliageFXConfig.OnButton);
        if (cancelBtn != null && cancelBtn.button != null) cancelBtn.button.onClick.AddListener(delegate { OnCancelClick(); });
        confirmBtn = kit.Button(rowGo.transform, "Overwrite", FoliageFXConfig.Danger, FoliageFXConfig.OnDanger);
        if (confirmBtn != null && confirmBtn.button != null) confirmBtn.button.onClick.AddListener(delegate { OnConfirmClick(); });
        SizeButton(cancelBtn);
        SizeButton(confirmBtn);
        Hover(cancelBtn, FoliageFXConfig.Button);
        Hover(confirmBtn, FoliageFXConfig.Danger);

        root.SetActive(false);
    }

    // ---- a report as a table (InformRows) ----

    const float TagWidth = 172f;     // the coloured word's column (and Meta's labels)
    const float ReleaseWidth = 46f;  // "v2", when a report spans releases
    const float BulletWidth = 14f;   // the release notes' dot column

    // Lays out a load report's summary rows: labels and their values, the
    // verdict on a tinted band, feature heads, and items whose coloured word
    // and release sit in fixed columns so a wrapped line stays in its own
    // column. Rows are rebuilt each time (a report is rare, never per frame).
    void FillRows(List<ReportRow> rows)
    {
        for (int i = 0; i < rowObjects.Count; i++)
        {
            GameObject go = rowObjects[i];
            if (go == null) continue;
            go.SetActive(false);   // out of the layout now; destroyed at the end of the frame
            Object.Destroy(go);
        }
        rowObjects.Clear();
        if (rows == null || rowsRoot == null) return;
        bool releases = false;
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Kind == ReportRow.KindItem && rows[i].Release != null) { releases = true; break; }
        string muted = "<color=#" + UiKit.Hex(FoliageFXConfig.ReportMuted) + ">";
        // Release notes have their own rhythm: a release starts under a
        // hairline, a section stands clear of what is above it, bullets sit
        // apart enough to be read one at a time.
        bool notes = false;
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Kind == ReportRow.KindRelease) { notes = true; break; }
        int prev = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            ReportRow row = rows[i];
            int k = row.Kind;
            if (notes)
            {
                if (prev >= 0)
                {
                    if (k == ReportRow.KindRelease) { Gap(26f); Rule(); Gap(22f); }
                    else if (k == ReportRow.KindSection) Gap(24f);
                    else if (k == ReportRow.KindBullet) Gap(prev == ReportRow.KindSection ? 10f : 12f);
                    else Gap(prev == ReportRow.KindRelease ? 14f : 18f);
                }
            }
            else if (prev >= 0)
            {
                if (k == ReportRow.KindItem && prev == ReportRow.KindItem) Rule();
                else if (!(k == ReportRow.KindMeta && prev == ReportRow.KindMeta) && !(k == ReportRow.KindItem && prev == ReportRow.KindHead))
                    Gap(k == ReportRow.KindHead ? 22f : 16f);
            }
            switch (k)
            {
                case ReportRow.KindMeta:
                {
                    Transform r = RowGroup(0, 3, false);
                    Cell(r, row.Tag, TagWidth, FoliageFXConfig.ReportMuted, FontStyle.Normal, false);
                    Cell(r, row.Text, 0f, FoliageFXConfig.Title, FontStyle.Bold, true);
                    break;
                }
                case ReportRow.KindAction:
                {
                    Transform r = RowGroup(10, 10, true);
                    Cell(r, row.Text, 0f, FoliageFXConfig.Title, FontStyle.Normal, true);
                    break;
                }
                case ReportRow.KindHead:
                {
                    string page = row.Tag != null ? "  " + muted + "(" + row.Tag + ")</color>" : "";
                    Cell(rowsRoot, "<b>" + row.Text + "</b>" + page, 0f, FoliageFXConfig.Title, FontStyle.Normal, true);
                    break;
                }
                case ReportRow.KindItem:
                {
                    Transform r = RowGroup(7, 7, false);
                    Cell(r, row.Tag, TagWidth, row.TagColor, FontStyle.Bold, false);
                    if (releases) Cell(r, row.Release ?? "", ReleaseWidth, FoliageFXConfig.ReportMuted, FontStyle.Normal, false);
                    string text = row.Text;
                    if (row.Note != null) text += " " + muted + row.Note + "</color>";
                    if (row.More != null) text += "\n" + muted + row.More + "</color>";
                    Cell(r, text, 0f, FoliageFXConfig.Caption, FontStyle.Normal, true);
                    break;
                }
                case ReportRow.KindRelease:
                {
                    // The newest release in the accent; older ones quieter.
                    bool old = row.Note != null;
                    Transform r = RowGroup(0, 0, false);
                    Pill(r, row.Tag, old ? FoliageFXConfig.ReportRule : FoliageFXConfig.Accent, old ? FoliageFXConfig.Caption : FoliageFXConfig.OnAccent);
                    Text head = Cell(r, row.Text, 0f, old ? FoliageFXConfig.Caption : FoliageFXConfig.Title, FontStyle.Bold, true);
                    head.fontSize = 28;
                    break;
                }
                case ReportRow.KindSection:
                {
                    Cell(rowsRoot, row.Text, 0f, row.TagColor, FontStyle.Bold, true);
                    Gap(6f);
                    Rule();
                    break;
                }
                case ReportRow.KindBullet:
                {
                    // The dot in its own column, so a wrapped line stays under the text.
                    Transform r = RowGroup(0, 0, false);
                    Cell(r, ((char)0x2022).ToString(), BulletWidth, row.TagColor, FontStyle.Bold, false);
                    string lead = row.Tag != null ? "<b><color=#" + UiKit.Hex(FoliageFXConfig.Title) + ">" + row.Tag + "</color></b> " : "";
                    Cell(r, lead + row.Text, 0f, FoliageFXConfig.Caption, FontStyle.Normal, true);
                    break;
                }
                default:
                    Cell(rowsRoot, row.Text, 0f, FoliageFXConfig.Caption, FontStyle.Normal, true);
                    break;
            }
            prev = k;
        }
    }

    // A row of cells across the table, its height the tallest cell's; a band
    // row has a tinted background with room around the text.
    Transform RowGroup(int padTop, int padBottom, bool band)
    {
        GameObject go = new GameObject("row", typeof(RectTransform));
        go.transform.SetParent(rowsRoot, false);
        rowObjects.Add(go);
        HorizontalLayoutGroup h = go.AddComponent<HorizontalLayoutGroup>();
        int side = band ? 16 : 0;
        h.padding = new RectOffset(side, side, padTop, padBottom);
        h.spacing = 14f;
        h.childAlignment = TextAnchor.UpperLeft;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        if (band)
        {
            Image bg = go.AddComponent<Image>();
            bg.color = FoliageFXConfig.ReportBand;
            bg.raycastTarget = false;
        }
        return go.transform;
    }

    // One cell: a fixed width (a column), or 0 to take the rest of the row
    // and wrap there.
    Text Cell(Transform parent, string s, float width, Color color, FontStyle style, bool wrap)
    {
        GameObject go = new GameObject("cell", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        if (parent == rowsRoot) rowObjects.Add(go);
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.minWidth = width;
        le.preferredWidth = width;
        le.flexibleWidth = width > 0f ? 0f : 1f;
        Text tx = go.AddComponent<Text>();
        tx.font = kit.Font;
        tx.fontSize = 24;
        tx.fontStyle = style;
        tx.color = color;
        tx.lineSpacing = 1.1f;
        tx.alignment = TextAnchor.UpperLeft;
        tx.raycastTarget = false;
        tx.supportRichText = true;
        tx.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
        tx.verticalOverflow = VerticalWrapMode.Overflow;
        tx.text = s ?? "";
        return tx;
    }

    // A small filled label, as wide as its word (a release's "Version 2").
    void Pill(Transform parent, string s, Color bg, Color fg)
    {
        GameObject go = new GameObject("pill", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = bg;
        img.raycastTarget = false;
        HorizontalLayoutGroup h = go.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(12, 12, 5, 5);
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        Text tx = Cell(go.transform, s, 0f, fg, FontStyle.Bold, false);
        tx.fontSize = 20;
        // The word's own width, not a column's and not the rest of the row.
        LayoutElement le = tx.GetComponent<LayoutElement>();
        if (le != null) { le.minWidth = -1f; le.preferredWidth = -1f; le.flexibleWidth = 0f; }
    }

    void Gap(float height)
    {
        GameObject go = new GameObject("gap", typeof(RectTransform));
        go.transform.SetParent(rowsRoot, false);
        rowObjects.Add(go);
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
    }

    // The hairline between two items.
    void Rule()
    {
        GameObject go = new GameObject("rule", typeof(RectTransform));
        go.transform.SetParent(rowsRoot, false);
        rowObjects.Add(go);
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.minHeight = 1f;
        le.preferredHeight = 1f;
        Image img = go.AddComponent<Image>();
        img.color = FoliageFXConfig.ReportRule;
        img.raycastTarget = false;
    }

    // The well shows the plain message or the table, and scrolls and caps
    // whichever it shows.
    void UseRows(bool rows)
    {
        if (messageText != null && messageText.gameObject.activeSelf == rows) messageText.gameObject.SetActive(!rows);
        if (rowsRoot != null && rowsRoot.gameObject.activeSelf != rows) rowsRoot.gameObject.SetActive(rows);
        if (!rows) relayoutPending = false;
        RectTransform content = rows ? rowsRoot : (messageText != null ? messageText.rectTransform : null);
        if (msgScroll != null) msgScroll.content = content;
        if (msgCap != null) msgCap.Content = content;
    }

    // A table's height depends on its width, which the card sets: lay out the
    // card (widths), then the table at that width, then the card again so the
    // well takes the table's height. Starts at the top.
    void Relayout()
    {
        if (cardRect == null || rowsRoot == null) return;
        relayoutPending = !rowsRoot.gameObject.activeInHierarchy;
        if (relayoutPending) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(cardRect);
        LayoutRebuilder.ForceRebuildLayoutImmediate(rowsRoot);
        LayoutRebuilder.ForceRebuildLayoutImmediate(cardRect);
        if (msgScroll != null) msgScroll.StopMovement();
        if (msgSmooth != null) msgSmooth.Stop();
        rowsRoot.anchoredPosition = Vector2.zero;
    }

    // A dialog button keeps a comfortable fixed width instead of stretching to
    // fill the row (kit.Button flexes by default).
    static void SizeButton(UIDynamicButton b)
    {
        if (b == null) return;
        LayoutElement le = b.GetComponent<LayoutElement>();
        if (le == null) return;
        le.flexibleWidth = 0f;
        le.minWidth = 160f;
        le.preferredWidth = 240f;
    }

    // A gentle hover: the button lightens under the pointer and darkens on
    // press. kit.Paint flattens the state colours (targetGraphic tinted, states
    // white), so this restores a live hover on the button's own base colour.
    static void Hover(UIDynamicButton b, Color bg)
    {
        if (b == null || b.button == null) return;
        if (b.button.targetGraphic != null) b.button.targetGraphic.color = Color.white;
        ColorBlock cb = b.button.colors;
        cb.normalColor = bg;
        cb.highlightedColor = Color.Lerp(bg, Color.white, 0.16f);
        cb.pressedColor = Color.Lerp(bg, Color.black, 0.16f);
        cb.disabledColor = bg;
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.08f;
        b.button.colors = cb;
    }

    Text Label(Transform parent, int size, FontStyle style, Color color, float minHeight)
    {
        GameObject go = new GameObject("label");
        go.transform.SetParent(parent, false);
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.minHeight = minHeight;
        le.flexibleWidth = 1f;
        Text tx = go.AddComponent<Text>();
        tx.font = kit.Font;
        tx.fontSize = size;
        tx.fontStyle = style;
        tx.color = color;
        tx.alignment = TextAnchor.UpperLeft;
        tx.raycastTarget = false;
        tx.horizontalOverflow = HorizontalWrapMode.Wrap;
        tx.verticalOverflow = VerticalWrapMode.Overflow;
        return tx;
    }

    void OnConfirmClick()
    {
        OverlayLog.Fn c = onConfirm;
        Hide();
        if (c != null) OverlayLog.SafeRun(kit.Log, "confirm", c);
    }

    void OnCancelClick()
    {
        OverlayLog.Fn c = onCancel;
        Hide();
        if (c != null) OverlayLog.SafeRun(kit.Log, "cancel", c);
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}

// Swallows scroll and drag so the plugin page cannot move behind a modal. It
// sits on the modal root, above the dim backdrop and the card, so a wheel or
// drag over either is caught here - the first handler up from what the pointer
// hit - and never climbs to the plugin's scroll rect. It carries no click
// handler, so the buttons and the backdrop still take their clicks. Active only
// while the modal is shown, since the root it lives on is.
sealed class ModalInputSink : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IInitializePotentialDragHandler
{
    public void OnScroll(PointerEventData e) { }
    public void OnBeginDrag(PointerEventData e) { }
    public void OnDrag(PointerEventData e) { }
    public void OnEndDrag(PointerEventData e) { }
    public void OnInitializePotentialDrag(PointerEventData e) { }
}

// A click on the modal's backdrop or card, inside the log's boundary. Not a
// Selectable, unlike a Button: no hover ring, no pull toward it, no haptic
// tick. A disabled one takes no click, and the click goes nowhere.
sealed class ModalClick : MonoBehaviour, IPointerClickHandler
{
    public OverlayLog Log;
    public string What;
    public OverlayLog.Fn Click;

    public void OnPointerClick(PointerEventData e)
    {
        if (Click != null) OverlayLog.SafeRun(Log, What, Click);
    }
}

// A layout element whose preferred height is the content's own height plus a
// fixed padding, clamped to Max. It lets the message well grow to fit its text
// but never past the cap, at which point the ScrollRect on the same object
// scrolls instead - so a long list is reachable, never hidden.
sealed class MaxHeight : MonoBehaviour, ILayoutElement
{
    public RectTransform Content;
    public float Max = 560f;
    public float Extra;

    public float minWidth { get { return -1f; } }
    public float preferredWidth { get { return -1f; } }
    public float flexibleWidth { get { return -1f; } }
    public float minHeight { get { return 0f; } }
    public float preferredHeight
    {
        get
        {
            float h = Content != null ? LayoutUtility.GetPreferredHeight(Content) + Extra : 0f;
            return h < Max ? h : Max;
        }
    }
    public float flexibleHeight { get { return -1f; } }
    public int layoutPriority { get { return 1; } }
    public void CalculateLayoutInputHorizontal() { }
    public void CalculateLayoutInputVertical() { }
}
