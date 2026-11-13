using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace WardrobeAttendant
{
    [HarmonyPatch(
        typeof(JobDriver_ForceTargetWear),
        "TryUnequipSomething")]
    public static class JobDriver_ForceTargetWear_Wardrobe_Patch
    {
        private const int WearValidationIntervalTicks = 30;

        public static bool Prefix(JobDriver_ForceTargetWear __instance)
        {
            try
            {
                Job job = __instance?.job;

                if (job == null)
                {
                    return true;
                }

                Pawn target = job.targetA.Thing as Pawn;
                Apparel apparel = job.targetB.Thing as Apparel;
                Pawn worker = __instance.pawn;

                if (target == null ||
                    apparel == null ||
                    worker == null)
                {
                    return true;
                }

                if (WardrobeModSettingsExtensions.SkipDraftedTargets &&
                    target.Drafted)
                {
                    __instance.EndJobWith(JobCondition.Incompletable);
                    return false;
                }

                WardrobeGameComponent component = WardrobeGameComponent.Get;

                if (component == null ||
                    !component.TryGetLockPending(apparel, out _))
                {
                    return true;
                }

                if (!target.IsHashIntervalTick(WearValidationIntervalTicks))
                {
                    return true;
                }

                if (!WorkGiver_WardrobeAttendant.IsWearStillValid(
                        target,
                        apparel,
                        worker))
                {
                    component.ClearLockPending(apparel);

                    if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                    {
                        Log.Message(
                            "[Wardrobe] Cancelled stale ForceTargetWear job for " +
                            $"{target.LabelShortCap}: " +
                            $"{apparel.def.defName} is no longer a valid candidate.");
                    }

                    __instance.EndJobWith(JobCondition.Incompletable);
                    return false;
                }

                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error(
                    "[Wardrobe Attendant] ForceTargetWear validation failed: " +
                    ex);

                return true;
            }
        }
    }
}