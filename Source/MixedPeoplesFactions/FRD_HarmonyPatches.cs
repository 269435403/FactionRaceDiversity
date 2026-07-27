using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MixedPeoplesFactions
{
    public static class FRD_HarmonyBootstrap
    {
        public const string HarmonyId = "yyyyy.factionracediversity";
        private static bool patched;

        public static void Apply()
        {
            if (patched)
            {
                return;
            }
            new Harmony(HarmonyId).PatchAll(Assembly.GetExecutingAssembly());
            patched = true;
        }
    }

    [HarmonyPatch(typeof(PawnGroupKindWorker), nameof(PawnGroupKindWorker.GeneratePawns), new[]
    {
        typeof(PawnGroupMakerParms), typeof(PawnGroupMaker), typeof(bool)
    })]
    public static class FRD_Patch_PawnGroupKindWorker_GeneratePawns
    {
        public static void Prefix(PawnGroupMakerParms parms)
        {
            FRD_PawnGroupContext.Push(parms);
        }

        public static Exception Finalizer(Exception __exception)
        {
            FRD_PawnGroupContext.Pop();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), "GenerateOrRedressPawnInternal")]
    public static class FRD_Patch_GenerateOrRedressPawnInternal
    {
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(ref PawnGenerationRequest request)
        {
            PawnGenerationRequest originalRequest = request;
            try
            {
                if (FRD_RaceService.TryApplyAtomicSelection(ref request))
                {
                    request.ValidateAndFix();
                }
            }
            catch (Exception exception)
            {
                request = originalRequest;
                Log.ErrorOnce("[FactionRaceDiversity] Race and xenotype selection failed; the original request was restored. " + exception, 174832501);
            }
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    public static class FRD_Patch_GeneratePawn_Diagnostics
    {
        public static void Prefix()
        {
            FRD_PawnGroupContext.EnterPawnGeneration();
        }

        public static void Postfix(PawnGenerationRequest request, Pawn __result)
        {
            Faction faction = FRD_PawnGroupContext.EffectiveFactionFor(request.Faction);
            if (__result?.def != null
                && faction?.def != null
                && FRD_RaceRegistry.IsRealPawnRace(__result.def)
                && !FRD_RaceService.IsConfiguredRaceAllowed(faction, __result.def))
            {
                FRD_Diagnostics.RecordUnexpectedRace(faction.def.defName, request.KindDef, __result.def);
            }
        }

        public static Exception Finalizer(Exception __exception)
        {
            FRD_PawnGroupContext.ExitPawnGeneration();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.XenotypesAvailableFor))]
    [HarmonyAfter("rimworld.erdelf.alien_race.main")]
    public static class FRD_Patch_XenotypesAvailableFor
    {
        public static void Postfix(PawnKindDef kind, FactionDef factionDef, Faction faction, ref Dictionary<XenotypeDef, float> __result)
        {
            FactionDef effectiveFaction = faction?.def ?? factionDef;
            if (kind == null
                || effectiveFaction == null
                || !kind.useFactionXenotypes
                || !MPF_Injector.IsXenotypeOverrideEnabled(effectiveFaction))
            {
                return;
            }

            try
            {
                Dictionary<XenotypeDef, float> cleaned = (__result ?? new Dictionary<XenotypeDef, float>())
                    .Where(pair => pair.Key != null
                        && pair.Value > 0f
                        && !float.IsNaN(pair.Value)
                        && !float.IsInfinity(pair.Value)
                        && FRD_XenotypeService.IsSafeForRace(pair.Key, kind.race, false))
                    .ToDictionary(pair => pair.Key, pair => pair.Value);

                float total = cleaned.Values.Sum();
                if (total > 0f)
                {
                    foreach (XenotypeDef key in cleaned.Keys.ToList())
                    {
                        cleaned[key] /= total;
                    }
                    __result = cleaned;
                    return;
                }

                XenotypeDef fallback = MPF_Injector.FindSafeFallbackXenotype(effectiveFaction, kind.race);
                if (fallback != null)
                {
                    cleaned[fallback] = 1f;
                    __result = cleaned;
                }
            }
            catch (Exception exception)
            {
                Log.ErrorOnce("[FactionRaceDiversity] Xenotype safety normalization failed; the previously filtered result was preserved. " + exception, 174832502);
            }
        }
    }
    [HarmonyPatch(typeof(RaidStrategyWorker_WithRequiredPawnKinds), nameof(RaidStrategyWorker_WithRequiredPawnKinds.CanUseWith))]
    public static class FRD_Patch_RequiredRaidStrategy_CanUseWith
    {
        public static void Postfix(RaidStrategyWorker_WithRequiredPawnKinds __instance, IncidentParms parms, ref bool __result)
        {
            if (__result && parms?.faction != null)
            {
                __result = FRD_RaceService.ConfiguredRacesSupportRequiredRaidRole(parms.faction, __instance);
            }
        }
    }

}
