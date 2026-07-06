using UnityModManagerNet;

namespace Sapphire
{
    // Mod entry point. UnityModManager calls Load when the game opens.
    internal static class Startup
    {
        internal static void Load(UnityModManager.ModEntry modEntry) {
            MainClass.Setup(modEntry);
        }
    }
}
