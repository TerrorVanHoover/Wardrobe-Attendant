using HarmonyLib;
using Verse;

namespace WardrobeAttendant
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            var harmony = new Harmony("terrorvanhoover.wardrobeattendant");
            harmony.PatchAll();

            if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
            {
                Log.Message("[Wardrobe Attendant] loaded and patched");
            }
        }
    }
}