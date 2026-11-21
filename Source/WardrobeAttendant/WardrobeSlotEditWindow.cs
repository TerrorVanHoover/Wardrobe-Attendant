using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WardrobeAttendant
{
    public class WardrobeSlotEditWindow : Window
    {
        private const float ExactApparelListMaxHeight = 300f;
        private const float ExactApparelListRowHeight = 30f;

        private readonly WardrobeSlot slot;
        private readonly Pawn target;

        private readonly List<ThingDef> _cachedApparelDefs =
            new List<ThingDef>();

        private Pawn _cachedApparelTarget;
        private string _cachedSearchText;

        private readonly Listing_Standard _listing = new Listing_Standard();
        private string searchText = "";
        private Vector2 scrollPosition;
        private Vector2 outerScrollPosition;

        private float _contentHeight = 900f;

        private static readonly QualityCategory[] QualityOrder =
            Enum.GetValues(typeof(QualityCategory))
                .Cast<QualityCategory>()
                .ToArray();

        public WardrobeSlotEditWindow(WardrobeSlot slot, Pawn target)
        {
            this.slot = slot;
            this.target = target;

            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = true;
        }

        public override Vector2 InitialSize => new Vector2(640f, 780f);

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
            float outerViewHeight = Mathf.Max(inRect.height, _contentHeight);

            var outerViewRect = new Rect(
                0f,
                0f,
                inRect.width - 20f,
                outerViewHeight);

            Rect outerScrollRect = new Rect(
                inRect.x,
                inRect.y + 7f,
                inRect.width,
                inRect.height - 7f);

            Widgets.BeginScrollView(
                outerScrollRect,
                ref outerScrollPosition,
                outerViewRect);

            Listing_Standard listing = _listing;

            listing.Begin(outerViewRect);

            listing.Label(
                "WardrobeAttendant.Slot.EditTitle".Translate(slot.slotName));

            listing.Gap(10f);

            bool canSelfService =
                target != null &&
                (target.IsColonist || target.IsSlaveOfColony);

            if (canSelfService)
            {
                bool allowSelfService = slot.allowSelfService;

                listing.CheckboxLabeled(
                    "WardrobeAttendant.Slot.AllowSelfService".Translate(),
                    ref allowSelfService,
                    "WardrobeAttendant.Slot.AllowSelfServiceDesc".Translate());

                slot.allowSelfService = allowSelfService;

                listing.Gap(10f);
            }

            bool lockApparel = slot.lockApparel;

            listing.CheckboxLabeled(
                "WardrobeAttendant.Slot.LockApparel".Translate(),
                ref lockApparel,
                slot.lockApparel
                    ? "WardrobeAttendant.Slot.LockedDesc".Translate()
                    : "WardrobeAttendant.Slot.UnlockedDesc".Translate());

            slot.lockApparel = lockApparel;

            if (slot.lockApparel)
            {
                listing.Gap(4f);

                bool allowWorkerRemoveLocked = slot.allowWorkerRemoveLocked;

                listing.CheckboxLabeled(
                    "WardrobeAttendant.Slot.AllowWorkerRemoveLocked".Translate(),
                    ref allowWorkerRemoveLocked,
                    "WardrobeAttendant.Slot.AllowWorkerRemoveLockedDesc".Translate());

                slot.allowWorkerRemoveLocked = allowWorkerRemoveLocked;
            }

            listing.Gap(14f);

            bool useThreshold = slot.useDurabilityThreshold;

            listing.CheckboxLabeled(
                "WardrobeAttendant.Slot.UseDurability".Translate(),
                ref useThreshold);

            slot.useDurabilityThreshold = useThreshold;

            if (slot.useDurabilityThreshold)
            {
                float value = listing.Slider(slot.durabilityThreshold, 0f, 1f);
                slot.durabilityThreshold = value;

                listing.Label(
                    "WardrobeAttendant.Slot.MinimumCondition".Translate(
                        Mathf.RoundToInt(slot.durabilityThreshold * 100f)));

                listing.Gap(4f);

                bool allowEarlyReplacement = slot.allowEarlyReplacement;

                listing.CheckboxLabeled(
                    "WardrobeAttendant.Slot.AllowEarlyReplacement".Translate(),
                    ref allowEarlyReplacement,
                    "WardrobeAttendant.Slot.AllowEarlyReplacementDesc".Translate());

                slot.allowEarlyReplacement = allowEarlyReplacement;

                listing.Gap(4f);

                bool removeAtThreshold = slot.removeAtDurabilityThreshold;

                listing.CheckboxLabeled(
                    "WardrobeAttendant.Slot.RemoveAtThreshold".Translate(),
                    ref removeAtThreshold,
                    "WardrobeAttendant.Slot.RemoveAtThresholdDesc".Translate());

                slot.removeAtDurabilityThreshold = removeAtThreshold;
            }

            listing.Gap(14f);

            bool useProtectionPriorities = slot.useProtectionPriorities;

            listing.CheckboxLabeled(
                "WardrobeAttendant.Slot.UseProtectionPriorities".Translate(),
                ref useProtectionPriorities,
                "WardrobeAttendant.Slot.UseProtectionPrioritiesDesc".Translate());

            slot.useProtectionPriorities = useProtectionPriorities;

            if (slot.useProtectionPriorities)
            {
                listing.Gap(4f);

                bool prioritizeCold = slot.prioritizeCold;

                listing.CheckboxLabeled(
                    "WardrobeAttendant.Slot.PrioritizeCold".Translate(),
                    ref prioritizeCold);

                slot.prioritizeCold = prioritizeCold;

                bool prioritizeHeat = slot.prioritizeHeat;

                listing.CheckboxLabeled(
                    "WardrobeAttendant.Slot.PrioritizeHeat".Translate(),
                    ref prioritizeHeat);

                slot.prioritizeHeat = prioritizeHeat;

                bool prioritizeArmor = slot.prioritizeArmor;

                listing.CheckboxLabeled(
                    "WardrobeAttendant.Slot.PrioritizeArmor".Translate(),
                    ref prioritizeArmor);

                slot.prioritizeArmor = prioritizeArmor;

                bool prioritizeToxicity = slot.prioritizeToxicity;

                listing.CheckboxLabeled(
                    "WardrobeAttendant.Slot.PrioritizeToxicity".Translate(),
                    ref prioritizeToxicity);

                slot.prioritizeToxicity = prioritizeToxicity;

                bool prioritizeVacuum = slot.prioritizeVacuum;

                listing.CheckboxLabeled(
                    "WardrobeAttendant.Slot.PrioritizeVacuum".Translate(),
                    ref prioritizeVacuum);

                slot.prioritizeVacuum = prioritizeVacuum;
            }

            listing.Gap(14f);

            bool allowCorpseApparel = slot.allowCorpseApparel;

            listing.CheckboxLabeled(
                "WardrobeAttendant.Slot.AllowCorpseApparel".Translate(),
                ref allowCorpseApparel);

            slot.allowCorpseApparel = allowCorpseApparel;

            if (slot.allowCorpseApparel)
            {
                listing.Gap(4f);

                bool onlyCorpseApparel = slot.onlyCorpseApparel;

                listing.CheckboxLabeled(
                    "WardrobeAttendant.Slot.OnlyCorpseApparel".Translate(),
                    ref onlyCorpseApparel);

                slot.onlyCorpseApparel = onlyCorpseApparel;
            }

            listing.Gap(14f);

            listing.Label(
                "WardrobeAttendant.Slot.QualityRange".Translate());

            listing.Gap(4f);

            int minIndex = Array.IndexOf(QualityOrder, slot.minQuality);
            int maxIndex = Array.IndexOf(QualityOrder, slot.maxQuality);

            float newMin = listing.Slider(minIndex, 0f, QualityOrder.Length - 1);
            float newMax = listing.Slider(maxIndex, 0f, QualityOrder.Length - 1);

            minIndex = Mathf.RoundToInt(newMin);
            maxIndex = Mathf.RoundToInt(newMax);

            if (minIndex > maxIndex)
            {
                if (minIndex != Array.IndexOf(QualityOrder, slot.minQuality))
                {
                    maxIndex = minIndex;
                }
                else
                {
                    minIndex = maxIndex;
                }
            }

            slot.minQuality = QualityOrder[minIndex];
            slot.maxQuality = QualityOrder[maxIndex];

            listing.Label(
                "WardrobeAttendant.Summary.Quality".Translate(
                    slot.GetQualitySummary()));

            listing.Gap(14f);

            string modeLabel = slot.mode == WardrobeSlotMode.FullOutfit
                ? "WardrobeAttendant.Slot.ModeFullOutfit".Translate()
                : "WardrobeAttendant.Slot.ModeExactItem".Translate();

            if (listing.ButtonText(modeLabel))
            {
                bool switchingToExactItem =
                    slot.mode == WardrobeSlotMode.FullOutfit;

                slot.mode = switchingToExactItem
                    ? WardrobeSlotMode.ExactItem
                    : WardrobeSlotMode.FullOutfit;

                SoundDefOf.Click.PlayOneShotOnCamera();

                if (switchingToExactItem)
                {
                    _contentHeight += ExactApparelListMaxHeight;
                }
                else
                {
                    _contentHeight = Mathf.Max(
                        inRect.height,
                        _contentHeight - ExactApparelListMaxHeight);
                }

                WardrobeGameComponent
                    .Get?
                    .NotifySlotModeChanged(target, slot);
            }

            listing.Gap(8f);

            if (slot.mode == WardrobeSlotMode.ExactItem)
            {
                listing.Label(
                    "WardrobeAttendant.Slot.Selected".Translate(
                        slot.exactApparelDef?.LabelCap.RawText
                        ?? "WardrobeAttendant.Slot.None".Translate()));

                listing.Gap(4f);

                searchText = listing.TextEntry(searchText);
                listing.Gap(4f);

                float availableHeight =
                    outerViewRect.height - listing.CurHeight - 50f;

                float listHeight = Mathf.Clamp(
                    availableHeight,
                    60f,
                    ExactApparelListMaxHeight);

                Rect listRect = listing.GetRect(listHeight);

                if (_cachedApparelTarget != target ||
                    !string.Equals(_cachedSearchText, searchText, StringComparison.Ordinal))
                {
                    RebuildApparelDefCache(target, searchText);
                }

                List<ThingDef> apparelDefs = _cachedApparelDefs;

                var viewRect = new Rect(
                    0f,
                    0f,
                    listRect.width - 20f,
                    apparelDefs.Count * ExactApparelListRowHeight);

                Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);

                float y = 0f;

                for (int i = 0; i < apparelDefs.Count; i++)
                {
                    ThingDef def = apparelDefs[i];

                    Rect rowRect = new Rect(
                        0f,
                        y,
                        viewRect.width,
                        ExactApparelListRowHeight);

                    if (Widgets.ButtonText(rowRect, def.LabelCap))
                    {
                        slot.exactApparelDef = def;

                        WardrobeGameComponent
                            .Get?
                            .NotifyExactDefSelected(target, slot);

                        SoundDefOf.Click.PlayOneShotOnCamera();

                        break;
                    }

                    y += ExactApparelListRowHeight;
                }

                Widgets.EndScrollView();
            }

            listing.Gap(10f);

            if (listing.ButtonText(
                    "WardrobeAttendant.Slot.Close".Translate()))
            {
                SavePlacement();
                Close();
            }

            float measuredContentHeight = listing.CurHeight + 16f;

            if (slot.mode == WardrobeSlotMode.ExactItem)
            {
                _contentHeight = Mathf.Max(
                    _contentHeight,
                    measuredContentHeight);
            }
            else
            {
                _contentHeight = measuredContentHeight;
            }

            listing.End();

            Widgets.EndScrollView();
        }

        private void ApplySavedPlacement()
        {
            var settings = WardrobeModSettingsExtensions.GetSettings();

            if (settings == null ||
                settings.SlotEditWindowWidth < 300f ||
                settings.SlotEditWindowHeight < 300f)
            {
                return;
            }

            float x = Mathf.Clamp(
                settings.SlotEditWindowX,
                0f,
                Mathf.Max(0f, UI.screenWidth - settings.SlotEditWindowWidth));

            float y = Mathf.Clamp(
                settings.SlotEditWindowY,
                0f,
                Mathf.Max(0f, UI.screenHeight - settings.SlotEditWindowHeight));

            windowRect = new Rect(
                x,
                y,
                settings.SlotEditWindowWidth,
                settings.SlotEditWindowHeight);
        }

        private void SavePlacement()
        {
            var settings = WardrobeModSettingsExtensions.GetSettings();

            if (settings == null || windowRect.width <= 0f)
            {
                return;
            }

            settings.SlotEditWindowX = windowRect.x;
            settings.SlotEditWindowY = windowRect.y;
            settings.SlotEditWindowWidth = windowRect.width;
            settings.SlotEditWindowHeight = windowRect.height;

            WardrobeModSettingsExtensions.SaveSettings();
        }

        private void RebuildApparelDefCache(Pawn target, string search)
        {
            _cachedApparelDefs.Clear();
            _cachedApparelTarget = target;
            _cachedSearchText = search;

            var allDefs = DefDatabase<ThingDef>.AllDefsListForReading;

            for (int i = 0; i < allDefs.Count; i++)
            {
                ThingDef def = allDefs[i];

                if (def == null ||
                    !def.IsApparel ||
                    !CanTargetWearDef(def, target) ||
                    !MatchesSearch(def, search))
                {
                    continue;
                }

                _cachedApparelDefs.Add(def);
            }

            _cachedApparelDefs.Sort(
                (a, b) => string.Compare(
                    a.LabelCap.RawText,
                    b.LabelCap.RawText,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static bool MatchesSearch(ThingDef def, string search)
        {
            if (string.IsNullOrEmpty(search))
            {
                return true;
            }

            string label = def.LabelCap.RawText;

            return label.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool CanTargetWearDef(ThingDef apparelDef, Pawn target)
        {
            if (apparelDef == null ||
                target == null ||
                !apparelDef.IsApparel ||
                apparelDef.apparel == null)
            {
                return false;
            }

            try
            {
                bool structural = target.RaceProps?.Humanlike == true
                    ? CanPotentiallyWearDef(target, apparelDef)
                    : ApparelUtility.HasPartsToWear(target, apparelDef);

                return structural &&
                       apparelDef.apparel.PawnCanWear(target);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "Wardrobe Attendant: structural apparel compatibility check failed " +
                    $"for target '{target.LabelShortCap}' and apparel '{apparelDef.defName}': {ex}");

                return ApparelUtility.HasPartsToWear(target, apparelDef) &&
                       apparelDef.apparel.PawnCanWear(target);
            }
        }

        private static bool CanPotentiallyWearDef(Pawn target, ThingDef apparelDef)
        {
            if (target?.RaceProps?.body == null)
            {
                return false;
            }

            if (apparelDef?.apparel == null)
            {
                return false;
            }

            List<BodyPartGroupDef> requiredGroups = apparelDef.apparel.bodyPartGroups;

            if (requiredGroups == null || requiredGroups.Count == 0)
            {
                return true;
            }

            List<BodyPartRecord> bodyParts = target.RaceProps.body.AllParts;

            if (bodyParts == null)
            {
                return false;
            }

            for (int i = 0; i < requiredGroups.Count; i++)
            {
                BodyPartGroupDef requiredGroup = requiredGroups[i];

                if (requiredGroup == null)
                {
                    continue;
                }

                bool hasMatchingPart = false;

                for (int j = 0; j < bodyParts.Count; j++)
                {
                    if (bodyParts[j]?.IsInGroup(requiredGroup) == true)
                    {
                        hasMatchingPart = true;
                        break;
                    }
                }

                if (!hasMatchingPart)
                {
                    return false;
                }
            }

            return true;
        }
    }
}