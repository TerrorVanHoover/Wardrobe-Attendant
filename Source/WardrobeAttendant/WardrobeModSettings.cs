using Verse;

namespace WardrobeAttendant
{
    public class WardrobeModSettings : ModSettings
    {
        public bool DebugLogging;

        public bool PreventVanillaSelfEquippingRemovalTargets;

        public bool SkipDraftedTargets;

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref DebugLogging, "debugLogging", false);
            Scribe_Values.Look(
                ref PreventVanillaSelfEquippingRemovalTargets,
                "preventVanillaSelfEquippingRemovalTargets",
                true);
            Scribe_Values.Look(
                ref SkipDraftedTargets,
                "skipDraftedTargets",
                false);
        }
    }
}