using UnityEngine;
using Verse;

namespace WardrobeAttendant
{
    public class WardrobeAttendantMod : Mod
    {
        public WardrobeAttendantMod(ModContentPack content) : base(content)
        {
            GetSettings<WardrobeModSettings>();
            WardrobeModSettingsExtensions.InvalidateSettingsCache();
        }

        public override string SettingsCategory()
        {
            return "Wardrobe Attendant";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var settings = GetSettings<WardrobeModSettings>();
            var listing = new Listing_Standard();

            listing.Begin(inRect);

            bool debugLogging = settings.DebugLogging;

            listing.CheckboxLabeled(
                "WardrobeAttendant.Settings.DebugLogging".Translate(),
                ref debugLogging,
                "WardrobeAttendant.Settings.DebugLoggingDesc".Translate());

            settings.DebugLogging = debugLogging;

            listing.Gap(8f);

            bool preventVanillaSelfEquipping =
                settings.PreventVanillaSelfEquippingRemovalTargets;

            listing.CheckboxLabeled(
                "WardrobeAttendant.Settings.PreventVanillaSelfEquipping".Translate(),
                ref preventVanillaSelfEquipping,
                "WardrobeAttendant.Settings.PreventVanillaSelfEquippingDesc".Translate());

            settings.PreventVanillaSelfEquippingRemovalTargets =
                preventVanillaSelfEquipping;

            listing.Gap(8f);

            bool skipDraftedTargets = settings.SkipDraftedTargets;

            listing.CheckboxLabeled(
                "WardrobeAttendant.Settings.SkipDraftedTargets".Translate(),
                ref skipDraftedTargets,
                "WardrobeAttendant.Settings.SkipDraftedTargetsDesc".Translate());

            settings.SkipDraftedTargets = skipDraftedTargets;

            listing.End();

            WardrobeModSettingsExtensions.InvalidateSettingsCache();

            base.DoSettingsWindowContents(inRect);
        }
    }
}