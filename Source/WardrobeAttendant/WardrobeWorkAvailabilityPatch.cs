using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WardrobeAttendant
{
    [HarmonyPatch(
        typeof(Pawn),
        nameof(Pawn.GetDisabledWorkTypes))]
    public static class Pawn_GetDisabledWorkTypes_Wardrober_Patch
    {
        public static void Postfix(
            Pawn __instance,
            ref IEnumerable<WorkTypeDef> __result)
        {
            try
            {
                if (__instance == null ||
                    __result == null ||
                    WardrobeWorkTypeDefOf.WardrobeAttendant_Wardrober == null)
                {
                    return;
                }

                bool shouldDisableWardrober =
                    !IsAllowedWardroberWorker(__instance) ||
                    __instance.WorkTagIsDisabled(WorkTags.Hauling);

                if (!shouldDisableWardrober)
                {
                    return;
                }

                List<WorkTypeDef> disabledTypes =
                    __result as List<WorkTypeDef> ??
                    new List<WorkTypeDef>(__result);

                if (!disabledTypes.Contains(
                        WardrobeWorkTypeDefOf.WardrobeAttendant_Wardrober))
                {
                    disabledTypes.Add(
                        WardrobeWorkTypeDefOf.WardrobeAttendant_Wardrober);
                }

                __result = disabledTypes;
            }
            catch (System.Exception ex)
            {
                Log.Error(
                    "[Wardrobe Attendant] Wardrober availability patch failed: " +
                    ex);
            }
        }

        private static bool IsAllowedWardroberWorker(Pawn pawn)
        {
            return pawn != null &&
                   (pawn.IsColonist || pawn.IsSlaveOfColony);
        }
    }
}