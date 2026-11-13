using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace WardrobeAttendant
{
    [HarmonyPatch(
        typeof(JobGiver_OptimizeApparel),
        "TryGiveJob")]
    public static class JobGiver_OptimizeApparel_TryGiveJob_Patch
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            try
            {
                if (!WardrobeModSettingsExtensions
                        .PreventVanillaSelfEquippingRemovalTargets)
                {
                    return;
                }

                if (__result == null ||
                    __result.def != JobDefOf.Wear)
                {
                    return;
                }

                Apparel apparel = __result.targetA.Thing as Apparel;

                if (apparel == null ||
                    !WardrobeRemovalRules.ShouldPreventSelfEquipping(
                        pawn,
                        apparel))
                {
                    return;
                }

                __result = null;
            }
            catch (System.Exception ex)
            {
                Log.Error(
                    "[Wardrobe Attendant] Vanilla apparel optimization patch failed: " +
                    ex);
            }
        }
    }

    public static class WardrobeRemovalRules
    {
        public static bool ShouldPreventSelfEquipping(
    Pawn pawn,
    Apparel apparel)
        {
            if (pawn == null ||
                apparel == null ||
                apparel.Destroyed ||
                pawn.apparel == null ||
                pawn.outfits?.forcedHandler == null ||
                pawn.outfits.forcedHandler.IsForced(apparel))
            {
                return false;
            }

            WardrobeGameComponent component = WardrobeGameComponent.Get;

            if (component == null ||
                !component.TryGetSlots(pawn, out List<WardrobeSlot> slots))
            {
                return false;
            }

            int count = slots.Count;

            for (int i = 0; i < count; i++)
            {
                WardrobeSlot slot = slots[i];

                if (slot == null ||
                    !slot.enabled ||
                    !slot.removeAtDurabilityThreshold)
                {
                    continue;
                }

                bool failsQualityFilter =
                    !slot.MatchesApparelIgnoreDurability(apparel);

                bool belowDurabilityThreshold =
                    slot.useDurabilityThreshold &&
                    GetCondition(apparel) < slot.durabilityThreshold;

                if (slot.mode == WardrobeSlotMode.ExactItem)
                {
                    if (apparel.def == slot.exactApparelDef &&
                        (failsQualityFilter || belowDurabilityThreshold))
                    {
                        return true;
                    }

                    continue;
                }

                if (failsQualityFilter || belowDurabilityThreshold)
                {
                    return true;
                }
            }

            return false;
        }

        private static float GetCondition(Apparel apparel)
        {
            return apparel.HitPoints /
                   (float)System.Math.Max(1, apparel.MaxHitPoints);
        }
    }
}