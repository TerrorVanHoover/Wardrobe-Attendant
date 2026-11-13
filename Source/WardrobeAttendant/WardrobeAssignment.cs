using System.Collections.Generic;
using Verse;

namespace WardrobeAttendant
{
    public class WardrobeAssignment : IExposable
    {
        public Pawn target;
        public List<WardrobeSlot> slots = new List<WardrobeSlot>();

        public void ExposeData()
        {
            Scribe_References.Look(ref target, "target");
            Scribe_Collections.Look(ref slots, "slots", LookMode.Deep);
        }
    }
}