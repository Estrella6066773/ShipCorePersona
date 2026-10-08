using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ShipCorePersona
{
    /// <summary>
    /// 飞船主脑不在地图上时，原版不让她指挥机械族。电脑核心还在，就允许指挥。
    /// 倒地、被俘、精神崩溃仍按原版处理。
    /// </summary>
    [HarmonyPatch(typeof(Pawn_MechanitorTracker), "get_CanControlMechs")]
    public static class Patch_CorePersonaCanControlMechs
    {
        public static void Postfix(ref AcceptanceReport __result, Pawn ___pawn)
        {
            if (__result.Accepted)
                return;

            Pawn pawn = ___pawn;
            if (!CorePersonaUtility.IsPersona(pawn))
                return;
            if (pawn.Downed || pawn.IsPrisoner || pawn.InMentalState)
                return;

            Thing core = CorePersonaUtility.CoreOf(pawn);
            if (core != null && core.Spawned)
                __result = true;
        }
    }

    /// <summary>
    /// 机械族的「选择监督者」在监督者还没生成时是灰的。这里要能重新选中飞船主脑。
    /// </summary>
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.GetMechGizmos))]
    public static class Patch_SelectCorePersonaOverseer
    {
        private static readonly AccessTools.FieldRef<Gizmo, bool> DisabledField =
            AccessTools.FieldRefAccess<Gizmo, bool>("disabled");

        public static void Postfix(Pawn mech, ref IEnumerable<Gizmo> __result)
        {
            Pawn overseer = mech.GetOverseer();
            if (!CorePersonaUtility.IsPersona(overseer))
                return;

            string label = "CommandSelectOverseer".Translate();
            IEnumerable<Gizmo> original = __result;
            __result = Enable(original, label);
        }

        private static IEnumerable<Gizmo> Enable(IEnumerable<Gizmo> gizmos, string selectLabel)
        {
            foreach (Gizmo gizmo in gizmos)
            {
                if (gizmo is Command command && command.defaultLabel == selectLabel && DisabledField(command))
                {
                    DisabledField(command) = false;
                    command.disabledReason = null;
                }

                yield return gizmo;
            }
        }
    }

    /// <summary>
    /// 飞船主脑的手术清单里只留机械师植入物。
    /// </summary>
    [HarmonyPatch(typeof(RecipeWorker), nameof(RecipeWorker.AvailableOnNow))]
    public static class Patch_SurgeryOnlyImplants
    {
        public static void Postfix(RecipeWorker __instance, Thing thing, ref bool __result)
        {
            if (!__result)
                return;
            if (thing is not Pawn pawn || !CorePersonaUtility.IsPersona(pawn))
                return;
            if (__instance is Recipe_InstallCorePersonaImplant)
                return;

            __result = false;
        }
    }

    /// <summary>
    /// 飞船主脑不能离开电脑核心。
    /// 释放能力和原地等待这两类工作要放行，否则能力放不出来，征召也会报错。
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_PersonaCannotAct
    {
        private static readonly HashSet<string> Allowed =
            new HashSet<string> { "RepairMechRemote", "Wait", "Wait_Combat", "Wait_MaintainPosture" };

        public static bool Prefix(Pawn ___pawn, Job newJob)
        {
            if (!CorePersonaUtility.IsPersona(___pawn))
                return true;
            if (newJob?.ability != null)
                return true;

            string defName = newJob?.def?.defName;
            return defName != null && Allowed.Contains(defName);
        }
    }

    /// <summary>
    /// 飞船主脑画在飞船电脑核心的中心。她的逻辑位置仍是核心所占的格子，能力用那个格子计算距离。
    /// </summary>
    [HarmonyPatch(typeof(Pawn), "get_DrawPos")]
    public static class Patch_PersonaDrawsInsideCore
    {
        public static void Postfix(Pawn __instance, ref Vector3 __result)
        {
            if (!CorePersonaUtility.IsPersona(__instance))
                return;

            Thing core = CorePersonaUtility.CoreOf(__instance);
            if (core == null || !core.Spawned)
                return;

            float altitude = __result.y;
            __result = core.DrawPos;
            __result.y = altitude;
        }
    }

    /// <summary>
    /// 飞船主脑放能力时不检查视线，距离仍要在能力范围内。
    /// 她在建筑里，建筑本身会挡住视线，所以这里直接放过视线检查。
    /// 开始施放时，原版另外调用 TryFindShootLineFromTo，不走 CanHitTargetFrom，所以两处都要放过。
    /// </summary>
    [HarmonyPatch(typeof(Verb), nameof(Verb.CanHitTargetFrom))]
    public static class Patch_PersonaIgnoresSight
    {
        public static bool Prefix(Verb __instance, IntVec3 root, LocalTargetInfo targ, ref bool __result)
        {
            if (__instance.caster is not Pawn pawn || !CorePersonaUtility.IsPersona(pawn))
                return true;

            __result = InRange(pawn, __instance, root, targ);
            return false;
        }

        internal static bool InRange(Pawn pawn, Verb verb, IntVec3 root, LocalTargetInfo targ)
        {
            Map map = pawn.MapHeld;
            if (map == null || !targ.IsValid || (targ.HasThing && targ.Thing.Map != map))
                return false;

            float range = verb.EffectiveRange;
            return root.DistanceToSquared(targ.Cell) <= range * range;
        }

        internal static bool NearCore(Pawn pawn, LocalTargetInfo targ)
        {
            Thing core = CorePersonaUtility.CoreOf(pawn);
            Map map = pawn.MapHeld;
            if (core == null || !core.Spawned || map == null || !targ.IsValid || (targ.HasThing && targ.Thing.Map != map))
                return false;

            IntVec3 closest = core.OccupiedRect().ClosestCellTo(targ.Cell);
            return closest.DistanceToSquared(targ.Cell) <= 2;
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryFindShootLineFromTo))]
    public static class Patch_PersonaShootLineIgnoresSight
    {
        public static bool Prefix(Verb __instance, IntVec3 root, LocalTargetInfo targ, ref ShootLine resultingLine, bool ignoreRange, ref bool __result)
        {
            if (__instance.caster is not Pawn pawn || !CorePersonaUtility.IsPersona(pawn))
                return true;

            if (!ignoreRange && !CanUse(__instance, pawn, root, targ))
            {
                resultingLine = new ShootLine(root, targ.Cell);
                __result = false;
                return false;
            }

            resultingLine = new ShootLine(root, targ.Cell);
            __result = true;
            return false;
        }

        private static bool CanUse(Verb verb, Pawn pawn, IntVec3 root, LocalTargetInfo targ)
        {
            if (verb.EffectiveRange <= 0f)
                return Patch_PersonaIgnoresSight.NearCore(pawn, targ);
            return Patch_PersonaIgnoresSight.InRange(pawn, verb, root, targ);
        }
    }

    /// <summary>
    /// 近身能力原版要求人走到目标旁边。人站在建筑里走不出去，于是每个目标都选不中。
    /// 挨着电脑核心的目标算够得着。有射程的能力仍按距离判断，不看视线。
    /// </summary>
    [HarmonyPatch(typeof(Verb_CastAbility), nameof(Verb_CastAbility.ValidateTarget))]
    public static class Patch_PersonaAbilityTarget
    {
        public static bool Prefix(Verb_CastAbility __instance, LocalTargetInfo target, bool showMessages, ref bool __result)
        {
            Pawn pawn = __instance.ability?.pawn;
            if (!CorePersonaUtility.IsPersona(pawn) || __instance.EffectiveRange > 0f)
                return true;

            if (!Patch_PersonaIgnoresSight.NearCore(pawn, target) || !__instance.IsApplicableTo(target, showMessages))
            {
                __result = false;
                return false;
            }

            List<CompAbilityEffect> effects = __instance.ability.EffectComps;
            for (int i = 0; i < effects.Count; i++)
            {
                if (!effects[i].Valid(target, showMessages))
                {
                    __result = false;
                    return false;
                }
            }

            __result = true;
            return false;
        }
    }

    /// <summary>
    /// 原版找施法格子时，会挑一个看得见目标的位置，人就会走出建筑。
    /// 飞船主脑就在当前格子施放。
    /// </summary>
    [HarmonyPatch(typeof(CastPositionFinder), nameof(CastPositionFinder.TryFindCastPosition))]
    public static class Patch_PersonaCastsFromCore
    {
        public static bool Prefix(CastPositionRequest newReq, ref IntVec3 dest, ref bool __result)
        {
            if (newReq.caster is not Pawn pawn || !CorePersonaUtility.IsPersona(pawn))
                return true;
            if (newReq.verb == null || !newReq.verb.CanHitTargetFrom(pawn.Position, newReq.target))
            {
                __result = false;
                return false;
            }

            dest = pawn.Position;
            __result = true;
            return false;
        }
    }
}
