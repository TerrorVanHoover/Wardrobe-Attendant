using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

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

            listing.Gap(16f);

            if (listing.ButtonText(
                    "WardrobeAttendant.Settings.ResetWindowPositions".Translate()))
            {
                settings.ResetWindowPositions();
                WardrobeModSettingsExtensions.SaveSettings();

                SoundDefOf.Click.PlayOneShotOnCamera();

                Find.WindowStack?.Add(
                    new WardrobeMessageWindow(
                        "WardrobeAttendant.Settings.WindowPositionsReset"
                            .Translate()
                            .ToString()));
            }

            listing.End();

            WardrobeModSettingsExtensions.InvalidateSettingsCache();

            base.DoSettingsWindowContents(inRect);
        }
    }
}