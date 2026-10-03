using System.Xml;
using Verse;

namespace DeathrestUnbound;

/// <summary>
/// Applies <see cref="match"/> when a load-time setting is on, otherwise <see cref="nomatch"/>. Mod classes (and so our
/// settings) are created before XML patching, so the setting is already loaded here; changes need a restart.
/// </summary>
public class PatchOperation_DeathrestUnboundSetting : PatchOperation
{
    public string setting;
    public PatchOperation match;
    public PatchOperation nomatch;

    protected override bool ApplyWorker(XmlDocument xml)
    {
        bool on = IsOn(setting);
        PatchOperation op = on ? match : nomatch;
        return op == null || op.Apply(xml);
    }

    private static bool IsOn(string name)
    {
        DeathrestUnboundSettings s = DeathrestUnboundMod.Settings;
        switch (name)
        {
            case "hideSerum":
                return s?.hideSerum ?? true;
            default:
                Log.Error($"[DeathrestUnbound] Unknown patch setting '{name}'.");
                return false;
        }
    }
}
