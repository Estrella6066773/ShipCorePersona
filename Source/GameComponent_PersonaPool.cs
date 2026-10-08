using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 飞船主脑的专属人物池。非激活时人在这里，不进世界人物池，事件抽不到她。
    /// 关联的人格核心还在就留着。人格核心没了才删除。
    /// </summary>
    public class GameComponent_PersonaPool : GameComponent, IThingHolder
    {
        private ThingOwner<Pawn> pawns;
        private List<int> mindIds = new List<int>();
        private List<int> buildingIds = new List<int>();

        public GameComponent_PersonaPool(Game game)
        {
            pawns = new ThingOwner<Pawn>(this);
        }

        public IThingHolder ParentHolder => null;

        public static GameComponent_PersonaPool Get()
        {
            return Current.Game?.GetComponent<GameComponent_PersonaPool>();
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return pawns;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref pawns, "pawns", this);
            Scribe_Collections.Look(ref mindIds, "mindIds", LookMode.Value);
            Scribe_Collections.Look(ref buildingIds, "buildingIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (pawns == null)
                    pawns = new ThingOwner<Pawn>(this);
                if (mindIds == null)
                    mindIds = new List<int>();
                if (buildingIds == null)
                    buildingIds = new List<int>();
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                AlignLists();
        }

        public override void GameComponentTick()
        {
            if (pawns == null || Find.TickManager == null || Find.TickManager.TicksGame % 60 != 0)
                return;

            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Destroyed)
                {
                    RemoveAt(i);
                    continue;
                }

                int mindId = mindIds[i];
                if (mindId != 0)
                {
                    if (!VoyageCompat.MindStillExists(mindId))
                        DestroyAt(i);
                    continue;
                }

                if (!BuildingAlive(buildingIds[i]))
                    DestroyAt(i);
            }
        }

        public static bool Contains(Pawn pawn)
        {
            return Get()?.IndexOf(pawn) >= 0;
        }

        public static void Park(Pawn pawn, int mindId, int buildingId)
        {
            Get()?.Hold(pawn, mindId, buildingId);
        }

        public static Pawn Take(int mindId, int buildingId)
        {
            return Get()?.Release(mindId, buildingId);
        }

        public static void DestroyPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed)
                return;

            GameComponent_PersonaPool pool = Get();
            int index = pool?.IndexOf(pawn) ?? -1;
            if (index >= 0)
                pool.RemoveAt(index);
            else if (pawn.holdingOwner != null)
                pawn.holdingOwner.Remove(pawn);

            if (Find.WorldPawns != null && Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);
            if (pawn.Spawned)
                pawn.DeSpawn();
            pawn.Destroy(DestroyMode.Vanish);
        }

        public static void ReleaseMind(int mindId)
        {
            GameComponent_PersonaPool pool = Get();
            if (pool == null || mindId == 0)
                return;

            for (int i = pool.pawns.Count - 1; i >= 0; i--)
            {
                if (pool.mindIds[i] == mindId)
                    pool.DestroyAt(i);
            }
        }

        public static void ReleaseBuilding(int buildingId)
        {
            GameComponent_PersonaPool pool = Get();
            if (pool == null || buildingId == 0)
                return;

            for (int i = pool.pawns.Count - 1; i >= 0; i--)
            {
                if (pool.buildingIds[i] == buildingId && pool.mindIds[i] == 0)
                    pool.DestroyAt(i);
            }
        }

        private void Hold(Pawn pawn, int mindId, int buildingId)
        {
            if (pawn == null || pawn.Destroyed)
                return;

            int existing = IndexOf(pawn);
            if (existing >= 0)
            {
                mindIds[existing] = mindId;
                buildingIds[existing] = buildingId;
                PullOffMap(pawn);
                return;
            }

            if (pawn.holdingOwner != null)
                pawn.holdingOwner.Remove(pawn);
            PullOffMap(pawn);
            if (!pawns.TryAdd(pawn))
            {
                Log.Error("[ShipCorePersona] 没能把飞船主脑放进专属人物池。");
                return;
            }

            mindIds.Add(mindId);
            buildingIds.Add(buildingId);
            PullOffMap(pawn);
        }

        private Pawn Release(int mindId, int buildingId)
        {
            int index = FindIndex(mindId, buildingId);
            if (index < 0)
                return null;

            Pawn pawn = pawns[index];
            RemoveAt(index);
            if (pawn != null && Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);
            return pawn;
        }

        private int FindIndex(int mindId, int buildingId)
        {
            if (mindId != 0)
            {
                for (int i = 0; i < mindIds.Count && i < pawns.Count; i++)
                {
                    if (mindIds[i] == mindId)
                        return i;
                }
            }

            if (buildingId == 0)
                return -1;

            for (int i = 0; i < buildingIds.Count && i < pawns.Count; i++)
            {
                if (buildingIds[i] == buildingId)
                    return i;
            }

            return -1;
        }

        private int IndexOf(Pawn pawn)
        {
            if (pawns == null || pawn == null)
                return -1;

            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i] == pawn)
                    return i;
            }

            return -1;
        }

        private void DestroyAt(int index)
        {
            Pawn pawn = index >= 0 && index < pawns.Count ? pawns[index] : null;
            RemoveAt(index);
            if (pawn == null || pawn.Destroyed)
                return;
            if (Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);
            if (pawn.Spawned)
                pawn.DeSpawn();
            pawn.Destroy(DestroyMode.Vanish);
        }

        private void RemoveAt(int index)
        {
            if (index < 0 || index >= pawns.Count)
                return;

            Pawn pawn = pawns[index];
            if (pawn != null && !pawn.Destroyed)
                pawns.Remove(pawn);
            if (index < mindIds.Count)
                mindIds.RemoveAt(index);
            if (index < buildingIds.Count)
                buildingIds.RemoveAt(index);
        }

        private static void PullOffMap(Pawn pawn)
        {
            if (pawn.Spawned)
            {
                pawn.jobs?.StopAll(false);
                pawn.DeSpawn();
            }

            if (Find.WorldPawns != null && Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);
        }

        private void AlignLists()
        {
            while (mindIds.Count < pawns.Count)
                mindIds.Add(0);
            while (buildingIds.Count < pawns.Count)
                buildingIds.Add(0);
            while (mindIds.Count > pawns.Count)
                mindIds.RemoveAt(mindIds.Count - 1);
            while (buildingIds.Count > pawns.Count)
                buildingIds.RemoveAt(buildingIds.Count - 1);
        }

        private static bool BuildingAlive(int thingId)
        {
            if (thingId == 0)
                return false;

            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Ship_ComputerCore");
            foreach (Map map in Find.Maps)
            {
                if (def != null)
                {
                    List<Thing> cores = map.listerThings.ThingsOfDef(def);
                    for (int i = 0; i < cores.Count; i++)
                    {
                        if (cores[i].thingIDNumber == thingId && !cores[i].Destroyed)
                            return true;
                    }
                }

                List<Thing> minified = map.listerThings.ThingsInGroup(ThingRequestGroup.MinifiedThing);
                for (int i = 0; i < minified.Count; i++)
                {
                    if (minified[i] is MinifiedThing mini && mini.InnerThing != null && mini.InnerThing.thingIDNumber == thingId && !mini.InnerThing.Destroyed)
                        return true;
                }
            }

            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                foreach (Thing thing in caravan.AllThings)
                {
                    if (thing.thingIDNumber == thingId && !thing.Destroyed)
                        return true;
                    if (thing is MinifiedThing mini && mini.InnerThing != null && mini.InnerThing.thingIDNumber == thingId && !mini.InnerThing.Destroyed)
                        return true;
                }
            }

            return false;
        }
    }
}
