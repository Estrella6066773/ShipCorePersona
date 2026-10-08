using RimWorld;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 本模组用到的定义。
    /// </summary>
    [DefOf]
    public static class ShipCorePersonaDefOf
    {
        public static PawnKindDef ShipCorePersona;
        public static HediffDef ShipCorePersonaBound;
        public static HediffDef ShipCorePersonaBandwidth;
        public static HediffDef ShipCorePersonaChipOffset;
        public static JobDef OperateShipCorePersona;
        public static JobDef StoreShipCoreImplant;
        public static JobDef RetrieveShipCoreImplant;

        static ShipCorePersonaDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ShipCorePersonaDefOf));
        }
    }
}
