using RimWorld;
using Verse;

namespace WardrobeAttendant
{
    [DefOf]
    public static class WardrobeJobDefOf
    {
        public static JobDef WardrobeAttendant_RemoveApparel;

        public static JobDef WardrobeAttendant_SelfWearApparel;

        static WardrobeJobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(WardrobeJobDefOf));
        }
    }
}