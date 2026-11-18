using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace WardrobeAttendant
{
    [HarmonyPatch(typeof(ITab_Pawn_Gear), "FillTab")]
    public static class WardrobeGearTabPatch
    {
        // Размер должен соответствовать ванильной кнопке "Dev tool...".
        // При необходимости сверить с декомпилированным ITab_Pawn_Gear.FillTab.
        private const float ButtonWidth = 120f;
        private const float ButtonHeight = 22f;

        // Кнопка размещается у левого края вкладки.
        // Горизонтальный отступ вправо равен одной четверти ширины кнопки.
        private const float LeftOffset = ButtonWidth * 0.25f;
        private const float TopOffset = 5f;

        private static readonly System.Reflection.FieldInfo SizeField =
            AccessTools.Field(typeof(ITab_Pawn_Gear), "size");

        private static readonly Dictionary<Pawn, CachedAssignmentState> AssignmentCache =
            new Dictionary<Pawn, CachedAssignmentState>();

        public static void Postfix(ITab_Pawn_Gear __instance)
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;

            if (__instance == null ||
                !WardrobeTargetUtility.CanConfigureWardrobe(pawn))
            {
                return;
            }

            if (!(SizeField?.GetValue(__instance) is Vector2 tabSize))
            {
                return;
            }

            if (tabSize.x < ButtonWidth + LeftOffset ||
                tabSize.y < ButtonHeight + TopOffset)
            {
                return;
            }

            bool hasActiveAssignment = GetCachedAssignmentState(pawn);

            Rect buttonRect = new Rect(
                LeftOffset,
                TopOffset,
                ButtonWidth,
                ButtonHeight);

            GUI.depth = 0;

            GUI.BeginGroup(new Rect(0f, 0f, tabSize.x, tabSize.y));

            Color previousColor = GUI.color;

            if (hasActiveAssignment)
            {
                GUI.color = new Color(0.62f, 1f, 0.62f);
            }

            bool clicked = Widgets.ButtonText(
                buttonRect,
                "WardrobeAttendant.GearButton".Translate());

            GUI.color = previousColor;

            GUI.EndGroup();

            if (clicked)
            {
                Find.WindowStack.Add(new WardrobeWindow(pawn));
            }
        }

        public static void InvalidateAssignmentCache()
        {
            AssignmentCache.Clear();
        }

        private static bool GetCachedAssignmentState(Pawn pawn)
        {
            if (AssignmentCache.TryGetValue(pawn, out var cachedState))
            {
                return cachedState.HasActiveAssignment;
            }

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

            AssignmentCache[pawn] = new CachedAssignmentState
            {
                HasActiveAssignment = hasActiveAssignment
            };

            return hasActiveAssignment;
        }

        private struct CachedAssignmentState
        {
            public bool HasActiveAssignment;
        }
    }
}