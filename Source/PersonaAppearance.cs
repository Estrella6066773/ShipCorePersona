using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 生成住在电脑核心里的那一个人格，并写成固定外貌。
    ///
    /// 联动关系：只由 <see cref="CompShipCorePersona.EnsurePersona"/> 调用。
    /// 生成完成后立刻加上「驻留在电脑核心中」，之后的补丁才把这个人当成核心人格。
    ///
    /// 注意：外貌在生成之后强制改写。若改在生成请求里碰运气，头型、发型和肤色仍会随殖民者生成器变化。
    /// 铸造以外的技能在这里清成 0；修理速度读的是铸造技能，不读工作优先级。
    /// </summary>
    public static class PersonaAppearance
    {
        private static readonly string[] Names =
        {
            "Iris", "Helix", "Solace", "Nadir", "Echo", "Marlow", "Soren", "Ada", "Linnea", "Corin"
        };

        public static Pawn Generate()
        {
            PawnKindDef kind = ShipCorePersonaDefOf.ShipCorePersona;
            var request = new PawnGenerationRequest(kind, Faction.OfPlayer, PawnGenerationContext.NonPlayer);
            request.FixedGender = Gender.Female;
            request.MustBeCapableOfViolence = false;
            request.AllowDead = false;
            request.AllowDowned = false;
            request.CanGeneratePawnRelations = false;
            request.ForbidAnyTitle = true;
            request.ForceGenerateNewPawn = true;
            request.AllowedDevelopmentalStages = DevelopmentalStage.Adult;
            if (ModsConfig.BiotechActive)
                request.ForcedXenotype = XenotypeDefOf.Baseliner;
            if (Faction.OfPlayer?.ideos?.PrimaryIdeo != null)
                request.FixedIdeo = Faction.OfPlayer.ideos.PrimaryIdeo;

            Pawn persona = PawnGenerator.GeneratePawn(request);
            persona.Name = new NameSingle(Names[Rand.Range(0, Names.Length)]);
            ApplyAppearance(persona);
            ClearInjuriesAndTraits(persona);
            ApplySkills(persona);
            EnsureColonistComponents(persona);
            persona.health.AddHediff(ShipCorePersonaDefOf.ShipCorePersonaBound);
            return persona;
        }

        private static void ApplyAppearance(Pawn persona)
        {
            if (persona.story == null)
                return;

            persona.story.bodyType = BodyTypeDefOf.Female;
            HeadTypeDef head = DefDatabase<HeadTypeDef>.GetNamedSilentFail("Female_NarrowNormal");
            if (head != null)
                persona.story.headType = head;

            HairDef hair = DefDatabase<HairDef>.GetNamedSilentFail("Cleopatra");
            if (hair != null)
                persona.story.hairDef = hair;

            persona.story.HairColor = new Color(0.55f, 0.68f, 0.74f);
            persona.story.skinColorOverride = new Color(0.95f, 0.86f, 0.78f);

            if (persona.style != null && DefDatabase<BeardDef>.GetNamedSilentFail("NoBeard") is BeardDef beard)
                persona.style.beardDef = beard;

            TryWear(persona, "Apparel_CollarShirt");
            TryWear(persona, "Apparel_Pants");
            persona.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        private static void TryWear(Pawn persona, string defName)
        {
            if (persona.apparel == null)
                return;

            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
                return;

            var apparel = (Apparel)ThingMaker.MakeThing(def, ThingDefOf.Cloth);
            apparel.SetColor(new Color(0.32f, 0.42f, 0.52f));
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

        private static void ApplySkills(Pawn persona)
        {
            if (persona.skills == null)
                return;

            foreach (SkillRecord skill in persona.skills.skills)
            {
                skill.passion = Passion.None;
                skill.Level = skill.def == SkillDefOf.Crafting ? 10 : 0;
            }
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
