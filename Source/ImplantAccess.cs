using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 飞船电脑核心存取机械师植入物，规则与原版自行安装相同。
    /// 取出队列的下标写在工作的 count 上。不能从中间删掉更早的记录，否则读档后的工作会取错物品。
    /// </summary>
    public static class ImplantAccess
    {
        public static bool IsMechanitorImplant(ThingDef def)
        {
            CompProperties_UseEffectInstallImplant props = InstallProps(def);
            if (props?.hediffDef == null)
                return false;
            if (props.hediffDef == HediffDefOf.MechlinkImplant)
                return true;

            CompProperties_Usable usable = def.GetCompProperties<CompProperties_Usable>();
            return usable?.userMustHaveHediff == HediffDefOf.MechlinkImplant;
        }

        public static bool CanInstall(Pawn pawn, ThingDef item)
        {
            CompProperties_UseEffectInstallImplant props = InstallProps(item);
            if (pawn?.health == null || props?.hediffDef == null)
                return false;

            CompProperties_Usable usable = item.GetCompProperties<CompProperties_Usable>();
            if (usable?.userMustHaveHediff != null && !pawn.health.hediffSet.HasHediff(usable.userMustHaveHediff))
                return false;
            if (props.requiresPsychicallySensitive && pawn.psychicEntropy != null && !pawn.psychicEntropy.IsPsychicallySensitive)
                return false;

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(props.hediffDef);
            if (existing == null)
                return !props.requiresExistingHediff;
            if (!props.canUpgrade || existing is not Hediff_Level level)
                return false;
            if (level.level >= level.def.maxSeverity || props.maxSeverity <= level.level)
                return false;
            if (props.minSeverity > level.level)
                return false;
            return true;
        }

        public static bool CanRetrieve(Pawn pawn, ThingDef item)
        {
            CompProperties_UseEffectInstallImplant props = InstallProps(item);
            if (pawn?.health == null || props?.hediffDef == null)
                return false;

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(props.hediffDef);
            return hediff != null && ItemToTakeOut(hediff) == item;
        }

        public static void Install(CompShipCorePersona comp, Thing item)
        {
            Pawn pawn = comp?.Persona;
            CompProperties_UseEffectInstallImplant props = InstallProps(item?.def);
            if (pawn?.health == null || props?.hediffDef == null || !CanInstall(pawn, item.def))
                return;

            Thing one = item.stackCount > 1 ? item.SplitOff(1) : item;
            BodyPartRecord part = CorePersonaUtility.BrainOf(pawn);
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(props.hediffDef);
            if (existing == null)
                pawn.health.AddHediff(props.hediffDef, part);
            else if (existing is Hediff_Level level)
                level.ChangeLevel(1);

            if (props.hediffDef == HediffDefOf.MechlinkImplant)
                comp.MechlinkTakenOut = false;

            if (one != null && !one.Destroyed)
                one.Destroy(DestroyMode.Vanish);

            PawnComponentsUtility.AddAndRemoveDynamicComponents(pawn, false);
            CorePersonaUtility.SyncBandwidth(pawn, comp.parent);
        }

        public static void Retrieve(CompShipCorePersona comp, ThingDef item, Pawn carrier)
        {
            Pawn pawn = comp?.Persona;
            CompProperties_UseEffectInstallImplant props = InstallProps(item);
            if (pawn?.health == null || props == null || !CanRetrieve(pawn, item))
                return;

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(props.hediffDef);
            if (props.hediffDef == HediffDefOf.MechlinkImplant)
                comp.MechlinkTakenOut = true;

            if (hediff is Hediff_Level level && level.level > 1)
                level.ChangeLevel(-1);
            else if (hediff != null)
                pawn.health.RemoveHediff(hediff);

            Thing made = ThingMaker.MakeThing(item);
            if (carrier?.inventory?.innerContainer == null || !carrier.inventory.innerContainer.TryAdd(made))
                GenPlace.TryPlaceThing(made, comp.parent.Position, comp.parent.Map, ThingPlaceMode.Near);

            PawnComponentsUtility.AddAndRemoveDynamicComponents(pawn, false);
            CorePersonaUtility.SyncBandwidth(pawn, comp.parent);
        }

        public static IEnumerable<Thing> CarriedImplants(Pawn pawn)
        {
            if (pawn?.carryTracker?.CarriedThing != null && IsMechanitorImplant(pawn.carryTracker.CarriedThing.def))
                yield return pawn.carryTracker.CarriedThing;

            if (pawn?.inventory?.innerContainer == null)
                yield break;

            foreach (Thing thing in pawn.inventory.innerContainer)
            {
                if (IsMechanitorImplant(thing.def))
                    yield return thing;
            }
        }

        public static bool IsMechanitorImplantHediff(Hediff hediff)
        {
            if (hediff?.def == null)
                return false;
            if (hediff.def == ShipCorePersonaDefOf.ShipCorePersonaBound
                || hediff.def == ShipCorePersonaDefOf.ShipCorePersonaBandwidth
                || hediff.def == ShipCorePersonaDefOf.ShipCorePersonaChipOffset
                || hediff.def.defName == CorePersonaUtility.VoyageChipHediff)
                return false;

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                CompProperties_UseEffectInstallImplant props = InstallProps(def);
                if (props?.hediffDef == hediff.def && IsMechanitorImplant(def))
                    return true;
            }

            return false;
        }

        public static IEnumerable<ThingDef> Retrievable(Pawn pawn)
        {
            if (pawn?.health == null)
                yield break;

            HashSet<ThingDef> yielded = new HashSet<ThingDef>();
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
            {
                ThingDef item = ItemToTakeOut(hediff);
                if (item != null && yielded.Add(item))
                    yield return item;
            }
        }

        private static ThingDef ItemToTakeOut(Hediff hediff)
        {
            if (hediff?.def == null)
                return null;

            int level = hediff is Hediff_Level leveled ? leveled.level : 1;
            int previous = level - 1;
            ThingDef best = null;
            float bestMin = float.MinValue;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                CompProperties_UseEffectInstallImplant props = InstallProps(def);
                if (!IsMechanitorImplant(def) || props.hediffDef != hediff.def)
                    continue;
                if (!props.canUpgrade)
                    return def;
                if (props.requiresExistingHediff && previous <= 0)
                    continue;
                if (props.minSeverity > previous || props.maxSeverity <= previous)
                    continue;
                if (props.minSeverity >= bestMin)
                {
                    bestMin = props.minSeverity;
                    best = def;
                }
            }

            return best;
        }

        private static CompProperties_UseEffectInstallImplant InstallProps(ThingDef def)
        {
            return def?.GetCompProperties<CompProperties_UseEffectInstallImplant>();
        }
    }

    public class StoredCoreImplant : IExposable
    {
        public string defName;
        public int level = 1;

        public void ExposeData()
        {
            Scribe_Values.Look(ref defName, "defName");
            Scribe_Values.Look(ref level, "level", 1);
        }
    }
}
