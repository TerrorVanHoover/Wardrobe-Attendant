using Verse;

namespace WardrobeAttendant
{
    public static class WardrobeModSettingsExtensions
    {
        private static WardrobeModSettings _cachedSettings;

        public static bool DebugLoggingEnabled =>
            GetSettings()?.DebugLogging ?? false;

        public static bool PreventVanillaSelfEquippingRemovalTargets =>
            GetSettings()?.PreventVanillaSelfEquippingRemovalTargets ?? false;

        public static bool SkipDraftedTargets =>
            GetSettings()?.SkipDraftedTargets ?? false;

        public static WardrobeModSettings GetSettings()
        {
            if (_cachedSettings != null)
            {
                return _cachedSettings;
            }

            _cachedSettings = LoadedModManager
                .GetMod<WardrobeAttendantMod>()
                ?.GetSettings<WardrobeModSettings>();

            return _cachedSettings;
        }

        public static void InvalidateSettingsCache()
        {
            _cachedSettings = null;
        }
    }
}