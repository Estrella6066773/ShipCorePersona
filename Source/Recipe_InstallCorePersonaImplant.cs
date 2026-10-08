using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 只允许给核心人格安装机械师植入体，并且按植入体自己的等级上限升级。
    ///
    /// 联动关系：健康页能列出哪些手术，先看本方法的 AvailableOnNow，
    /// 再由 <see cref="Patch_SurgeryOnlyImplants"/> 把其他手术从核心人格身上拿掉。
    /// 真正动手的是 <see cref="JobDriver_OperateCorePersona"/>，它调用 ApplyOnPawn。
    ///
    /// 注意：标准控制子链最高 3 级，高级子链从 3 级继续加到 6 级，和原版自安装规则一致。
    /// 其他植入体的上限用健康状态自己的 maxSeverity。
    /// </summary>
    public class Recipe_InstallCorePersonaImplant : Recipe_Surgery
    {
        private const int StandardSublinkCap = 3;

        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            if (thing is not Pawn pawn || !CorePersonaUtility.IsPersona(pawn))
                return false;
            BodyPartRecord brain = CorePersonaUtility.BrainOf(pawn);
            if (part != null && brain != null && part != brain)
                return false;
            if (recipe.addsHediff == null)
                return false;

            return CanInstallAnother(pawn);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (!CanInstallAnother(pawn))
                return;

            if (part == null)
                part = CorePersonaUtility.BrainOf(pawn);

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(recipe.addsHediff);
            if (existing is Hediff_Level level)
                level.SetLevelTo(level.level + 1);
            else
                pawn.health.AddHediff(HediffMaker.MakeHediff(recipe.addsHediff, pawn, part), part);

            PawnComponentsUtility.AddAndRemoveDynamicComponents(pawn, false);
            pawn.mechanitor?.Notify_BandwidthChanged();
        }

        private bool CanInstallAnother(Pawn pawn)
        {
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(recipe.addsHediff);
            int level = existing is Hediff_Level leveled ? leveled.level : 0;

            if (recipe.defName == "ShipCorePersona_InstallControlSublink")
                return level < StandardSublinkCap;
            if (recipe.defName == "ShipCorePersona_InstallControlSublinkHigh")
                return level >= StandardSublinkCap && level < (int)recipe.addsHediff.maxSeverity;

            int cap = (int)recipe.addsHediff.maxSeverity;
            if (cap <= 0)
                cap = 1;
            return level < cap;
        }
    }
}
