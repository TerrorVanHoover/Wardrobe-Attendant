using System.Collections.Generic;
using System.Linq;
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
                    IsHaulingUnavailable(__instance);

                List<WorkTypeDef> disabledTypes =
                    __result as List<WorkTypeDef>;

                if (shouldDisableWardrober)
                {
                    if (disabledTypes == null)
                    {
                        disabledTypes = new List<WorkTypeDef>(__result);
                    }

                    if (!disabledTypes.Contains(
                            WardrobeWorkTypeDefOf.WardrobeAttendant_Wardrober))
                    {
                        disabledTypes.Add(
                            WardrobeWorkTypeDefOf.WardrobeAttendant_Wardrober);
                    }

                    __result = disabledTypes;
                    return;
                }

                if (!__instance.IsColonyMech)
                {
                    return;
                }

                if (__instance.health != null &&
                    __instance.health.DisabledWorkTypes.Contains(
                        WardrobeWorkTypeDefOf.WardrobeAttendant_Wardrober))
                {
                    return;
                }

                if (disabledTypes == null)
                {
                    disabledTypes = new List<WorkTypeDef>(__result);

                    if (!disabledTypes.Remove(
                            WardrobeWorkTypeDefOf.WardrobeAttendant_Wardrober))
                    {
                        return;
                    }

                    __result = disabledTypes;
                    return;
                }

                if (disabledTypes.Remove(
                        WardrobeWorkTypeDefOf.WardrobeAttendant_Wardrober))
                {
                    __result = disabledTypes;
                }
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
                   (pawn.IsColonist ||
                    pawn.IsSlaveOfColony ||
                    pawn.IsColonyMech) &&
                   pawn.health != null &&
                   pawn.health.capacities.CapableOf(
                       PawnCapacityDefOf.Manipulation);
        }

        private static bool IsHaulingUnavailable(Pawn pawn)
        {
            if (pawn.WorkTagIsDisabled(WorkTags.Hauling))
            {
                return true;
            }

            if (pawn.IsColonyMech)
            {
                List<WorkTypeDef> mechWorkTypes =
                    pawn.RaceProps?.mechEnabledWorkTypes;

                if (mechWorkTypes == null ||
                    !mechWorkTypes.Contains(WorkTypeDefOf.Hauling))
                {
                    return true;
                }
            }

            return false;
        }
    }
}