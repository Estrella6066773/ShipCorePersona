using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 认出飞船主脑，并把机械带宽维持在 20 点。
    /// 玩家自己取出机控中枢之后，不要自动装回去。
    /// 除此之外不要卸掉机控中枢，卸掉会断开她和机械族的联系。
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
            if (pawn == null || !pawn.Spawned)
                return null;

            ThingDef coreDef = DefDatabase<ThingDef>.GetNamedSilentFail("Ship_ComputerCore");
            if (coreDef == null)
                return null;

            List<Thing> cores = pawn.Map.listerThings.ThingsOfDef(coreDef);
            for (int i = 0; i < cores.Count; i++)
            {
                CompShipCorePersona onCore = cores[i].TryGetComp<CompShipCorePersona>();
                if (onCore?.Persona == pawn)
                    return cores[i];
            }

            return null;
        }

        public static bool HasVoyageChip(Pawn pawn)
        {
            HediffDef chip = DefDatabase<HediffDef>.GetNamedSilentFail(VoyageChipHediff);
            return chip != null && pawn?.health?.hediffSet?.HasHediff(chip) == true;
        }

        public static void SyncBandwidth(Pawn persona, Thing core)
        {
            if (persona?.health == null)
                return;

            bool voyageLoaded = VoyageCompat.IsLoaded;
            bool chipOnPersona = HasVoyageChip(persona);
            bool chipSeatedInCore = voyageLoaded && VoyageCompat.ChipSeated(core);
            bool mechlinkTakenOut = core?.TryGetComp<CompShipCorePersona>()?.MechlinkTakenOut == true;

            if (!mechlinkTakenOut && (chipOnPersona || !voyageLoaded || chipSeatedInCore))
                EnsureHediff(persona, HediffDefOf.MechlinkImplant, onBrain: true);

            bool hasMechlink = persona.health.hediffSet.HasHediff(HediffDefOf.MechlinkImplant);
            if (chipOnPersona && hasMechlink)
            {
                EnsureHediff(persona, ShipCorePersonaDefOf.ShipCorePersonaChipOffset, onBrain: false);
                RemoveHediff(persona, ShipCorePersonaDefOf.ShipCorePersonaBandwidth);
            }
            else if (hasMechlink && (!voyageLoaded || chipSeatedInCore))
            {
                EnsureHediff(persona, ShipCorePersonaDefOf.ShipCorePersonaBandwidth, onBrain: false);
                RemoveHediff(persona, ShipCorePersonaDefOf.ShipCorePersonaChipOffset);
            }
            else
            {
                RemoveHediff(persona, ShipCorePersonaDefOf.ShipCorePersonaBandwidth);
                RemoveHediff(persona, ShipCorePersonaDefOf.ShipCorePersonaChipOffset);
                if (voyageLoaded && !chipSeatedInCore && !chipOnPersona)
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
