using UnityModManagerNet;

namespace Sapphire
{
    // Mod entry point. UnityModManager calls Load when the game opens.
    internal static class Startup
    {
        internal static void Load(UnityModManager.ModEntry modEntry) {
            // First, before anything names a PrismLib.UI type: one copy loads per session, so
            // bring every mod folder's copy up to the newest.
            PrismLib.Bootstrap.PrismBootstrap.SyncUi();
            MainClass.Setup(modEntry);
        }
    }
}
