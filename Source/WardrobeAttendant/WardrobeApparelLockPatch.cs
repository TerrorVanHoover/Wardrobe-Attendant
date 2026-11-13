using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WardrobeAttendant
{
    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Wear))]
    public static class Pawn_ApparelTracker_Wardrobe_Patch
    {
        public static bool Prefix(
            Pawn_ApparelTracker __instance,
            Apparel newApparel)
        {
            try
            {
                Pawn target = __instance?.pawn;
                WardrobeGameComponent component = WardrobeGameComponent.Get;

                if (target == null ||
                    newApparel == null ||
                    component == null ||
                    !component.TryGetLockPending(newApparel, out var pending))
                {
                    return true;
                }

                bool isPolicyAllowed =
    target.outfits?.CurrentApparelPolicy == null ||
    target.outfits.CurrentApparelPolicy.filter.Allows(newApparel);

                bool isValid =
                    target.Spawned &&
                    !target.Dead &&
                    target.apparel != null &&
                    !target.apparel.WornApparel.Contains(newApparel) &&
                    !newApparel.Destroyed &&
                    !newApparel.IsBurning() &&
                    !newApparel.IsForbidden(target) &&
                    isPolicyAllowed;

                if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                {
                    Log.Message(
                        $"[Wardrobe] Wear validation for {target.LabelShortCap}: " +
                        $"{newApparel.def.defName}, " +
                        $"spawned={target.Spawned}, " +
                        $"dead={target.Dead}, " +
                        $"alreadyWorn={target.apparel != null && target.apparel.WornApparel.Contains(newApparel)}, " +
                        $"destroyed={newApparel.Destroyed}, " +
                        $"burning={newApparel.IsBurning()}, " +
                        $"forbidden={newApparel.IsForbidden(target)}, " +
                        $"policyAllowed={isPolicyAllowed}, " +
                        $"corpse={newApparel.WornByCorpse}");
                }

                if (isValid)
                {
                    return true;
                }

                component.ClearLockPending(newApparel);

                if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                {
                    Log.Message(
                        $"[Wardrobe] Cancelled invalid wear for " +
                        $"{target.LabelShortCap}: " +
                        $"{newApparel.def.defName} is no longer wearable.");
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[Wardrobe Attendant] Wear validation patch failed: " + ex);

                return true;
            }
        }

        public static void Postfix(
            Pawn_ApparelTracker __instance,
            Apparel newApparel)
        {
            try
            {
                var pawn = __instance?.pawn;
                var component = WardrobeGameComponent.Get;

                if (pawn == null ||
                    newApparel == null ||
                    component == null ||
                    !component.IsLockPending(newApparel))
                {
                    return;
                }

                component.ClearLockPending(newApparel);

                if (pawn.outfits?.forcedHandler == null ||
                    !component.TryGetSlots(pawn, out var slots))
                {
                    return;
                }

                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];

                    if (slot == null ||
                        !slot.enabled ||
                        !slot.lockApparel)
                    {
                        continue;
                    }

                    bool shouldForce = slot.mode == WardrobeSlotMode.ExactItem
                        ? slot.exactApparelDef == newApparel.def &&
                          slot.AllowsApparel(newApparel)
                        : slot.AllowsApparel(newApparel);

                    if (shouldForce)
                    {
                        pawn.outfits.forcedHandler.SetForced(newApparel, true);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[Wardrobe Attendant] Wear lock patch failed: " + ex);
            }
        }
    }
}