// One row of a table the branded modal lays out (ConfirmBox.InformRows): the
// What's New notes, and any report that is read as rows rather than as one text.
sealed class ReportRow
{
    public const int KindPara = 0;     // a paragraph across the well
    public const int KindMeta = 1;     // a label (Tag) and its value: File, Saved by, This is
    public const int KindAction = 2;   // the verdict and what makes the report stop, on a tinted band
    public const int KindHead = 3;     // a feature's name over its rows, its page (Tag) muted
    public const int KindItem = 4;     // a coloured word (Tag), the release, the text, a muted note
    // The release notes (the Overview's Full Changelog):
    public const int KindRelease = 5;  // a release: its number in a pill (Tag), its title (Text); Note set = an older release, drawn quieter
    public const int KindSection = 6;  // a section's name (Text) in TagColor over a hairline: Heads Up, New Features...
    public const int KindBullet = 7;   // a dot in TagColor, a bold lead (Tag, may be null), the sentence (Text) with a hanging indent

    public int Kind;
    public string Tag;
    public UnityEngine.Color TagColor;
    public string Release;   // "v2" when the report spans releases, else null
    public string Text;
    public string Note;      // an item's muted ending ("at their defaults", "empty")
    public string More;      // muted lines under an item: a change's values, vertex counts, a note

    public static ReportRow Make(int kind, string tag, string text)
    {
        ReportRow row = new ReportRow();
        row.Kind = kind;
        row.Tag = tag;
        row.Text = text;
        return row;
    }

    public static ReportRow Tagged(UnityEngine.Color color, string tag, string release, string text, string note)
    {
        ReportRow row = Make(KindItem, tag, text);
        row.TagColor = color;
        row.Release = release;
        row.Note = note;
        return row;
    }
}
