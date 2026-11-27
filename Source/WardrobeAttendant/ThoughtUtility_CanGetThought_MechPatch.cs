using HarmonyLib;
using RimWorld;
using Verse;

namespace WardrobeAttendant
{
    [HarmonyPatch(
        typeof(ThoughtUtility),
        nameof(ThoughtUtility.CanGetThought))]
    public static class ThoughtUtility_CanGetThought_MechPatch
    {
        public static bool Prefix(Pawn pawn, ref bool __result)
        {
            if (pawn != null &&
                pawn.RaceProps != null &&
                pawn.RaceProps.IsMechanoid &&
                (pawn.needs == null ||
                 pawn.needs.mood == null ||
                 pawn.story == null ||
                 pawn.story.traits == null))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}