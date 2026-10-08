using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 生成飞船主脑。幼年背景是人格核心，成年背景是飞船主脑。
    /// 10 点手工写在成年背景上，生成时就会加上，这里不再改技能。
    /// 头型和发型交给生成器。性别在生成请求里定为女性。
    /// 体型从「女性」「纤细」「魁梧」里取一个，不取「肥胖」。
    /// 生成请求不带发色、肤色和衣物材料。发色和肤色等生成完再写上。衣服换成一套合成纤维的裤子和衬衫。
    /// 名字不在这里取。调用方沿用人格核心上已有的名字。
    /// </summary>
    public static class PersonaAppearance
    {
        private static readonly Color HairColor = new Color(0.55f, 0.68f, 0.74f);
        private static readonly Color SkinColor = new Color(0.95f, 0.86f, 0.78f);

        public static Pawn Generate()
        {
            PawnKindDef kind = ShipCorePersonaDefOf.ShipCorePersona;
            var request = new PawnGenerationRequest(kind, Faction.OfPlayer, PawnGenerationContext.NonPlayer);
            request.FixedGender = Gender.Female;
            request.ForceBodyType = BodyTypeWithoutFat();
            request.MustBeCapableOfViolence = false;
            request.AllowDead = false;
            request.AllowDowned = false;
            request.CanGeneratePawnRelations = false;
            request.ForbidAnyTitle = true;
            request.ForceGenerateNewPawn = true;
            request.OnlyUseForcedBackstories = true;
            request.AllowedDevelopmentalStages = DevelopmentalStage.Adult;
            if (ModsConfig.BiotechActive)
                request.ForcedXenotype = XenotypeDefOf.Baseliner;
            if (Faction.OfPlayer?.ideos?.PrimaryIdeo != null)
                request.FixedIdeo = Faction.OfPlayer.ideos.PrimaryIdeo;

            Pawn persona = PawnGenerator.GeneratePawn(request);
            EnsureIdentity(persona);
            ApplyAppearance(persona);
            ClearInjuriesAndTraits(persona);
            EnsureColonistComponents(persona);
            persona.health.AddHediff(ShipCorePersonaDefOf.ShipCorePersonaBound);
            return persona;
        }

        public static void EnsureIdentity(Pawn persona)
        {
            if (persona?.story == null)
                return;

            BackstoryDef childhood = DefDatabase<BackstoryDef>.GetNamedSilentFail("ShipCorePersona_Childhood");
            BackstoryDef adulthood = DefDatabase<BackstoryDef>.GetNamedSilentFail("ShipCorePersona_Adulthood");
            if (childhood != null)
                persona.story.Childhood = childhood;
            if (adulthood != null)
                persona.story.Adulthood = adulthood;
        }

        /// <summary>
        /// 名字跟人格核心走。核心上已经有名字，就用那个名字。
        /// 芯片还在核心里、名字还空着时，让核心取一次名字，角色用同一个。
        /// 没有 Gravship Voyage 时，用幼年背景的标题「人格核心」。
        /// </summary>
        public static void ApplyCoreName(Pawn persona, Thing core)
        {
            if (persona == null)
                return;

            string name = VoyageCompat.CoreName(core);
            if (string.IsNullOrEmpty(name))
            {
                BackstoryDef childhood = DefDatabase<BackstoryDef>.GetNamedSilentFail("ShipCorePersona_Childhood");
                name = childhood?.title;
            }

            if (string.IsNullOrEmpty(name))
                return;
            if (persona.Name is NameSingle current && current.Name == name)
                return;

            persona.Name = new NameSingle(name);
        }

        private static void ApplyAppearance(Pawn persona)
        {
            if (persona.story == null)
                return;

            persona.story.HairColor = HairColor;
            persona.story.skinColorOverride = SkinColor;
            EnsureBodyType(persona);
            WearSynthreadSet(persona);
            persona.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        /// <summary>
        /// 生成请求里的 ForceBodyType 会被后来的基因改掉，肥胖就从这里再换掉。
        /// </summary>
        public static void EnsureBodyType(Pawn persona)
        {
            if (persona?.story == null)
                return;
            if (persona.story.bodyType != null && persona.story.bodyType != BodyTypeDefOf.Fat)
                return;

            persona.story.bodyType = BodyTypeWithoutFat();
            persona.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        public static void EnsureHologram(Pawn persona)
        {
            Hediff bound = persona?.health?.hediffSet?.GetFirstHediffOfDef(ShipCorePersonaDefOf.ShipCorePersonaBound);
            if (bound == null || bound.TryGetComp<HediffComp_Invisibility>() != null)
                return;

            persona.health.RemoveHediff(bound);
            persona.health.AddHediff(ShipCorePersonaDefOf.ShipCorePersonaBound);
        }

        private static BodyTypeDef BodyTypeWithoutFat()
        {
            BodyTypeDef[] allowed = { BodyTypeDefOf.Female, BodyTypeDefOf.Thin, BodyTypeDefOf.Hulk };
            return allowed[Rand.Range(0, allowed.Length)];
        }

        private static void WearSynthreadSet(Pawn persona)
        {
            if (persona.apparel == null)
                return;

            ThingDef synthread = DefDatabase<ThingDef>.GetNamedSilentFail("Synthread");
            if (synthread == null)
                return;

            persona.apparel.DestroyAll(DestroyMode.Vanish);
            Wear(persona, "Apparel_Pants", synthread);
            Wear(persona, "Apparel_CollarShirt", synthread);
        }

        private static void Wear(Pawn persona, string defName, ThingDef stuff)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
                return;

            var apparel = (Apparel)ThingMaker.MakeThing(def, stuff);
            persona.apparel.Wear(apparel, dropReplacedApparel: false, locked: true);
        }

        private static void ClearInjuriesAndTraits(Pawn persona)
        {
            if (persona.story?.traits != null)
            {
                List<Trait> traits = persona.story.traits.allTraits.ToList();
                for (int i = 0; i < traits.Count; i++)
                    persona.story.traits.RemoveTrait(traits[i]);
            }

            if (persona.health == null)
                return;

            List<Hediff> bad = persona.health.hediffSet.hediffs.Where(hediff => hediff.def.isBad).ToList();
            for (int i = 0; i < bad.Count; i++)
                persona.health.RemoveHediff(bad[i]);
        }

        private static void EnsureColonistComponents(Pawn persona)
        {
            PawnComponentsUtility.AddComponentsForSpawn(persona);
            PawnComponentsUtility.AddAndRemoveDynamicComponents(persona, false);

            if (persona.drafter == null)
                persona.drafter = new Pawn_DraftController(persona);
            if (persona.playerSettings == null)
                persona.playerSettings = new Pawn_PlayerSettings(persona);
            if (persona.workSettings == null)
                persona.workSettings = new Pawn_WorkSettings(persona);

            persona.workSettings.DisableAll();
            persona.playerSettings.hostilityResponse = HostilityResponseMode.Ignore;
        }
    }
}
