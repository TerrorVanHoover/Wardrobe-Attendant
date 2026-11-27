using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace WardrobeAttendant
{
    public static class WardrobeTargetUtility
    {
        private static readonly MethodInfo HumanoidMechCheck =
            GenTypes.GetTypeInAnyAssembly("HumanoidMechMod.HumanoidMechUtility")
                ?.GetMethod(
                    "IsHumanoidMech",
                    BindingFlags.Public | BindingFlags.Static);

        private static readonly Dictionary<string, bool> HumanoidMechCache =
            new Dictionary<string, bool>();

        public static bool CanConfigureWardrobe(Pawn target)
        {
            if (target == null ||
                target.Destroyed ||
                !target.Spawned ||
                target.Dead ||
                target.apparel == null)
            {
                return false;
            }

            if (target.IsColonist ||
                target.IsSlaveOfColony ||
                target.IsPrisonerOfColony)
            {
                return true;
            }

            if (target.IsColonyMech)
            {
                return CanMechWearApparel(target);
            }

            return target.RaceProps != null &&
                   target.RaceProps.Animal &&
                   target.Faction == Faction.OfPlayer;
        }

        public static bool CanMechWearApparel(Pawn target)
        {
            if (target == null || target.apparel == null)
            {
                return false;
            }

            if (IsHumanoidMechModPawn(target))
            {
                return true;
            }

            return target.apparel.AnyApparel;
        }

        private static bool IsHumanoidMechModPawn(Pawn pawn)
        {
            if (pawn == null ||
                pawn.def == null ||
                HumanoidMechCheck == null)
            {
                return false;
            }

            string defName = pawn.def.defName;

            if (HumanoidMechCache.TryGetValue(defName, out bool cached))
            {
                return cached;
            }

            bool result;

            try
            {
                result = (bool)HumanoidMechCheck.Invoke(
                    null,
                    new object[] { pawn });
            }
            catch
            {
                result = false;
            }

            HumanoidMechCache[defName] = result;
            return result;
        }
    }
}