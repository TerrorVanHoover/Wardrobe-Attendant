using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WardrobeAttendant
{
    [StaticConstructorOnStartup]
    public static class MechWorkTabCompatibility
    {
        private const string HarmonyId =
            "terrorvanhoover.wardrobeattendant.mechworktab";

        static MechWorkTabCompatibility()
        {
            try
            {
                Harmony harmony = new Harmony(HarmonyId);

                PatchColumn(harmony);
                PatchTooltip(harmony);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[Wardrobe Attendant] Mech Work Tab compatibility failed: " +
                    ex);
            }
        }

        private static void PatchColumn(Harmony harmony)
        {
            Type type = GenTypes.GetTypeInAnyAssembly(
                "SM_MechTab.DefGenerator_GenerateImpliedDefs_MechsWork");

            if (type == null)
            {
                return;
            }

            var method = AccessTools.Method(
                type,
                "CheckWorkTypeMechValid");

            if (method == null)
            {
                return;
            }

            harmony.Patch(
                method,
                prefix: new HarmonyMethod(
                    typeof(MechWorkTabCompatibility),
                    nameof(CheckWorkTypeMechValid_Prefix)));
        }

        private static void PatchTooltip(Harmony harmony)
        {
            Type type = GenTypes.GetTypeInAnyAssembly(
                "SM_MechTab.WidgetMechWork");

            if (type == null)
            {
                return;
            }

            var method = AccessTools.Method(
                type,
                "TipForPawnWorker");

            if (method == null)
            {
                return;
            }

            harmony.Patch(
                method,
                prefix: new HarmonyMethod(
                    typeof(MechWorkTabCompatibility),
                    nameof(TipForPawnWorker_Prefix)));
        }

        private static bool CheckWorkTypeMechValid_Prefix(
            WorkTypeDef workTypeDef,
            ref bool __result)
        {
            if (workTypeDef ==
                WardrobeWorkTypeDefOf.WardrobeAttendant_Wardrober)
            {
                __result = true;
                return false;
            }

            return true;
        }

        private static bool TipForPawnWorker_Prefix(
            Pawn p,
            WorkTypeDef wDef,
            ref string __result)
        {
            if (p == null ||
                wDef == null ||
                wDef != WardrobeWorkTypeDefOf.WardrobeAttendant_Wardrober ||
                p.WorkTypeIsDisabled(wDef))
            {
                return true;
            }

            if (p.RaceProps != null && p.RaceProps.Humanlike)
            {
                __result = WidgetsWork.TipForPawnWorker(p, wDef, false);
                return false;
            }

            __result = wDef.gerundLabel.CapitalizeFirst() +
                       "\n\n" +
                       wDef.description;

            return false;
        }
    }
}