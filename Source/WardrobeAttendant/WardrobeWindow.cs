using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WardrobeAttendant
{
    public class WardrobeWindow : Window
    {
        private readonly Pawn target;
        private readonly Listing_Standard _listing = new Listing_Standard();
        private Vector2 scrollPosition;

        public WardrobeWindow(Pawn target)
        {
            this.target = target;

            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = true;
        }

        private static readonly List<WardrobeSlot> EmptySlots =
            new List<WardrobeSlot>();

        public override Vector2 InitialSize => new Vector2(800f, 600f);

        public override void DoWindowContents(Rect inRect)
        {
            if (!IsTargetValid())
            {
                Close();
                return;
            }

            var component = WardrobeGameComponent.Get;

            if (component == null)
            {
                Widgets.Label(
                    inRect,
                    "WardrobeAttendant.Window.ComponentNotInitialized".Translate());

                return;
            }

            List<WardrobeSlot> slots = component.GetSlots(target) ?? EmptySlots;

            Listing_Standard listing = _listing;

            listing.Begin(inRect);

            listing.Label(
                "WardrobeAttendant.Window.Title".Translate(target.LabelShortCap));

            listing.Gap(10f);

            Rect listRect = listing.GetRect(inRect.height - 90f);
            float rowHeight = 38f;

            var viewRect = new Rect(
                0f,
                0f,
                listRect.width - 20f,
                slots.Count * rowHeight);

            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);

            float y = 0f;

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];

                if (slot == null)
                {
                    continue;
                }

                Rect enabledRect = new Rect(0f, y + 8f, 28f, 28f);
                Rect nameRect = new Rect(36f, y, 160f, rowHeight);
                Rect modeRect = new Rect(204f, y, 160f, rowHeight);
                Rect apparelRect = new Rect(372f, y, viewRect.width - 546f, rowHeight);
                Rect editRect = new Rect(viewRect.width - 168f, y + 5f, 82f, 28f);
                Rect removeRect = new Rect(viewRect.width - 80f, y + 5f, 74f, 28f);

                bool wasEnabled = slot.enabled;
                bool enabled = slot.enabled;

                Widgets.Checkbox(
                    new Vector2(enabledRect.x, enabledRect.y),
                    ref enabled);

                slot.enabled = enabled;

                if (enabled && !wasEnabled)
                {
                    component.NotifySlotEnabled(target, slot);
                }

                slot.slotName = Widgets.TextField(nameRect, slot.slotName);

                string modeLabel = slot.mode == WardrobeSlotMode.FullOutfit
                    ? "WardrobeAttendant.Summary.FullOutfit".Translate()
                    : "WardrobeAttendant.Slot.ModeExactItem".Translate();

                if (Widgets.ButtonText(modeRect, modeLabel))
                {
                    slot.mode = slot.mode == WardrobeSlotMode.FullOutfit
                        ? WardrobeSlotMode.ExactItem
                        : WardrobeSlotMode.FullOutfit;

                    component.NotifySlotModeChanged(target, slot);
                }

                string apparelLabel = slot.mode == WardrobeSlotMode.ExactItem
                    ? slot.exactApparelDef?.LabelCap.RawText
                        ?? "WardrobeAttendant.Slot.None".Translate()
                    : "WardrobeAttendant.Summary.FullOutfit".Translate();

                string summary =
                    apparelLabel +
                    "   |   " +
                    "WardrobeAttendant.Summary.Quality".Translate(slot.GetQualitySummary()) +
                    "   |   " +
                    "WardrobeAttendant.Summary.Condition".Translate(slot.GetConditionSummary());

                if (slot.lockApparel)
                {
                    summary += "   |   " +
                        "WardrobeAttendant.Summary.Locked".Translate();
                }

                if (slot.removeAtDurabilityThreshold)
                {
                    summary += "   |   " +
                        "WardrobeAttendant.Summary.RemoveAtThreshold".Translate();
                }

                summary += "   |   " +
                    slot.GetProtectionSummary();

                summary += "   |   " +
                    slot.GetCorpseApparelSummary();

                string displaySummary = summary;

                if (Text.CalcSize(summary).x > apparelRect.width)
                {
                    displaySummary = summary.Truncate(apparelRect.width - 18f);
                    displaySummary += "…";
                }

                Widgets.Label(apparelRect, displaySummary);
                TooltipHandler.TipRegion(apparelRect, summary);

                if (Widgets.ButtonText(editRect,
                        "WardrobeAttendant.Summary.Edit".Translate()))
                {
                    Find.WindowStack.Add(new WardrobeSlotEditWindow(slot, target));
                }

                if (Widgets.ButtonText(removeRect,
                        "WardrobeAttendant.Summary.Remove".Translate()))
                {
                    component.RemoveSlot(target, slot);
                    break;
                }

                y += rowHeight;
            }

            Widgets.EndScrollView();

            listing.Gap(10f);

            if (listing.ButtonText(
                    "WardrobeAttendant.Window.AddSlot".Translate()))
            {
                component.AddSlot(target);
            }

            listing.Gap(6f);

            if (listing.ButtonText(
                    "WardrobeAttendant.Window.Close".Translate()))
            {
                Close();
            }

            listing.End();
        }

        private bool IsTargetValid()
        {
            return WardrobeTargetUtility.CanConfigureWardrobe(target);
        }
    }
}


