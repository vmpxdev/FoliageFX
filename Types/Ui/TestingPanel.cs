// The Show Hints switch (it lives on Overview): the line under each control
// and the cards' story boxes. Ui scope: a display preference saved with the
// scene, kept off VaM's storable lists.
sealed class TestingPanel
{
    public JSONStorableBool Hints;

    public void Create(ParamSet set)
    {
        Hints = set.Bool(FoliageFXConfig.PluginId, ParamSet.Ui | ParamSet.Private, "show_hints", new JSONStorableBool("Show Hints", true));
    }
}
