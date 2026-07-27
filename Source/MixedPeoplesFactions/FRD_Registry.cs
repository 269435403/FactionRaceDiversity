using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MixedPeoplesFactions
{
    public sealed class FRD_FactionRecord
    {
        public FactionDef Def;
        public List<PawnKindDef> SourceKinds = new List<PawnKindDef>();
        public bool SupportsXenotypes;
        public bool SupportsRaces;
        public string UnsupportedReasonKey;

        public string Label
        {
            get
            {
                string label = Def?.LabelCap.ToString();
                return string.IsNullOrEmpty(label) ? "Unknown".Translate().ToString() : label;
            }
        }

        public bool IsSupported => SupportsXenotypes || SupportsRaces;
    }

    public static class FRD_FactionRegistry
    {
        private static readonly List<FRD_FactionRecord> records = new List<FRD_FactionRecord>();
        private static readonly Dictionary<string, FRD_FactionRecord> byDefName = new Dictionary<string, FRD_FactionRecord>();

        public static IReadOnlyList<FRD_FactionRecord> Records => records;

        public static void Refresh()
        {
            FRD_RaceRegistry.Refresh();
            records.Clear();
            byDefName.Clear();

            foreach (FactionDef faction in DefDatabase<FactionDef>.AllDefsListForReading)
            {
                if (faction == null || string.IsNullOrEmpty(faction.defName))
                {
                    continue;
                }

                FRD_FactionRecord record = BuildRecord(faction);
                records.Add(record);
                byDefName[faction.defName] = record;
            }

            records.Sort(delegate(FRD_FactionRecord a, FRD_FactionRecord b)
            {
                int labelCompare = string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase);
                return labelCompare != 0 ? labelCompare : string.Compare(a.Def.defName, b.Def.defName, StringComparison.OrdinalIgnoreCase);
            });

            FRD_CompatibilityRegistry.Refresh();
        }

        public static FRD_FactionRecord Get(string defName)
        {
            if (string.IsNullOrEmpty(defName))
            {
                return null;
            }
            byDefName.TryGetValue(defName, out FRD_FactionRecord record);
            return record;
        }

        private static FRD_FactionRecord BuildRecord(FactionDef faction)
        {
            HashSet<PawnKindDef> kinds = new HashSet<PawnKindDef>();
            AddKind(kinds, faction.basicMemberKind);
            if (faction.pawnGroupMakers != null)
            {
                foreach (PawnGroupMaker maker in faction.pawnGroupMakers)
                {
                    if (maker == null)
                    {
                        continue;
                    }
                    AddOptions(kinds, maker.options);
                    AddOptions(kinds, maker.traders);
                    AddOptions(kinds, maker.guards);
                }
            }

            List<PawnKindDef> safeKinds = kinds.Where(FRD_RaceRegistry.IsOrdinarySafeKind)
                .OrderBy(kind => kind.defName, StringComparer.OrdinalIgnoreCase).ToList();
            FRD_FactionRecord record = new FRD_FactionRecord { Def = faction, SourceKinds = safeKinds };

            if (!faction.humanlikeFaction)
            {
                record.UnsupportedReasonKey = "FRD_UnsupportedNonHumanlike";
            }
            else if (kinds.Count == 0)
            {
                record.UnsupportedReasonKey = "FRD_UnsupportedNoPawnKinds";
            }
            else if (safeKinds.Count == 0)
            {
                record.UnsupportedReasonKey = "FRD_UnsupportedNoHumanlikePawnKinds";
            }
            else
            {
                record.SupportsXenotypes = true;
                record.SupportsRaces = true;
            }
            return record;
        }

        private static void AddKind(HashSet<PawnKindDef> kinds, PawnKindDef kind)
        {
            if (kind != null)
            {
                kinds.Add(kind);
            }
        }

        private static void AddOptions(HashSet<PawnKindDef> kinds, List<PawnGenOption> options)
        {
            if (options == null)
            {
                return;
            }
            foreach (PawnGenOption option in options)
            {
                AddKind(kinds, option?.kind);
            }
        }
    }

    public static class FRD_RaceRegistry
    {
        private static readonly List<ThingDef> humanlikeRaces = new List<ThingDef>();
        private static readonly Dictionary<string, ThingDef> raceByDefName = new Dictionary<string, ThingDef>();
        private static readonly Dictionary<ThingDef, List<PawnKindDef>> kindsByRace = new Dictionary<ThingDef, List<PawnKindDef>>();

        public static IReadOnlyList<ThingDef> HumanlikeRaces => humanlikeRaces;

        public static void Refresh()
        {
            humanlikeRaces.Clear();
            raceByDefName.Clear();
            kindsByRace.Clear();

            foreach (PawnKindDef kind in DefDatabase<PawnKindDef>.AllDefsListForReading)
            {
                if (!IsOrdinarySafeKind(kind))
                {
                    continue;
                }
                if (!kindsByRace.TryGetValue(kind.race, out List<PawnKindDef> list))
                {
                    list = new List<PawnKindDef>();
                    kindsByRace[kind.race] = list;
                }
                list.Add(kind);
            }

            foreach (KeyValuePair<ThingDef, List<PawnKindDef>> pair in kindsByRace.ToList())
            {
                ThingDef race = pair.Key;
                List<PawnKindDef> kinds = pair.Value;
                kinds.Sort((a, b) => string.Compare(a.defName, b.defName, StringComparison.OrdinalIgnoreCase));
                if (!IsRealPawnRace(race) || !kinds.Any(kind => !kind.factionLeader && !kind.isBoss))
                {
                    kindsByRace.Remove(race);
                    continue;
                }
                humanlikeRaces.Add(race);
                raceByDefName[race.defName] = race;
            }

            FRD_XenotypeService.ClearCaches();

            humanlikeRaces.Sort(delegate(ThingDef a, ThingDef b)
            {
                int labelCompare = string.Compare(a.LabelCap.ToString(), b.LabelCap.ToString(), StringComparison.CurrentCultureIgnoreCase);
                return labelCompare != 0 ? labelCompare : string.Compare(a.defName, b.defName, StringComparison.OrdinalIgnoreCase);
            });
        }

        public static bool IsRealPawnRace(ThingDef race)
        {
            return race != null
                && !string.IsNullOrEmpty(race.defName)
                && race.category == ThingCategory.Pawn
                && race.thingClass != null
                && typeof(Pawn).IsAssignableFrom(race.thingClass)
                && !race.IsCorpse
                && race.race != null
                && race.race.Humanlike;
        }

        public static bool IsOrdinarySafeKind(PawnKindDef kind)
        {
            return kind != null
                && IsRealPawnRace(kind.race)
                && kind.mutant == null
                && !(kind is CreepJoinerFormKindDef)
                && !kind.isBoss;
        }

        public static ThingDef GetRace(string defName)
        {
            if (string.IsNullOrEmpty(defName))
            {
                return null;
            }
            raceByDefName.TryGetValue(defName, out ThingDef race);
            return race;
        }

        public static IReadOnlyList<PawnKindDef> GetKinds(ThingDef race)
        {
            return race != null && kindsByRace.TryGetValue(race, out List<PawnKindDef> list) ? list : Array.Empty<PawnKindDef>();
        }

        public static bool HasCandidateKinds(ThingDef race)
        {
            return race != null && kindsByRace.TryGetValue(race, out List<PawnKindDef> list) && list.Any(kind => !kind.factionLeader && !kind.isBoss);
        }

        public static ThingDef DefaultRaceFor(FactionDef faction)
        {
            FRD_FactionRecord record = faction == null ? null : FRD_FactionRegistry.Get(faction.defName);
            PawnKindDef kind = faction?.basicMemberKind ?? record?.SourceKinds.FirstOrDefault();
            return IsRealPawnRace(kind?.race) ? kind.race : ThingDefOf.Human;
        }
    }
}


