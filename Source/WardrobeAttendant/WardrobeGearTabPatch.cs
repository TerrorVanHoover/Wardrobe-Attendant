using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace WardrobeAttendant
{
    [HarmonyPatch(typeof(ITab_Pawn_Gear), "FillTab")]
    public static class WardrobeGearTabPatch
    {
        private static readonly System.Reflection.FieldInfo SizeField =
            AccessTools.Field(typeof(ITab_Pawn_Gear), "size");

        public static void Postfix(ITab_Pawn_Gear __instance)
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;

            if (__instance == null ||
                !WardrobeTargetUtility.CanConfigureWardrobe(pawn))
            {
                return;
            }

            if (SizeField == null)
            {
                return;
            }

            if (!(SizeField.GetValue(__instance) is Vector2 tabSize))
            {
                return;
            }

            Rect buttonRect = new Rect(
                tabSize.x - 130f,
                tabSize.y - 42f,
                118f,
                30f);

            bool hasActiveAssignment = false;

            if (WardrobeGameComponent.Get?.TryGetSlots(pawn, out var slots) == true)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i] != null && slots[i].enabled)
                    {
                        hasActiveAssignment = true;
                        break;
                    }
                }
            }

            Color previousColor = GUI.color;

            if (hasActiveAssignment)
            {
                GUI.color = new Color(0.62f, 1f, 0.62f);
            }

            bool clicked = Widgets.ButtonText(
                buttonRect,
                "WardrobeAttendant.GearButton".Translate());

            GUI.color = previousColor;

            if (clicked)
            {
                Find.WindowStack.Add(new WardrobeWindow(pawn));
            }
        }
    }
}


