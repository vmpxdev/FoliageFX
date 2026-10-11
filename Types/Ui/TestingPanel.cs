// The Technical Info and Show Hints switches (both live on Overview).
// Technical Info reveals the numbers meant for tuning and bug reports: the
// per-frame milliseconds on the dashboard and in the captions, the cost
// caption and its hints, and Diagnose. Off, the plugin speaks plainly. Show
// Hints shows the line under each control and the cards' story boxes. Both
// are Ui scope: display preferences saved with the scene, kept off VaM's
// storable lists.
sealed class TestingPanel
{
    public JSONStorableBool Technical;
    public JSONStorableBool Hints;

    public void Create(ParamSet set)
    {
        Technical = set.Bool(FoliageFXConfig.PluginId, ParamSet.Ui | ParamSet.Private, "technical_info", new JSONStorableBool("Technical Info", false));
        Hints = set.Bool(FoliageFXConfig.PluginId, ParamSet.Ui | ParamSet.Private, "show_hints", new JSONStorableBool("Show Hints", true));
    }
}
