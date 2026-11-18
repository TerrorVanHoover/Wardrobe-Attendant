using Verse;

namespace WardrobeAttendant
{
    public class WardrobeModSettings : ModSettings
    {
        public bool DebugLogging;

        public bool PreventVanillaSelfEquippingRemovalTargets;

        public bool SkipDraftedTargets;

        public float WardrobeWindowX = -1f;
        public float WardrobeWindowY = -1f;
        public float WardrobeWindowWidth = -1f;
        public float WardrobeWindowHeight = -1f;

        public float SlotEditWindowX = -1f;
        public float SlotEditWindowY = -1f;
        public float SlotEditWindowWidth = -1f;
        public float SlotEditWindowHeight = -1f;

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

            Scribe_Values.Look(ref WardrobeWindowX, "wardrobeWindowX", -1f);
            Scribe_Values.Look(ref WardrobeWindowY, "wardrobeWindowY", -1f);
            Scribe_Values.Look(ref WardrobeWindowWidth, "wardrobeWindowWidth", -1f);
            Scribe_Values.Look(ref WardrobeWindowHeight, "wardrobeWindowHeight", -1f);

            Scribe_Values.Look(ref SlotEditWindowX, "slotEditWindowX", -1f);
            Scribe_Values.Look(ref SlotEditWindowY, "slotEditWindowY", -1f);
            Scribe_Values.Look(ref SlotEditWindowWidth, "slotEditWindowWidth", -1f);
            Scribe_Values.Look(ref SlotEditWindowHeight, "slotEditWindowHeight", -1f);
        }

        public void ResetWindowPositions()
        {
            WardrobeWindowX = -1f;
            WardrobeWindowY = -1f;
            WardrobeWindowWidth = -1f;
            WardrobeWindowHeight = -1f;

            SlotEditWindowX = -1f;
            SlotEditWindowY = -1f;
            SlotEditWindowWidth = -1f;
            SlotEditWindowHeight = -1f;
        }
    }
}