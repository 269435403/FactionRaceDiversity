from pathlib import Path
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
ERRORS = []


def check(condition, message):
    if condition:
        print(f"PASS: {message}")
    else:
        print(f"FAIL: {message}")
        ERRORS.append(message)


xml_files = sorted((ROOT / "About").glob("*.xml")) + sorted((ROOT / "1.6").rglob("*.xml"))
for path in xml_files:
    ET.parse(path)
check(len(xml_files) >= 5, f"all {len(xml_files)} XML files parse")

about = ET.parse(ROOT / "About" / "About.xml").getroot()
about_name = about.findtext("name") or ""
about_description = about.findtext("description") or ""
check("Race" in about_name and "People" not in about_name, "player-facing mod name uses Race and not People")
check(about.findtext("packageId") == "yyyyy.mixedpeoplesfactions", "legacy PackageID is preserved for saves")
check(about.findtext("author") == "yyyyy", "author is yyyyy")
check("1.6" in [node.text for node in about.findall("./supportedVersions/li")], "RimWorld 1.6 is supported")
dependencies = {node.findtext("packageId") for node in about.findall("./modDependencies/li")}
check("brrainz.harmony" in dependencies, "Harmony is declared as a required dependency")
load_after = {node.text for node in about.findall("./loadAfter/li")}
check("brrainz.harmony" in load_after, "loads after Harmony")
check("erdelf.HumanoidAlienRaces" in load_after, "optionally loads after HAR when present")
curated_packages = {
    "Solaris.RatkinRaceMod",
    "HenTaiLoliTeam.Axolotl",
    "Epona.EponaDynasticRise",
    "Ancot.KiiroRace",
    "Ancot.KiiroRaceGenePatch",
    "Ancot.MiliraRace",
    "keeptpa.NivarianRace",
    "EoralMilk.RatkinGeneExpanded",
    "MelonDove.WolfeinRace",
    "Ancot.WolfeinRaceGenePatch",
    "OARK.RatkinFaction.OberoniaAurea",
    "RooAndGloomy.DragonianRaceMod",
    "Seioch.Kurin.HAR",
    "Ariandel.MiliraImperium",
    "WaffelF.MoosesianRace",
    "RooAndGloomy.YuranRaceMod",
    "OARK.RatkinFaction.GeneExpand",
    "ZuoYao.KiiroMaineCoon",
    "ZuoYao.KiiroOrangeCat",
    "ZuoYao.KiiroRagdoll",
    "ZuoYao.KiiroSiamese",
    "Ancot.MiliraRaceGenePatch",
    "HenTaiLoliTeam.Axolotl.FactionExpand",
    "fxz.ratkinfaction",
    "RKK.RatKnights.Core",
    "EoralMilk.RatkinMoustate",
    "RKU.RatkinUnderground",
    "miho.fortifiedoutremer",
    "src.Core.markeazzyh",
}
check(curated_packages.issubset(load_after), "curated race and gene profiles load after their optional source mods when present")
check("per-faction" in about_description.lower() and "mixed-peoples factions" in about_description.lower(), "About describes direct proportions and bundled mixed-peoples factions")

factions_path = ROOT / "1.6" / "Defs" / "FactionDefs" / "MPF_Factions.xml"
factions = ET.parse(factions_path).getroot().findall("FactionDef")
by_name = {node.findtext("defName"): node for node in factions}
check(set(by_name) == {"MPF_MixedCivil", "MPF_MixedRough"}, "two legacy example FactionDefs remain available")
check(len(by_name) == len(factions), "no duplicate faction defName")
check(by_name["MPF_MixedCivil"].attrib.get("ParentName") == "OutlanderFactionBase", "civil example keeps its original parent")
check(by_name["MPF_MixedRough"].attrib.get("ParentName") == "OutlanderRoughBase", "rough example keeps its original parent")
for name, node in by_name.items():
    check(node.findtext("requiredCountAtGameStart") == "0", f"{name} can be removed at world creation")
    check(node.findtext("startingCountAtWorldCreation") == "1", f"{name} starts with one configurable entry")
    check(node.find("xenotypeSet") is None, f"{name} does not hardcode xenotypeSet in XML")


compat_path = ROOT / "1.6" / "Defs" / "CompatibilityDefs" / "FRD_FirstPartyCompatibility.xml"
compat_defs = ET.parse(compat_path).getroot()
compat_by_name = {
    node.findtext("defName"): node
    for node in compat_defs
    if node.tag == "MixedPeoplesFactions.FRD_RaceCompatibilityDef"
}
faction_set = next(
    node for node in compat_defs
    if node.tag == "MixedPeoplesFactions.FRD_FactionCompatibilitySetDef"
    and node.findtext("defName") == "FRD_FirstPartySupportedFactions"
)
shared_factions = {child.text for child in faction_set.findall("./factionDefNames/li")}
check(len(shared_factions) == 82, "shared compatibility set covers eighty-two curated faction identifiers")
check({
    "OutlanderCivil", "Empire", "Rakinia", "EponicKingdomFaction", "Kiiro_Faction",
    "Milira_Faction", "Axolotl_BloodSect", "Rakinia_Warlord", "RKK_KnightOrders",
    "Rakinia_Exotic", "RKU_Faction", "Rakinia_RockRatkin", "Miho_Faction_Supremacist", "SR_Faction_Pirate"
}.issubset(shared_factions), "shared compatibility set includes representative vanilla, race, gene, and faction-expansion factions")
expected_profiles = {
    "FRD_NewRatkinPlus_Compatibility": ("Ratkin", "Solaris.RatkinRaceMod", {"RK_XenoType_Ratkin"}),
    "FRD_MoeLotl_Compatibility": ("Axolotl", "HenTaiLoliTeam.Axolotl", {"Axolotl_Xenotype_MoeLotlBase"}),
    "FRD_EponaDynasticRise_Epona_Compatibility": ("Alien_Epona", "Epona.EponaDynasticRise", {"Xeno_Epona", "Xeno_Destrier", "Xeno_Unicorn"}),
    "FRD_EponaDynasticRise_Destrier_Compatibility": ("Alien_Destrier", "Epona.EponaDynasticRise", {"Xeno_Destrier"}),
    "FRD_EponaDynasticRise_Unicorn_Compatibility": ("Alien_Unicorn", "Epona.EponaDynasticRise", {"Xeno_Unicorn"}),
    "FRD_EponaMilira_Valkyrie_Compatibility": ("Alien_Epona_Milira", "Epona.EponaDynasticRise,Ancot.MiliraRaceGenePatch", {"Xeno_EponaMilira"}),
    "FRD_EponaMilira_Pegasus_Compatibility": ("Alien_Unicorn_Milira", "Epona.EponaDynasticRise,Ancot.MiliraRaceGenePatch", {"Xeno_UnicornMilira"}),
    "FRD_Kiiro_Compatibility": ("Kiiro_Race", "Ancot.KiiroRace", {"KiiroXenotype"}),
    "FRD_Milira_Compatibility": ("Milira_Race", "Ancot.MiliraRace", {"Baseliner"}),
    "FRD_MihoStarRing_Compatibility": ("Alien_Miho", "src.Core.markeazzyh", {"Xeno_CelestialMiho", "Xeno_CelestialMiho_Arctic", "Xeno_CelestialMiho_Desert", "Xeno_CelestialMiho_Highland", "Xeno_CelestialMiho_Highmate", "Xeno_CelestialMiho_Voidborn"}),
    "FRD_Nivarian_Compatibility": ("NivarianRace_Pawn", "keeptpa.NivarianRace", {"LuminNivarian", "PulsarNivarian", "AegisNivarian"}),
    "FRD_Wolfein_Compatibility": ("Wolfein_Race", "MelonDove.WolfeinRace", {"Wolfein_Xenotype", "Wolfein_Xenotype_PureBlood"}),
    "FRD_Dragonian_Compatibility": ("Dragonian_Race", "RooAndGloomy.DragonianRaceMod", {"DragonianXenotype", "DragonianXenotypeBlack"}),
    "FRD_KurinHAR_Compatibility": ("Kurin_Race", "Seioch.Kurin.HAR", {"Baseliner"}),
    "FRD_Moosesian_Compatibility": ("Moosesian", "WaffelF.MoosesianRace", {"MoosesianXenotype_Woodland", "MoosesianXenotype_Flatland"}),
    "FRD_Yuran_Compatibility": ("Yuran_Race", "RooAndGloomy.YuranRaceMod", {"YuranXenotype"}),
    "FRD_YuranMiko_Compatibility": ("Yuran_Race_Miko", "RooAndGloomy.YuranRaceMod", {"YuranXenotype"}),
    "FRD_YuranBlackSnake_Compatibility": ("Yuran_Race_Miko_BlackSnake", "RooAndGloomy.YuranRaceMod", {"YuranXenotypeBlackSnake"}),
}
check(set(compat_by_name) == set(expected_profiles), "all eighteen curated race compatibility profiles exist")
for name, (race, package, xenotypes) in expected_profiles.items():
    node = compat_by_name[name]
    faction_sets = {child.text for child in node.findall("./supportedFactionSetDefNames/li")}
    sources = {child.text for child in node.findall(".//sourceKindDefNames/li")}
    declared_xenotypes = {child.text for child in node.findall("./xenotypeDefNames/li")}
    check(node.attrib.get("MayRequire") == package and node.findtext("raceDefName") == race, f"{name} is optional and targets the expected race")
    check(xenotypes.issubset(declared_xenotypes), f"{name} declares the expected native xenotypes")
    check(faction_sets == {"FRD_FirstPartySupportedFactions"}, f"{name} uses the shared curated faction set")
    check({"OutlanderCivil", "TribeCivil", "Pirate", "Empire", "TradersGuild"}.issubset(shared_factions), f"{name} covers representative vanilla and DLC factions")
    check({"OA_RK_Faction", "Kurin_Faction", "Milira_Imperium", "MooseTribe", "YuranNPC", "RKU_Faction"}.issubset(shared_factions), f"{name} covers all eighty-two curated faction identifiers")
    check({"Villager", "Town_Councilman", "Town_Trader", "Grenadier_Destructive"}.issubset(sources), f"{name} maps civilian, leader, trader, and breacher roles")
    check(all(node.findtext(tag) for tag in ("civilianFallbackKindDefName", "combatFallbackKindDefName", "traderFallbackKindDefName", "leaderFallbackKindDefName")), f"{name} defines four universal role fallbacks")

for profile_name, target_kind in {
    "FRD_EponaMilira_Valkyrie_Compatibility": "Epona_Milira_Colonist",
    "FRD_EponaMilira_Pegasus_Compatibility": "Unicorn_Milira_Colonist",
}.items():
    node = compat_by_name[profile_name]
    check(node.findtext("allowAutomaticFallback") == "false", f"{profile_name} disables heuristic role matching")
    check(node.findtext("allowUniversalRoleFallback") == "true", f"{profile_name} explicitly authorizes its sole universal PawnKind")
    check({child.text for child in node.findall("./pawnKindMappings/li/targetKindDefName")} == {target_kind}, f"{profile_name} maps every supported role to the correct hybrid PawnKind")
    excluded = {child.text for child in node.findall("./excludedXenotypeDefNames/li")}
    expected_other_hybrid = "Xeno_UnicornMilira" if "Valkyrie" in profile_name else "Xeno_EponaMilira"
    check({"Baseliner", "Xeno_Epona", "Xeno_Destrier", "Xeno_Unicorn", expected_other_hybrid}.issubset(excluded), f"{profile_name} excludes inherited base and cross-hybrid xenotypes")

miho_excluded = {child.text for child in compat_by_name["FRD_MihoStarRing_Compatibility"].findall("./excludedXenotypeDefNames/li")}
check("Baseliner" in miho_excluded, "Miho explicitly excludes the stale Baseliner slider")

conditional_xenotypes = {
    ("FRD_Kiiro_Compatibility", "KiiroXenotype"): "Ancot.KiiroRaceGenePatch",
    ("FRD_Kiiro_Compatibility", "KiiroXenotype_MaineCoon"): "ZuoYao.KiiroMaineCoon",
    ("FRD_Kiiro_Compatibility", "KiiroXenotype_OrangeCat"): "ZuoYao.KiiroOrangeCat",
    ("FRD_Kiiro_Compatibility", "KiiroXenotype_Ragdoll"): "ZuoYao.KiiroRagdoll",
    ("FRD_Kiiro_Compatibility", "KiiroXenotype_Siamese"): "ZuoYao.KiiroSiamese",
    ("FRD_Milira_Compatibility", "MiliraXenotype"): "Ancot.MiliraRaceGenePatch",
    ("FRD_MihoStarRing_Compatibility", "Xeno_CelestialMiho"): None,
    ("FRD_MihoStarRing_Compatibility", "Xeno_CelestialMiho_Arctic"): None,
    ("FRD_MihoStarRing_Compatibility", "Xeno_CelestialMiho_Desert"): "miho.fortifiedoutremer",
    ("FRD_MihoStarRing_Compatibility", "Xeno_CelestialMiho_Highland"): None,
    ("FRD_MihoStarRing_Compatibility", "Xeno_CelestialMiho_Highmate"): None,
    ("FRD_MihoStarRing_Compatibility", "Xeno_CelestialMiho_Voidborn"): "Ludeon.RimWorld.Odyssey",
    ("FRD_Wolfein_Compatibility", "Wolfein_Xenotype"): "Ancot.WolfeinRaceGenePatch",
    ("FRD_Wolfein_Compatibility", "Wolfein_Xenotype_PureBlood"): "Ancot.WolfeinRaceGenePatch",
}
for (profile_name, xenotype_name), package in conditional_xenotypes.items():
    item = next(child for child in compat_by_name[profile_name].findall("./xenotypeDefNames/li") if child.text == xenotype_name)
    check(item.attrib.get("MayRequire") == package, f"{xenotype_name} has the expected optional package gate")
conditional_baseliner_exclusions = {
    "FRD_Kiiro_Compatibility": "Ancot.KiiroRaceGenePatch",
    "FRD_Milira_Compatibility": "Ancot.MiliraRaceGenePatch",
    "FRD_Wolfein_Compatibility": "Ancot.WolfeinRaceGenePatch",
}
for profile_name, package in conditional_baseliner_exclusions.items():
    items = [child for child in compat_by_name[profile_name].findall("./excludedXenotypeDefNames/li") if child.text == "Baseliner"]
    check(len(items) == 1 and items[0].attrib.get("MayRequire") == package, f"{profile_name} hides Baseliner only while its gene patch is active")
ratkin_gene_items = [child for child in compat_by_name["FRD_NewRatkinPlus_Compatibility"].findall("./xenotypeDefNames/li") if child.attrib.get("MayRequire") == "EoralMilk.RatkinGeneExpanded"]
check(len(ratkin_gene_items) == 8, "Ratkin Gene Expanded contributes eight conditional Ratkin xenotypes")
oa_xeno = next(child for child in compat_by_name["FRD_NewRatkinPlus_Compatibility"].findall("./xenotypeDefNames/li") if child.text == "Ratkin_OA")
check(oa_xeno.attrib.get("MayRequire") == "OARK.RatkinFaction.OberoniaAurea", "Oberonia aurea contributes its Ratkin xenotype conditionally")
oa_gene_items = [child for child in compat_by_name["FRD_NewRatkinPlus_Compatibility"].findall("./xenotypeDefNames/li") if child.attrib.get("MayRequire") == "OARK.RatkinFaction.GeneExpand"]
check(len(oa_gene_items) == 11, "OA Ratkin Gene Expand contributes eleven conditional Ratkin xenotypes")

conditional_faction_variants = {
    ("FRD_NewRatkinPlus_Compatibility", "OARK.RatkinFaction.OberoniaAurea"): "OA_RK_Assault_B",
    ("FRD_NewRatkinPlus_Compatibility", "fxz.ratkinfaction"): "RatkinCombatantWarlord",
    ("FRD_NewRatkinPlus_Compatibility", "RKK.RatKnights.Core"): "RKK_DragoonKnight",
    ("FRD_NewRatkinPlus_Compatibility", "EoralMilk.RatkinMoustate"): "RatkinExoticSoldier",
    ("FRD_NewRatkinPlus_Compatibility", "RKU.RatkinUnderground"): "RKU_Invader",
    ("FRD_MoeLotl_Compatibility", "HenTaiLoliTeam.Axolotl.FactionExpand"): "Axolotl_CultDisciple",
    ("FRD_Milira_Compatibility", "Ariandel.MiliraImperium"): "Milira_SpaceMarine",
}
for (profile_name, package), combat_fallback in conditional_faction_variants.items():
    node = compat_by_name[profile_name]
    mappings = [item for item in node.findall("./pawnKindMappings/li") if item.attrib.get("MayRequire") == package]
    overrides = [item for item in node.findall("./fallbackOverrides/li") if item.attrib.get("MayRequire") == package]
    check(len(mappings) == 13 and all(item.findtext("variantId") == package for item in mappings), f"{profile_name} registers thirteen role mappings for variant {package}")
    check(len(overrides) == 1 and overrides[0].findtext("variantId") == package and overrides[0].findtext("combatFallbackKindDefName") == combat_fallback, f"{profile_name} registers role fallbacks for variant {package}")


for profile_name, only_kind in {
    "FRD_YuranMiko_Compatibility": "Yuran_Colonist_Miko",
    "FRD_YuranBlackSnake_Compatibility": "Yuran_Colonist_Miko_BlackSnake",
}.items():
    node = compat_by_name[profile_name]
    fallbacks = {node.findtext(tag) for tag in ("civilianFallbackKindDefName", "combatFallbackKindDefName", "traderFallbackKindDefName", "leaderFallbackKindDefName")}
    targets = {item.findtext("targetKindDefName") for item in node.findall("./pawnKindMappings/li")}
    check(fallbacks == {only_kind} and targets == {only_kind}, f"{profile_name} safely uses its single ordinary PawnKind for all roles")

all_target_kinds = {item.text for item in compat_defs.findall(".//targetKindDefName")} | {
    item.text for tag in ("civilianFallbackKindDefName", "combatFallbackKindDefName", "traderFallbackKindDefName", "leaderFallbackKindDefName")
    for item in compat_defs.findall(f".//fallbackOverrides/li/{tag}")
}
check(not any(name and name.startswith("Milian_") for name in all_target_kinds), "Milian mechanoid PawnKinds are excluded from humanlike race fallbacks")

key_files = {
    "en": ROOT / "1.6" / "Languages" / "English" / "Keyed" / "MPF_Keys.xml",
    "zh": ROOT / "1.6" / "Languages" / "ChineseSimplified" / "Keyed" / "MPF_Keys.xml",
}
key_sets = {lang: {child.tag for child in ET.parse(path).getroot()} for lang, path in key_files.items()}
check(key_sets["en"] == key_sets["zh"], "English and Chinese Keyed sets match")
required_frd_keys = {
    "FRD_ModName", "FRD_FactionSearch", "FRD_Supported", "FRD_Unsupported",
    "FRD_XenotypeSection", "FRD_RaceSection",
    "FRD_RestoreOriginalRules", "FRD_DebugReportHeader",
    "FRD_ShowHiddenFactions", "FRD_ShowUnsupportedFactions", "FRD_RaceXenotypeSection",
}
check(required_frd_keys.issubset(key_sets["en"]), "new FRD player-facing translation keys exist")
check(all(key.startswith(("FRD_", "MPF_")) for key in key_sets["en"]), "translation keys use FRD_ or legacy MPF_ prefixes")

injected = ET.parse(ROOT / "1.6" / "Languages" / "ChineseSimplified" / "DefInjected" / "FactionDef" / "MPF_Factions.xml").getroot()
injected_keys = {child.tag for child in injected}
for def_name in by_name:
    for field in ("label", "description", "pawnSingular", "pawnsPlural", "leaderTitle"):
        check(f"{def_name}.{field}" in injected_keys, f"Chinese translation exists: {def_name}.{field}")

source_dir = ROOT / "Source" / "MixedPeoplesFactions"
source_files = {path.name: path.read_text(encoding="utf-8-sig") for path in source_dir.glob("*.cs")}
source_text = "\n".join(source_files.values())
check("namespace MixedPeoplesFactions" in source_text, "legacy C# namespace remains load-compatible")
check("Dictionary<string, FactionRaceSettings>" in source_text and "FRD_factionSettings" in source_text, "settings are stored per FactionDef")
check("MPF_xenotypeWeights" in source_text and "MigrateLegacySettings" in source_text, "legacy settings migration remains available")
check("xenotypeWeights.Count > 0" in source_text and "EnsureBuiltInFactionDefaults" in source_text, "legacy global weights migrate while built-in factions retain equal defaults")
check("DefDatabase<FactionDef>.AllDefsListForReading" in source_text, "all loaded factions are discovered dynamically")
check("DefDatabase<PawnKindDef>.AllDefsListForReading" in source_text, "existing PawnKindDefs are indexed without cloning")
check("race.category == ThingCategory.Pawn" in source_text and "typeof(Pawn).IsAssignableFrom" in source_text and "!race.IsCorpse" in source_text, "race registry excludes corpses and non-Pawn ThingDefs")
check("CreepJoinerFormKindDef" in source_text and "kind.mutant == null" in source_text, "special creep joiner and mutant kinds are filtered")
check("xenotypeSettingsByRace" in source_text and "RaceXenotypeSettings" in source_text, "each race has an independent xenotype pool")
check("CurrentSchemaVersion = 6" in source_text and "MigrateToDirectRaceSettings" in source_text, "schema 6 migrates legacy settings to direct race proportions")
check("FRD_RaceCompatibilityDef" in source_text and "TryGetMappedKind" in source_text and "TryGetFallbackKind" in source_text, "curated mappings use faction/race gates, automatic matching, and role fallbacks")
check("FRD_PawnKindFallbackOverride" in source_text and "fallbackOverrides" in source_text and "ResolveFallbackOverride" in source_text, "same-Race faction expansions can conditionally replace role fallbacks")
check("GetExplicitXenotypes" in source_text and "claimedByNonHumanRace" in source_text, "race-native xenotype ownership prevents cross-race pools")
check("excludedXenotypeDefNames" in source_text and "native.ExceptWith(excluded)" in source_text and "!excluded.Contains(XenotypeDefOf.Baseliner)" in source_text, "gene-patched races can conditionally suppress stale Baseliner sliders")
check("ConfiguredRacesSupportRequiredRaidRole" in source_text and "RaidStrategyWorker_WithRequiredPawnKinds" in source_text, "required raid roles are filtered before pawn generation")
check("chance.chance > 0f" in source_files.get("FRD_XenotypeService.cs", ""), "zero-chance xenotypes do not become race-native sliders")
check("ResetBuiltInFactionToEqualDefaults" in source_text and "EnsureBuiltInFactionDefaults" in source_text and "SetEqualRaceWeights" in source_text, "built-in original rules permanently keep all discovered races equal")
check("humanlikeFaction == true" in source_files.get("FRD_PawnGroupContext.cs", ""), "all humanlike pawn groups treat faction xenotypes as soft selections")
check("PawnGroupKindDefOf.Combat" in source_files.get("FRD_PawnGroupContext.cs", "") and "requireCombatKind" in source_files.get("FRD_RaceService.cs", ""), "combat pawn groups are identified independently of the source PawnKind role")
check("!universalRoleFallback && requireCombatKind && !candidate.isFighter" in source_files.get("FRD_RaceService.cs", "") and "requireCombatKind ? !candidate.isFighter" in source_files.get("FRD_RaceService.cs", ""), "combat replacement rejects civilian PawnKinds unless a profile explicitly declares a sole universal kind")
check("profile.Def.allowUniversalRoleFallback" in source_files.get("FRD_CompatibilityDefs.cs", "") and "return target?.isFighter == true" in source_files.get("FRD_CompatibilityDefs.cs", ""), "combat fallback remains fighter-only outside explicit universal-role profiles")
check("AllowsUniversalRoleFallback" in source_text and "allowUniversalRoleFallback" in source_text, "single-kind hybrid races use an explicit narrowly scoped universal-role exception")
check("SupportsRequiredSpecialRole(source, candidate)" in source_files.get("FRD_RaceService.cs", ""), "manual mappings preserve sapper, breacher, and psychic-invoker role requirements")
check(source_files.get("FRD_CompatibilityDefs.cs", "").count("SupportsRequiredSpecialRole(source, target)") >= 3, "all fallback paths preserve special raid-role requirements")
check("candidate.useFactionXenotypes != original.useFactionXenotypes" not in source_text, "HAR PawnKinds are not rejected for owning their xenotype rules")
check("TryApplyAtomicSelection" in source_text and "request.KindDef = selected.Kind" in source_text and "request.ForcedXenotype = selectedXenotype" in source_text, "race and xenotype are applied atomically")
check("ThreadStatic" in source_files.get("FRD_PawnGroupContext.cs", "") and "Stack<Frame>" in source_files.get("FRD_PawnGroupContext.cs", ""), "ordinary pawn groups use a thread-local context stack")
check("new Harmony(HarmonyId).PatchAll" in source_text, "Harmony patches are initialized")
check("PawnGenerationRequest originalRequest = request" in source_text and "request = originalRequest" in source_text, "failed atomic selection restores the complete original request")
check("Required raid-role compatibility check failed" in source_text and "catch (Exception exception)" in source_files.get("FRD_RaceService.cs", ""), "required raid-role reflection failures are logged once instead of being swallowed")
check(source_files.get("FRD_HarmonyPatches.cs", "").find("PatchAll") < source_files.get("FRD_HarmonyPatches.cs", "").find("patched = true"), "Harmony bootstrap marks success only after PatchAll completes")
check("GenerateOrRedressPawnInternal" in source_text and "PawnGenerator.XenotypesAvailableFor" in source_text and "PawnGroupKindWorker" in source_text, "final generation, xenotype, and pawn-group context entry points are patched")
for forbidden_target in ("TryGenerateNewPawnInternal", "Pawn_IdeoTracker", "RedressPawn", "SetFaction"):
    check(f"HarmonyPatch(typeof({forbidden_target}" not in source_text, f"does not patch FCD-owned target {forbidden_target}")
check("FRD_ShowHiddenFactions" in source_text and "FRD_ShowUnsupportedFactions" in source_text, "hidden and unavailable faction filters are exposed")
check('"FRD_RaceOverride".Translate()' not in source_text and '"FRD_XenotypeOverride".Translate()' not in source_text, "race and xenotype enable checkboxes were removed from the settings panel")
check("ConfiguredColor" in source_text and "OrderByDescending(record => IsActivelyConfigured" in source_text, "configured factions are green and sorted first")
check("DrawFactionIcon" in source_text and "factionIconPath" in source_text, "faction rows draw safe faction icons")
check("FRD_Diagnostics" in source_text and "NoCompatiblePawnKind" in source_text, "generation fallback reasons are aggregated for debugging")
check("EffectiveFactionFor" in source_text and "pawnGenerationDepth" in source_text, "outer caravan faction applies to direct group members without leaking into relationship recursion")
check("request.PawnKindDefGetter = null" in source_text and "HarmonyPriority(Priority.Last)" in source_text, "the final race gate clears dynamic kind getters and runs after other request prefixes")
check("FRD_RaceRegistry.IsRealPawnRace(__result.def)" in source_text, "diagnostics ignore caravan animals and other non-humanlike pawns")
check("request.FixedIdeo =" not in source_text and "request.ForceNoIdeo =" not in source_text, "culture request fields are not rewritten")
check("Faction.ideos" not in source_text and "Pawn.Ideo" not in source_text, "culture data is outside this mod's ownership")
check("cachedDescription" in source_text, "faction description cache is cleared after owned changes")
check("AlienRace" not in (source_dir / "MixedPeoplesFactions.csproj").read_text(encoding="utf-8-sig"), "HAR is not a hard assembly reference")

csproj_text = (source_dir / "MixedPeoplesFactions.csproj").read_text(encoding="utf-8-sig")
check("0Harmony" in csproj_text, "Harmony assembly is referenced")
check("<Private>False</Private>" in csproj_text, "game and Harmony dependencies are not copied")
check("D:\\steam" not in csproj_text, "csproj contains no machine-specific absolute path")

dll = ROOT / "1.6" / "Assemblies" / "MixedPeoplesFactions.dll"
check(dll.is_file() and dll.stat().st_size > 0, "Release DLL exists and is non-empty")
check(not (ROOT / "1.6" / "Assemblies" / "MixedPeoplesFactions.pdb").exists(), "release Assemblies contains no PDB")
check(not any((source_dir / name).exists() for name in ("bin", "obj")), "source tree contains no bin/obj build artifacts")

compat_rules = ROOT / "模组文档" / "种族兼容基本规则.md"
check(compat_rules.is_file() and "运行时解析顺序" in compat_rules.read_text(encoding="utf-8-sig"), "canonical race compatibility rules document exists")

wiki_def = ROOT / "Wiki" / "Defs" / "FactionDef.md"
wiki_index = ROOT / "Wiki" / "_总索引.md"
check(wiki_def.is_file() and "| 游戏解释 |" in wiki_def.read_text(encoding="utf-8-sig"), "Wiki FactionDef has game explanation column")
check(wiki_index.is_file() and "游戏解释" in wiki_index.read_text(encoding="utf-8-sig"), "Wiki index documents manual game explanations")

if ERRORS:
    print(f"\n{len(ERRORS)} check(s) failed.")
    sys.exit(1)
print("\nAll static checks passed.")






