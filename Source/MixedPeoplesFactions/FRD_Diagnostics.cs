using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using Verse;

namespace MixedPeoplesFactions
{
    public sealed class FRD_GenerationCounters
    {
        public long Requests;
        public long SuccessfulRaceChanges;
        public long ManualRaceChanges;
        public long AutomaticRaceChanges;
        public long PreservedOriginalRace;
        public long SpecialRequests;
        public long FixedLeaders;
        public long NoCompatiblePawnKind;
        public long HardForcedXenotypeIncompatible;
        public long NoRaceXenotype;
        public long UnexpectedGeneratedRace;
    }

    public static class FRD_Diagnostics
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, FRD_GenerationCounters> ByFaction = new Dictionary<string, FRD_GenerationCounters>();
        private static readonly Dictionary<string, Dictionary<string, long>> FallbackDetails = new Dictionary<string, Dictionary<string, long>>();

        public static void RecordRequest(string factionDefName)
        {
            Mutate(factionDefName, counters => counters.Requests++);
        }

        public static void RecordSuccess(string factionDefName, bool manual)
        {
            Mutate(factionDefName, counters =>
            {
                counters.SuccessfulRaceChanges++;
                if (manual)
                {
                    counters.ManualRaceChanges++;
                }
                else
                {
                    counters.AutomaticRaceChanges++;
                }
            });
        }

        public static void RecordPreserved(string factionDefName)
        {
            Mutate(factionDefName, counters => counters.PreservedOriginalRace++);
        }

        public static void RecordSpecial(string factionDefName)
        {
            Mutate(factionDefName, counters => counters.SpecialRequests++);
        }

        public static void RecordFixedLeader(string factionDefName)
        {
            Mutate(factionDefName, counters => counters.FixedLeaders++);
        }

        public static void RecordNoCompatibleKind(string factionDefName)
        {
            Mutate(factionDefName, counters => counters.NoCompatiblePawnKind++);
        }

        public static void RecordHardXenotypeIncompatible(string factionDefName)
        {
            Mutate(factionDefName, counters => counters.HardForcedXenotypeIncompatible++);
        }

        public static void RecordNoRaceXenotype(string factionDefName)
        {
            Mutate(factionDefName, counters => counters.NoRaceXenotype++);
        }

        public static void RecordUnexpectedRace(string factionDefName, PawnKindDef requestedKind, ThingDef actualRace)
        {
            Mutate(factionDefName, counters => counters.UnexpectedGeneratedRace++);
            RecordFallbackDetail(factionDefName, "unexpected " + (actualRace?.defName ?? "null"), requestedKind);
        }

        public static void RecordFallbackDetail(string factionDefName, string reason, PawnKindDef kind)
        {
            if (string.IsNullOrEmpty(factionDefName))
            {
                return;
            }
            string key = (reason ?? "unknown") + ":" + (kind?.defName ?? "null");
            lock (Sync)
            {
                if (!FallbackDetails.TryGetValue(factionDefName, out Dictionary<string, long> details))
                {
                    details = new Dictionary<string, long>();
                    FallbackDetails[factionDefName] = details;
                }
                details.TryGetValue(key, out long count);
                details[key] = count + 1;
            }
        }

        public static FRD_GenerationCounters Snapshot(string factionDefName)
        {
            lock (Sync)
            {
                if (!string.IsNullOrEmpty(factionDefName) && ByFaction.TryGetValue(factionDefName, out FRD_GenerationCounters source))
                {
                    return new FRD_GenerationCounters
                    {
                        Requests = source.Requests,
                        SuccessfulRaceChanges = source.SuccessfulRaceChanges,
                        ManualRaceChanges = source.ManualRaceChanges,
                        AutomaticRaceChanges = source.AutomaticRaceChanges,
                        PreservedOriginalRace = source.PreservedOriginalRace,
                        SpecialRequests = source.SpecialRequests,
                        FixedLeaders = source.FixedLeaders,
                        NoCompatiblePawnKind = source.NoCompatiblePawnKind,
                        HardForcedXenotypeIncompatible = source.HardForcedXenotypeIncompatible,
                        NoRaceXenotype = source.NoRaceXenotype,
                        UnexpectedGeneratedRace = source.UnexpectedGeneratedRace
                    };
                }
                return new FRD_GenerationCounters();
            }
        }

        public static IEnumerable<string> FormatLines(string factionDefName)
        {
            FRD_GenerationCounters counters = Snapshot(factionDefName);
            yield return "  generation requests=" + counters.Requests.ToString(CultureInfo.InvariantCulture)
                + ", race changes=" + counters.SuccessfulRaceChanges.ToString(CultureInfo.InvariantCulture)
                + " (manual=" + counters.ManualRaceChanges.ToString(CultureInfo.InvariantCulture)
                + ", automatic=" + counters.AutomaticRaceChanges.ToString(CultureInfo.InvariantCulture) + ")"
                + ", original race kept=" + counters.PreservedOriginalRace.ToString(CultureInfo.InvariantCulture);
            yield return "  preserved reasons: special=" + counters.SpecialRequests.ToString(CultureInfo.InvariantCulture)
                + ", fixed leader=" + counters.FixedLeaders.ToString(CultureInfo.InvariantCulture)
                + ", no compatible PawnKind=" + counters.NoCompatiblePawnKind.ToString(CultureInfo.InvariantCulture)
                + ", hard xenotype incompatible=" + counters.HardForcedXenotypeIncompatible.ToString(CultureInfo.InvariantCulture)
                + ", no race xenotype=" + counters.NoRaceXenotype.ToString(CultureInfo.InvariantCulture)
                + ", unexpected generated race=" + counters.UnexpectedGeneratedRace.ToString(CultureInfo.InvariantCulture);
            lock (Sync)
            {
                if (FallbackDetails.TryGetValue(factionDefName, out Dictionary<string, long> details) && details.Count > 0)
                {
                    yield return "  fallback details: " + string.Join(", ", details.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).Take(8).Select(pair => pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture)));
                }
            }
        }

        private static void Mutate(string factionDefName, System.Action<FRD_GenerationCounters> mutation)
        {
            if (string.IsNullOrEmpty(factionDefName) || mutation == null)
            {
                return;
            }
            lock (Sync)
            {
                if (!ByFaction.TryGetValue(factionDefName, out FRD_GenerationCounters counters))
                {
                    counters = new FRD_GenerationCounters();
                    ByFaction[factionDefName] = counters;
                }
                mutation(counters);
            }
        }
    }
}
