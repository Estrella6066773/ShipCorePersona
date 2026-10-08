using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 判断「这个人是不是住在电脑核心里的人格」，以及带宽该怎么叠。
    ///
    /// 联动关系：
    /// 电脑核心上的 <see cref="CompShipCorePersona"/> 持有这个人。
    /// 征召、手术、远程修理的补丁都通过这里确认目标，避免误伤普通殖民者。
    /// Gravship Voyage 的人格芯片如果也装在这个人身上，带宽改由芯片提供的 20 点负责，
    /// 机械链路自带的 6 点用校正健康状态扣回，合计仍是 20。
    ///
    /// 注意：不要为了扣掉那 6 点而移除机械链路。机械链路一移除，原版会立刻断开全部机械族。
    /// </summary>
    public static class CorePersonaUtility
    {
        public const string VoyageChipHediff = "AsuNib_Voyage_PersonaChipCarrier";

        public static bool IsPersona(Pawn pawn)
        {
            return pawn?.health?.hediffSet?.HasHediff(ShipCorePersonaDefOf.ShipCorePersonaBound) == true;
        }

        public static Thing CoreOf(Pawn pawn)
        {
            if (pawn?.ParentHolder is ThingOwner owner && owner.Owner is CompShipCorePersona comp)
                return comp.parent;
            return null;
        }

        public static bool HasVoyageChip(Pawn pawn)
        {
            HediffDef chip = DefDatabase<HediffDef>.GetNamedSilentFail(VoyageChipHediff);
            return chip != null && pawn?.health?.hediffSet?.HasHediff(chip) == true;
        }

        /// <summary>
        /// 按当前有没有 Voyage 人格芯片，把带宽来源收成合计 20 点。
        /// 芯片不在核心里时，这个人格不再担任机械师，控制权留给拿着芯片的人。
        /// </summary>
        public static void SyncBandwidth(Pawn persona, Thing core)
        {
            if (persona?.health == null)
                return;

            bool voyageLoaded = VoyageCompat.IsLoaded;
            bool chipOnPersona = HasVoyageChip(persona);
            bool chipSeatedInCore = voyageLoaded && VoyageCompat.ChipSeated(core);

            if (chipOnPersona)
            {
                EnsureHediff(persona, HediffDefOf.MechlinkImplant, onBrain: true);
                EnsureHediff(persona, ShipCorePersonaDefOf.ShipCorePersonaChipOffset, onBrain: false);
                RemoveHediff(persona, ShipCorePersonaDefOf.ShipCorePersonaBandwidth);
            }
            else if (!voyageLoaded || chipSeatedInCore)
            {
                EnsureHediff(persona, HediffDefOf.MechlinkImplant, onBrain: true);
                EnsureHediff(persona, ShipCorePersonaDefOf.ShipCorePersonaBandwidth, onBrain: false);
                RemoveHediff(persona, ShipCorePersonaDefOf.ShipCorePersonaChipOffset);
            }
            else
            {
                RemoveHediff(persona, ShipCorePersonaDefOf.ShipCorePersonaBandwidth);
                RemoveHediff(persona, ShipCorePersonaDefOf.ShipCorePersonaChipOffset);
                RemoveHediff(persona, HediffDefOf.MechlinkImplant);
            }

            PawnComponentsUtility.AddAndRemoveDynamicComponents(persona, false);
            persona.mechanitor?.Notify_BandwidthChanged();
        }

        public static void TransferMechs(Pawn from, Pawn to)
        {
            if (from == null || to == null || from == to || from.mechanitor == null || to.mechanitor == null)
                return;

            List<Pawn> mechs = from.mechanitor.ControlledPawns.ToList();
            for (int i = 0; i < mechs.Count; i++)
            {
                Pawn mech = mechs[i];
                if (mech == null || mech.Dead)
                    continue;

                from.relations?.TryRemoveDirectRelation(PawnRelationDefOf.Overseer, mech);
                mech.OverseerSubject?.Notify_DisconnectedFromOverseer();
                if (mech.GetOverseer() != to)
                    to.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
                to.mechanitor.AssignPawnControlGroup(mech, null);
            }

            from.mechanitor.Notify_BandwidthChanged();
            to.mechanitor.Notify_BandwidthChanged();
        }

        private static void EnsureHediff(Pawn pawn, HediffDef def, bool onBrain)
        {
            if (def == null || pawn.health.hediffSet.HasHediff(def))
                return;

            BodyPartRecord part = null;
            if (onBrain)
                part = BrainOf(pawn);

            pawn.health.AddHediff(def, part);
        }

        public static BodyPartRecord BrainOf(Pawn pawn)
        {
            BodyPartDef brain = DefDatabase<BodyPartDef>.GetNamedSilentFail("Brain");
            if (brain == null)
                return null;
            return pawn.RaceProps?.body?.GetPartsWithDef(brain).FirstOrDefault();
        }

        private static void RemoveHediff(Pawn pawn, HediffDef def)
        {
            if (def == null)
                return;

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (hediff != null)
                pawn.health.RemoveHediff(hediff);
        }
    }
}
