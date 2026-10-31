// =============================================================================
//  NoBodyMorePlayers — Plugin.cs
//  BepInEx 5 entry point, configuration bindings, and patch orchestration.
// =============================================================================

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace NoBodyMorePlayers
{
    // ─────────────────────────────────────────────────────────────────────────
    // Central constants. Bump PLUGIN_VERSION here for every release.
    // ─────────────────────────────────────────────────────────────────────────
    internal static class PluginInfo
    {
        public const string PLUGIN_GUID    = "com.community.nobodynoproblem.moreplayers";
        public const string PLUGIN_NAME    = "NoBodyMorePlayers";
        public const string PLUGIN_VERSION = "1.0.0";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Plugin class
    //
    // The `sealed` modifier is a BepInEx best-practice; the framework does not
    // require inheriting from BaseUnityPlugin to be extensible.
    // ─────────────────────────────────────────────────────────────────────────
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public sealed class Plugin : BaseUnityPlugin
    {
        // ── Singleton ─────────────────────────────────────────────────────────
        // Patches.cs accesses config entries through these static properties
        // so no static coupling to the config system itself is needed there.
        internal static Plugin         Instance { get; private set; } = null!;
        internal static ManualLogSource Log     => Instance._log;
        private ManualLogSource _log = null!;

        private Harmony _harmony = null!;

        // ── Config sections ───────────────────────────────────────────────────
        private const string SEC_LOBBY = "Lobby";
        private const string SEC_TEAMS = "Teams";
        private const string SEC_DEBUG = "Debug";

        // ── Exposed config entries (read by patch classes) ────────────────────

        /// <summary>
        /// Total Steam lobby capacity. Drives all low-level networking patches.
        /// Vanilla default: 4 (1 cop + 3 suspects).
        /// </summary>
        internal static ConfigEntry<int> CfgMaxPlayers { get; private set; } = null!;

        /// <summary>
        /// Maximum number of players that may be assigned the Cop (investigator) role.
        /// </summary>
        internal static ConfigEntry<int> CfgMaxCops { get; private set; } = null!;

        /// <summary>
        /// Maximum number of players that may be assigned the Suspect role.
        /// </summary>
        internal static ConfigEntry<int> CfgMaxSuspects { get; private set; } = null!;

        /// <summary>
        /// When <c>true</c>, size-override patches execute only on the host side.
        /// Guests running the unmodded game can still join the enlarged lobby.
        /// </summary>
        internal static ConfigEntry<bool> CfgHostOnly { get; private set; } = null!;

        /// <summary>
        /// Prints a line to the BepInEx console for every patch invocation.
        /// Useful while mapping class names with dnSpy; disable during normal play.
        /// </summary>
        internal static ConfigEntry<bool> CfgVerboseLog { get; private set; } = null!;

        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;
            _log     = base.Logger;

            BindConfig();
            ValidateConfig();

            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);

            // Phase 1 — dynamic patches applied via reflection.
            //   These target Steam / networking assemblies that are not compile-
            //   time dependencies, so they must be wired up manually before
            //   Harmony scans the assembly for [HarmonyPatch] attributes.
            PatchBootstrapper.ApplyDynamicPatches(_harmony);

            // Phase 2 — attribute-based patches in Patches.cs.
            //   Only the patches whose target class/method names are filled in
            //   (Sections §C–§E) will become active; commented-out blocks are
            //   ignored by PatchAll.
            _harmony.PatchAll(typeof(Plugin).Assembly);

            _log.LogInfo(
                $"[{PluginInfo.PLUGIN_NAME}] v{PluginInfo.PLUGIN_VERSION} loaded. " +
                $"MaxPlayers={CfgMaxPlayers.Value}, " +
                $"Cops={CfgMaxCops.Value}, " +
                $"Suspects={CfgMaxSuspects.Value}");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Config bindings
        // ─────────────────────────────────────────────────────────────────────
        private void BindConfig()
        {
            CfgMaxPlayers = Config.Bind(
                SEC_LOBBY,
                "MaxPlayers",
                8,
                new ConfigDescription(
                    "Total number of players allowed in one lobby.  " +
                    "The vanilla game caps this at 4 (1 cop + 3 suspects).  " +
                    "Steam lobbies support up to 250 members; keep this at 16 or below " +
                    "until the game's spawn and role-assignment logic is verified at scale.",
                    new AcceptableValueRange<int>(2, 32)));

            CfgMaxCops = Config.Bind(
                SEC_TEAMS,
                "MaxCops",
                2,
                new ConfigDescription(
                    "Number of players assigned to the Cop (investigator) role.  " +
                    "Must be less than MaxPlayers and greater than zero.",
                    new AcceptableValueRange<int>(1, 16)));

            CfgMaxSuspects = Config.Bind(
                SEC_TEAMS,
                "MaxSuspects",
                6,
                new ConfigDescription(
                    "Number of players assigned to the Suspect (crime-scene-cleaner) role.  " +
                    "Must be less than MaxPlayers and greater than zero.",
                    new AcceptableValueRange<int>(1, 16)));

            CfgHostOnly = Config.Bind(
                SEC_DEBUG,
                "HostOnlyEnforcement",
                true,
                "When true the lobby-size override patches only execute on the host side.  " +
                "Clients with an unmodded game copy can still join the larger lobby normally.  " +
                "Set to false only if you discover the game also enforces the cap on clients.");

            CfgVerboseLog = Config.Bind(
                SEC_DEBUG,
                "VerboseLogging",
                false,
                "Log every patch invocation to the BepInEx console.  " +
                "Enable while mapping real class names in dnSpy; disable during normal play.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Startup validation — warns on misconfigured team/total mismatches
        // ─────────────────────────────────────────────────────────────────────
        private void ValidateConfig()
        {
            int teamTotal = CfgMaxCops.Value + CfgMaxSuspects.Value;

            if (teamTotal != CfgMaxPlayers.Value)
            {
                _log.LogWarning(
                    $"Config mismatch: MaxCops ({CfgMaxCops.Value}) + " +
                    $"MaxSuspects ({CfgMaxSuspects.Value}) = {teamTotal}, " +
                    $"but MaxPlayers = {CfgMaxPlayers.Value}.  " +
                    $"Edit BepInEx/config/{PluginInfo.PLUGIN_NAME}.cfg so the three values are consistent.");
            }

            if (CfgMaxCops.Value >= CfgMaxPlayers.Value)
            {
                _log.LogError(
                    $"MaxCops ({CfgMaxCops.Value}) must be strictly less than " +
                    $"MaxPlayers ({CfgMaxPlayers.Value}).  " +
                    "The game will likely malfunction with this configuration.");
            }

            if (CfgMaxSuspects.Value >= CfgMaxPlayers.Value)
            {
                _log.LogError(
                    $"MaxSuspects ({CfgMaxSuspects.Value}) must be strictly less than " +
                    $"MaxPlayers ({CfgMaxPlayers.Value}).  " +
                    "The game will likely malfunction with this configuration.");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers for Patches.cs
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes <paramref name="message"/> to the BepInEx console at
        /// <see cref="LogLevel.Debug"/> level, but only when
        /// <see cref="CfgVerboseLog"/> is enabled.
        /// </summary>
        internal static void LogVerbose(string message)
        {
            if (CfgVerboseLog.Value)
                Log.LogDebug($"[Patch] {message}");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _log.LogInfo($"[{PluginInfo.PLUGIN_NAME}] unloaded.");
        }
    }
}
