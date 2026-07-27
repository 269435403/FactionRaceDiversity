using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace MixedPeoplesFactions
{
    public static class FRD_HarCompatibility
    {
        private const string HarRestrictionTypeName = "AlienRace.RaceRestrictionSettings";
        private static readonly Dictionary<ThingDef, IReadOnlyList<XenotypeDef>> ExplicitXenotypesByRace = new Dictionary<ThingDef, IReadOnlyList<XenotypeDef>>();
        private static bool initialized;
        private static bool adapterFailed;
        private static MethodInfo canUseXenotypeMethod;
        private static bool failureLogged;

        public static bool AdapterFailed
        {
            get
            {
                EnsureInitialized();
                return adapterFailed;
            }
        }

        public static bool CanUseXenotype(XenotypeDef xenotype, ThingDef race)
        {
            if (xenotype == null || race == null)
            {
                return false;
            }

            EnsureInitialized();
            if (adapterFailed)
            {
                return ReferenceEquals(race, ThingDefOf.Human);
            }
            if (canUseXenotypeMethod == null)
            {
                return true;
            }

            try
            {
                return (bool)canUseXenotypeMethod.Invoke(null, new object[] { xenotype, race });
            }
            catch (Exception exception)
            {
                adapterFailed = true;
                LogFailure(exception);
                return ReferenceEquals(race, ThingDefOf.Human);
            }
        }

        public static IReadOnlyList<XenotypeDef> GetExplicitXenotypes(ThingDef race)
        {
            if (race == null)
            {
                return Array.Empty<XenotypeDef>();
            }
            if (ExplicitXenotypesByRace.TryGetValue(race, out IReadOnlyList<XenotypeDef> cached))
            {
                return cached;
            }

            List<XenotypeDef> result = new List<XenotypeDef>();
            try
            {
                object alienRace = GetMemberValue(race, "alienRace");
                object restriction = GetMemberValue(alienRace, "raceRestriction");
                AddXenotypes(result, GetMemberValue(restriction, "xenotypeList"));
                AddXenotypes(result, GetMemberValue(restriction, "whiteXenotypeList"));

                HashSet<XenotypeDef> black = new HashSet<XenotypeDef>();
                AddXenotypes(black, GetMemberValue(restriction, "blackXenotypeList"));
                result.RemoveAll(xenotype => xenotype == null || black.Contains(xenotype) || !CanUseXenotype(xenotype, race));
            }
            catch (Exception exception)
            {
                LogFailure(exception);
            }

            IReadOnlyList<XenotypeDef> distinct = result.Distinct().ToList();
            ExplicitXenotypesByRace[race] = distinct;
            return distinct;
        }

        public static void ClearCaches()
        {
            ExplicitXenotypesByRace.Clear();
        }

        private static object GetMemberValue(object instance, string name)
        {
            if (instance == null || string.IsNullOrEmpty(name))
            {
                return null;
            }
            Type type = instance.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return field.GetValue(instance);
            }
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetValue(instance, null);
        }

        private static void AddXenotypes(ICollection<XenotypeDef> target, object source)
        {
            if (target == null || !(source is IEnumerable enumerable))
            {
                return;
            }
            foreach (object item in enumerable)
            {
                if (item is XenotypeDef xenotype && xenotype != null && !target.Contains(xenotype))
                {
                    target.Add(xenotype);
                }
            }
        }

        private static void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }
            initialized = true;

            try
            {
                Type restrictionType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(HarRestrictionTypeName, false))
                    .FirstOrDefault(type => type != null);
                if (restrictionType == null)
                {
                    return;
                }

                canUseXenotypeMethod = restrictionType.GetMethod(
                    "CanUseXenotype",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(XenotypeDef), typeof(ThingDef) },
                    null);
                if (canUseXenotypeMethod == null || canUseXenotypeMethod.ReturnType != typeof(bool))
                {
                    adapterFailed = true;
                    LogFailure(null);
                }
            }
            catch (Exception exception)
            {
                adapterFailed = true;
                LogFailure(exception);
            }
        }

        private static void LogFailure(Exception exception)
        {
            if (failureLogged)
            {
                return;
            }
            failureLogged = true;
            string detail = exception == null ? "compatible method not found" : exception.GetType().Name + ": " + exception.Message;
            Log.Warning("[FactionRaceDiversity] HAR xenotype compatibility adapter failed (" + detail + "). Non-human race replacement will use conservative fallbacks.");
        }
    }
}
