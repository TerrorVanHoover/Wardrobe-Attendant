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
        private readonly WardrobeSlot slot;
        private readonly Pawn target;

        private readonly List<ThingDef> _cachedApparelDefs =
            new List<ThingDef>();

        private Pawn _cachedApparelTarget;
        private string _cachedSearchText;

        private readonly Listing_Standard _listing = new Listing_Standard();
        private string searchText = "";
        private Vector2 scrollPosition;

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

        public override Vector2 InitialSize => new Vector2(640f, 720f);

        public override void DoWindowContents(Rect inRect)
        {
            Listing_Standard listing = _listing;

            listing.Begin(inRect);

            listing.Label(
                "WardrobeAttendant.Slot.EditTitle".Translate(slot.slotName));

            listing.Gap(10f);

            bool lockApparel = slot.lockApparel;

            listing.CheckboxLabeled(
                "WardrobeAttendant.Slot.LockApparel".Translate(),
                ref lockApparel);

            slot.lockApparel = lockApparel;

            listing.Gap(4f);

            listing.Label(
                slot.lockApparel
                    ? "WardrobeAttendant.Slot.LockedDesc".Translate()
                    : "WardrobeAttendant.Slot.UnlockedDesc".Translate());

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
            }

            if (slot.useDurabilityThreshold)
            {
                listing.Gap(4f);

                bool removeAtThreshold = slot.removeAtDurabilityThreshold;

                listing.CheckboxLabeled(
                    "WardrobeAttendant.Slot.RemoveAtThreshold".Translate(),
                    ref removeAtThreshold);

                slot.removeAtDurabilityThreshold = removeAtThreshold;

                listing.Label(
                    "WardrobeAttendant.Slot.RemoveAtThresholdDesc".Translate());
            }

            listing.Gap(14f);

            bool useProtectionPriorities = slot.useProtectionPriorities;

            listing.CheckboxLabeled(
                "WardrobeAttendant.Slot.UseProtectionPriorities".Translate(),
                ref useProtectionPriorities);

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
                slot.mode = slot.mode == WardrobeSlotMode.FullOutfit
                    ? WardrobeSlotMode.ExactItem
                    : WardrobeSlotMode.FullOutfit;

                WardrobeGameComponent
                    .Get?
                    .NotifySlotModeChanged(target, slot);
            }

            listing.Gap(8f);

            if (slot.mode == WardrobeSlotMode.ExactItem)
            {
                listing.Label(
                    "WardrobeAttendant.Slot.ChooseExact".Translate());

                listing.Gap(4f);

                listing.Label(
                    "WardrobeAttendant.Slot.Selected".Translate(
                        slot.exactApparelDef?.LabelCap.RawText
                        ?? "WardrobeAttendant.Slot.None".Translate()));

                listing.Gap(4f);

                searchText = listing.TextEntry(searchText);
                listing.Gap(4f);

                // Clamp the list height: with a strongly resized window the
                // expression below can go negative and break the scroll view.
                float listHeight = Mathf.Max(
                    60f,
                    inRect.height - listing.CurHeight - 50f);

                Rect listRect = listing.GetRect(listHeight);

                if (_cachedApparelTarget != target ||
                    !string.Equals(_cachedSearchText, searchText, StringComparison.Ordinal))
                {
                    RebuildApparelDefCache(target, searchText);
                }

                List<ThingDef> apparelDefs = _cachedApparelDefs;

                float rowHeight = 30f;

                var viewRect = new Rect(
                    0f,
                    0f,
                    listRect.width - 20f,
                    apparelDefs.Count * rowHeight);

                Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);

                float y = 0f;

                foreach (var def in apparelDefs)
                {
                    Rect rowRect = new Rect(0f, y, viewRect.width, rowHeight);

                    if (Widgets.ButtonText(rowRect, def.LabelCap))
                    {
                        slot.exactApparelDef = def;

                        WardrobeGameComponent
                            .Get?
                            .NotifyExactDefSelected(target, slot);

                        SoundDefOf.Click.PlayOneShotOnCamera();

                        break;
                    }

                    y += rowHeight;
                }

                Widgets.EndScrollView();
            }

            listing.Gap(10f);

            if (listing.ButtonText(
                    "WardrobeAttendant.Slot.Close".Translate()))
            {
                Close();
            }

            listing.End();
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
            return apparelDef != null &&
                   target != null &&
                   apparelDef.IsApparel &&
                   apparelDef.apparel != null &&
                   ApparelUtility.HasPartsToWear(target, apparelDef) &&
                   apparelDef.apparel.PawnCanWear(target);
        }
    }
}