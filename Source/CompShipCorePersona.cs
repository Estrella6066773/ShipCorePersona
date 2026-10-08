using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShipCorePersona
{
    public class CompProperties_ShipCorePersona : CompProperties
    {
        public CompProperties_ShipCorePersona()
        {
            compClass = typeof(CompShipCorePersona);
        }
    }

    /// <summary>
    /// 把核心人格放进飞船电脑核心，方式和休眠仓放人相同：人在容器里，不生成到地图上。
    ///
    /// 联动关系：
    /// 点选核心时，原版的「选择其中的人」按钮会画出这个人的头像。
    /// 医生的手术工作由 <see cref="WorkGiver_OperateCorePersona"/> 找到这里的手术清单。
    /// 远程修理的工作不会被地图 ticking，所以本组件在核心每次滴答时推动这个人当前的工作。
    /// 带宽和机械链路的加减在 <see cref="CorePersonaUtility.SyncBandwidth"/>。
    ///
    /// 注意：
    /// 这个人的 Position 要写成核心旁边一格能站立的格子。人并没有生成出来，
    /// 但瞄准和距离用的是 Position 而不是容器位置；若写成核心自己那一格，实心建筑会挡住视线。
    /// 核心被拆掉或摧毁时，人格一起消失，不会掉出一具尸体。
    /// </summary>
    public class CompShipCorePersona : ThingComp, IThingHolder
    {
        private ThingOwner<Pawn> inner;

        public CompShipCorePersona()
        {
            inner = new ThingOwner<Pawn>(this);
        }

        public Pawn Persona => inner != null && inner.Count > 0 ? inner[0] : null;

        public new IThingHolder ParentHolder => parent?.Map;

        public ThingOwner GetDirectlyHeldThings()
        {
            return inner;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (inner == null)
                inner = new ThingOwner<Pawn>(this);

            EnsurePersona();
            PlaceBesideCore();
            CorePersonaUtility.SyncBandwidth(Persona, parent);
            VoyageCompat.ClaimHost(this);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref inner, "personaContainer", this);
            if (Scribe.mode == LoadSaveMode.LoadingVars && inner == null)
                inner = new ThingOwner<Pawn>(this);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            if (inner != null)
            {
                for (int i = inner.Count - 1; i >= 0; i--)
                {
                    Pawn pawn = inner[i];
                    inner.Remove(pawn);
                    if (pawn != null && !pawn.Destroyed)
                        pawn.Destroy(DestroyMode.Vanish);
                }
            }

            base.PostDestroy(mode, previousMap);
        }

        public override void CompTick()
        {
            Pawn persona = Persona;
            if (persona == null || persona.Destroyed || !parent.Spawned)
                return;

            PlaceBesideCore();
            if (parent.IsHashIntervalTick(60))
                CorePersonaUtility.SyncBandwidth(persona, parent);

            try
            {
                if (persona.jobs?.curJob == null && persona.jobs?.jobQueue != null && persona.jobs.jobQueue.Count > 0)
                    persona.jobs.CheckForJobOverride(0f, false);

                if (persona.jobs?.curDriver != null)
                {
                    persona.jobs.JobTrackerTick();
                    persona.jobs.JobTrackerTickInterval(1);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[ShipCorePersona] 推动核心人格的工作时出错：" + ex, persona.thingIDNumber ^ 0x51C0);
            }
        }

        public override string CompInspectStringExtra()
        {
            Pawn persona = Persona;
            if (persona == null)
                return null;

            if (persona.mechanitor == null)
                return "ShipCorePersona_InspectDormant".Translate(persona.LabelShortCap);

            return "ShipCorePersona_Inspect".Translate(
                persona.LabelShortCap,
                persona.mechanitor.UsedBandwidth,
                persona.mechanitor.TotalBandwidth);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            Pawn persona = Persona;
            if (persona == null || parent.Faction != Faction.OfPlayer)
                yield break;

            Gizmo select = Building.SelectContainedItemGizmo(parent, persona);
            if (select != null)
                yield return select;

            if (persona.drafter != null)
            {
                yield return new Command_Toggle
                {
                    defaultLabel = "ShipCorePersona_Draft".Translate(),
                    defaultDesc = "ShipCorePersona_DraftDesc".Translate(),
                    icon = TexCommand.Draft,
                    hotKey = KeyBindingDefOf.Command_ColonistDraft,
                    isActive = () => persona.drafter.Drafted,
                    toggleAction = () => persona.drafter.Drafted = !persona.drafter.Drafted
                };
            }

            if (persona.mechanitor != null)
            {
                foreach (Gizmo gizmo in persona.mechanitor.GetGizmos())
                    yield return gizmo;
            }

            if (persona.abilities != null)
            {
                foreach (Gizmo gizmo in persona.abilities.GetGizmos())
                    yield return gizmo;
            }
        }

        public void EnsurePersona()
        {
            if (Persona != null || parent.Faction != Faction.OfPlayer || Faction.OfPlayer == null)
                return;
            if (!ModsConfig.BiotechActive)
                return;

            Pawn persona = PersonaAppearance.Generate();
            persona.SetFaction(Faction.OfPlayer);
            inner.TryAdd(persona);
            PlaceBesideCore();
        }

        private void PlaceBesideCore()
        {
            Pawn persona = Persona;
            if (persona == null || persona.Spawned || parent.Map == null)
                return;

            IntVec3 cell = parent.Position;
            foreach (IntVec3 adjacent in GenAdj.CellsAdjacent8Way(parent))
            {
                if (adjacent.InBounds(parent.Map) && adjacent.Standable(parent.Map))
                {
                    cell = adjacent;
                    break;
                }
            }

            if (persona.Position != cell)
                persona.Position = cell;
        }
    }
}
