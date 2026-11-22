using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WardrobeAttendant
{
    public class WardrobeWindow : Window
    {
        private readonly Pawn target;
        private readonly Listing_Standard _listing = new Listing_Standard();
        private Vector2 scrollPosition;

        private readonly Dictionary<WardrobeSlot, WardrobeSummaryCache> _summaryCache =
            new Dictionary<WardrobeSlot, WardrobeSummaryCache>();

        private static Texture2D SlotWarningIcon =>
            ContentFinder<Texture2D>.Get(
            "UI/WardrobeAttendant_SlotWarning",
            false);

        private static readonly List<WardrobeSlot> EmptySlots =
            new List<WardrobeSlot>();

        private static readonly System.Text.StringBuilder SummaryBuilder =
            new System.Text.StringBuilder(256);

        public WardrobeWindow(Pawn target)
        {
            this.target = target;

            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = true;
        }

        public override Vector2 InitialSize => new Vector2(800f, 600f);

        public override void PostOpen()
        {
            base.PostOpen();
            ApplySavedPlacement();
        }

        public override void PostClose()
        {
            SavePlacement();
            base.PostClose();
        }

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
                Rect moveUpRect = new Rect(viewRect.width - 189f, y + 2f, 22f, 16f);
                Rect moveDownRect = new Rect(viewRect.width - 189f, y + 20f, 22f, 16f);
                Rect editRect = new Rect(viewRect.width - 164f, y + 5f, 82f, 28f);
                Rect removeRect = new Rect(viewRect.width - 78f, y + 5f, 74f, 28f);

                bool showUnwearableWarning =
                    slot.enabled &&
                    slot.mode == WardrobeSlotMode.ExactItem &&
                    slot.exactApparelDef != null &&
                    !slot.IsExactDefWearable(target);

                bool showWarningIcon = showUnwearableWarning && SlotWarningIcon != null;

                Rect warningRect;
                Rect nameRect;
                Rect modeRect;
                Rect apparelRect;

                if (showWarningIcon)
                {
                    warningRect = new Rect(180f, y + 9f, 20f, 20f);
                    nameRect = new Rect(36f, y, 140f, rowHeight);
                    modeRect = new Rect(204f, y, 160f, rowHeight);
                    apparelRect = new Rect(
                        368f,
                        y,
                        viewRect.width - 562f,
                        rowHeight);
                }
                else
                {
                    warningRect = new Rect(180f, y + 9f, 20f, 20f);
                    nameRect = new Rect(36f, y, 160f, rowHeight);
                    modeRect = new Rect(204f, y, 160f, rowHeight);
                    apparelRect = new Rect(
                        368f,
                        y,
                        viewRect.width - 562f,
                        rowHeight);
                }

                bool wasEnabled = slot.enabled;
                bool enabled = slot.enabled;

                Widgets.Checkbox(
                    new Vector2(enabledRect.x, enabledRect.y),
                    ref enabled);

                slot.enabled = enabled;

                if (enabled != wasEnabled)
                {
                    WardrobeGearTabPatch.InvalidateAssignmentCache();

                    if (enabled)
                    {
                        component.NotifySlotEnabled(target, slot);
                    }
                }

                if (showWarningIcon)
                {
                    Widgets.DrawTextureFitted(
                        warningRect,
                        SlotWarningIcon,
                        1f);

                    TooltipHandler.TipRegion(
                        warningRect,
                        "WardrobeAttendant.Slot.ExactDefUnwearable".Translate());
                }

                slot.slotName = Widgets.TextField(nameRect, slot.slotName);

                if (!string.IsNullOrEmpty(slot.slotName) &&
                    slot.slotName.Length > 10)
                {
                    TooltipHandler.TipRegion(nameRect, slot.slotName);
                }

                string modeLabel = slot.mode == WardrobeSlotMode.FullOutfit
                    ? "WardrobeAttendant.Summary.FullOutfit".Translate()
                    : "WardrobeAttendant.Slot.ModeExactItem".Translate();

                if (Widgets.ButtonText(modeRect, modeLabel))
                {
                    slot.mode = slot.mode == WardrobeSlotMode.FullOutfit
                        ? WardrobeSlotMode.ExactItem
                        : WardrobeSlotMode.FullOutfit;

                    SoundDefOf.Click.PlayOneShotOnCamera();

                    component.NotifySlotModeChanged(target, slot);
                }

                string apparelLabel = slot.mode == WardrobeSlotMode.ExactItem
                    ? slot.exactApparelDef?.LabelCap.RawText
                        ?? "WardrobeAttendant.Slot.None".Translate()
                    : "WardrobeAttendant.Summary.FullOutfit".Translate();

                SummaryBuilder.Length = 0;

                SummaryBuilder.Append(apparelLabel);
                SummaryBuilder.Append("   |   ");
                SummaryBuilder.Append(
                    "WardrobeAttendant.Summary.Quality".Translate(
                        slot.GetQualitySummary()));
                SummaryBuilder.Append("   |   ");
                SummaryBuilder.Append(
                    "WardrobeAttendant.Summary.Condition".Translate(
                        slot.GetConditionSummary()));

                if (slot.lockApparel)
                {
                    SummaryBuilder.Append("   |   ");
                    SummaryBuilder.Append(
                        "WardrobeAttendant.Summary.Locked".Translate());
                }

                if (slot.removeAtDurabilityThreshold)
                {
                    SummaryBuilder.Append("   |   ");
                    SummaryBuilder.Append(
                        "WardrobeAttendant.Summary.RemoveAtThreshold".Translate());
                }

                if (slot.useDurabilityThreshold)
                {
                    SummaryBuilder.Append("   |   ");
                    SummaryBuilder.Append(
                        slot.allowEarlyReplacement
                            ? "WardrobeAttendant.Summary.EarlyReplacement".Translate()
                            : "WardrobeAttendant.Summary.NoEarlyReplacement".Translate());
                }

                if (target.IsColonist || target.IsSlaveOfColony)
                {
                    SummaryBuilder.Append("   |   ");
                    SummaryBuilder.Append(
                        slot.allowSelfService
                            ? "WardrobeAttendant.Summary.SelfService".Translate()
                            : "WardrobeAttendant.Summary.NoSelfService".Translate());
                }

                if (slot.lockApparel)
                {
                    SummaryBuilder.Append("   |   ");
                    SummaryBuilder.Append(
                        slot.allowWorkerRemoveLocked
                            ? "WardrobeAttendant.Summary.WorkerCanRemoveLocked".Translate()
                            : "WardrobeAttendant.Summary.WorkerCannotRemoveLocked".Translate());
                }

                SummaryBuilder.Append("   |   ");
                SummaryBuilder.Append(slot.GetProtectionSummary());

                SummaryBuilder.Append("   |   ");
                SummaryBuilder.Append(slot.GetCorpseApparelSummary());

                string summary = SummaryBuilder.ToString();
                WardrobeSummaryCache cache = GetOrCreateSummaryCache(slot);

                bool summaryChanged = !string.Equals(
                    cache.Summary,
                    summary,
                    System.StringComparison.Ordinal);

                bool widthChanged =
                    !Mathf.Approximately(cache.DisplayWidth, apparelRect.width);

                if (summaryChanged || widthChanged)
                {
                    cache.Summary = summary;
                    cache.DisplaySummary = null;
                    cache.DisplayWidth = apparelRect.width;
                }

                Rect summaryTextRect = apparelRect.ContractedBy(2f, 0f);

                string displaySummary = cache.DisplaySummary;

                if (displaySummary == null)
                {
                    displaySummary = summary;

                    if (Text.CalcSize(summary).x > summaryTextRect.width)
                    {
                        displaySummary = summary.Truncate(summaryTextRect.width);
                    }

                    cache.DisplaySummary = displaySummary;
                }

                if (Mouse.IsOver(apparelRect))
                {
                    Widgets.DrawHighlight(apparelRect);
                }

                TextAnchor previousSummaryAnchor = Text.Anchor;

                Text.Anchor = TextAnchor.MiddleLeft;

                Widgets.Label(summaryTextRect, displaySummary);

                Text.Anchor = previousSummaryAnchor;

                TooltipHandler.TipRegion(apparelRect, summary);

                bool isFirstSlot = i == 0;
                bool isLastSlot = i == slots.Count - 1;

                if (!isFirstSlot)
                {
                    if (Widgets.ButtonImage(moveUpRect, TexButton.ReorderUp))
                    {
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        component.MoveSlot(target, slot, -1);
                    }
                }
                else if (isLastSlot)
                {
                    Rect centeredInfoRect = new Rect(
                        moveUpRect.x,
                        y + ((rowHeight - moveUpRect.height) / 2f),
                        moveUpRect.width,
                        moveUpRect.height);

                    DrawServiceOrderInfo(centeredInfoRect);
                }
                else
                {
                    DrawServiceOrderInfo(moveUpRect);
                }

                if (!isLastSlot)
                {
                    if (Widgets.ButtonImage(moveDownRect, TexButton.ReorderDown))
                    {
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        component.MoveSlot(target, slot, 1);
                    }
                }
                else if (!isFirstSlot)
                {
                    DrawServiceOrderInfo(moveDownRect);
                }

                if (Widgets.ButtonText(editRect,
                        "WardrobeAttendant.Summary.Edit".Translate()))
                {
                    Find.WindowStack.Add(new WardrobeSlotEditWindow(slot, target));
                }

                if (Widgets.ButtonText(removeRect,
                    "WardrobeAttendant.Summary.Remove".Translate()))
                {
                    SoundDefOf.Click.PlayOneShotOnCamera();

                    component.RemoveSlot(target, slot);
                    _summaryCache.Remove(slot);

                    WardrobeGearTabPatch.InvalidateAssignmentCache();

                    break;
                }

                y += rowHeight;
            }

            Widgets.EndScrollView();

            listing.Gap(10f);

            if (listing.ButtonText(
                "WardrobeAttendant.Window.AddSlot".Translate()))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();

                component.AddSlot(target);

                WardrobeGearTabPatch.InvalidateAssignmentCache();
            }

            listing.Gap(6f);

            if (listing.ButtonText(
                    "WardrobeAttendant.Window.Close".Translate()))
            {
                SavePlacement();
                Close();
            }

            listing.End();
        }

        private static void DrawServiceOrderInfo(Rect rect)
        {
            TextAnchor previousAnchor = Text.Anchor;

            Text.Anchor = TextAnchor.MiddleCenter;

            Widgets.Label(rect, "?");

            Text.Anchor = previousAnchor;

            TooltipHandler.TipRegion(
                rect,
                "WardrobeAttendant.Slot.ServiceOrderInfo".Translate());
        }

        private void ApplySavedPlacement()
        {
            var settings = WardrobeModSettingsExtensions.GetSettings();

            if (settings == null ||
                settings.WardrobeWindowWidth < 200f ||
                settings.WardrobeWindowHeight < 150f)
            {
                return;
            }

            float x = Mathf.Clamp(
                settings.WardrobeWindowX,
                0f,
                Mathf.Max(0f, UI.screenWidth - settings.WardrobeWindowWidth));

            float y = Mathf.Clamp(
                settings.WardrobeWindowY,
                0f,
                Mathf.Max(0f, UI.screenHeight - settings.WardrobeWindowHeight));

            windowRect = new Rect(
                x,
                y,
                settings.WardrobeWindowWidth,
                settings.WardrobeWindowHeight);
        }

        private void SavePlacement()
        {
            var settings = WardrobeModSettingsExtensions.GetSettings();

            if (settings == null || windowRect.width <= 0f)
            {
                return;
            }

            settings.WardrobeWindowX = windowRect.x;
            settings.WardrobeWindowY = windowRect.y;
            settings.WardrobeWindowWidth = windowRect.width;
            settings.WardrobeWindowHeight = windowRect.height;

            WardrobeModSettingsExtensions.SaveSettings();
        }

        private WardrobeSummaryCache GetOrCreateSummaryCache(WardrobeSlot slot)
        {
            if (!_summaryCache.TryGetValue(slot, out var cache))
            {
                cache = new WardrobeSummaryCache();
                _summaryCache[slot] = cache;
            }

            return cache;
        }

        private bool IsTargetValid()
        {
            return WardrobeTargetUtility.CanConfigureWardrobe(target);
        }

        private sealed class WardrobeSummaryCache
        {
            public string Summary;
            public string DisplaySummary;
            public float DisplayWidth;
        }
    }
}