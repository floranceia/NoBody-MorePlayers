// =============================================================================
//  NoBodyMorePlayers — Patches.cs
//  All Harmony patches, dynamic bootstrapper, and placeholder guide.
// =============================================================================
//
//  QUICK-START FOR CONTRIBUTORS
//  ─────────────────────────────────────────────────────────────────────────
//  Before uncommenting any §C/§D/§E block you must find the real class names
//  with dnSpy.  Here is the exact workflow:
//
//    1. Download dnSpy: https://github.com/dnSpyEx/dnSpy/releases
//       (choose dnSpy-net-win64.zip, no install needed — just run dnSpy.exe)
//
//    2. File → Open → <GameFolder>\No Body, No Problem!_Data\Managed\Assembly-CSharp.dll
//
//    3. Press Ctrl+Shift+K to open "Search Assemblies".
//       Search for each placeholder in GameIdentifiers below and note the
//       real class name that appears on the left tree-view panel.
//
//    4. Fill in GameIdentifiers below and update every TODO comment.
//
//    5. Uncomment the relevant patch class block, rebuild, and test.
//  ─────────────────────────────────────────────────────────────────────────
//
//  SECTION MAP
//    §A  GameIdentifiers        — all placeholder names in one place
//    §B  SteamLobbyPatches      — Steamworks.NET low-level (active, no ref needed)
//    §C  LobbyManager patches   — game's own lobby wrapper   [needs dnSpy → TODO]
//    §D  TeamManager patches    — cop / suspect role sizes    [needs dnSpy → TODO]
//    §E  LobbyUI patches        — slot display count          [needs dnSpy → TODO]
//    §F  Alternative backends   — Mirror / FishNet / Photon   [commented out]
//    §G  PatchBootstrapper      — called from Plugin.Awake()
// =============================================================================

using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace NoBodyMorePlayers
{
    // =========================================================================
    // §A  GAME IDENTIFIERS
    //
    // Central lookup table for every class/method/field name that needs
    // resolving via dnSpy.  Update the string values here; the patch classes
    // below reference them via nameof-style string constants so renaming is
    // straightforward.
    // =========================================================================
    internal static class GameIdentifiers
    {
        // ── Lobby ─────────────────────────────────────────────────────────────
        /// <summary>
        /// TODO: Paste the lobby-manager class name from dnSpy.
        /// Search term: "maxPlayers" or "CreateLobby" in Assembly-CSharp.dll.
        /// Example real names: "LobbyManager", "GameLobby", "NetworkLobbyController"
        /// </summary>
        public const string LobbyManagerClass     = "LobbyManager";            // ← REPLACE

        /// <summary>
        /// TODO: The method on LobbyManagerClass that creates / opens a Steam lobby.
        /// Example real names: "CreateLobby", "HostLobby", "OpenSession", "StartLobby"
        /// </summary>
        public const string CreateLobbyMethod     = "CreateLobby";             // ← REPLACE

        /// <summary>
        /// TODO: Integer field on LobbyManagerClass that stores the player cap.
        /// Three underscores + this name is how Harmony injects it as a ref parameter.
        /// Example real names: "maxPlayers", "_maxPlayers", "MaxPlayerCount"
        /// </summary>
        public const string MaxPlayersField       = "maxPlayers";              // ← REPLACE

        // ── Teams ──────────────────────────────────────────────────────────────
        /// <summary>
        /// TODO: Class that assigns Cop / Suspect roles to connected players.
        /// Search term: "maxCops" or "AssignRoles" in Assembly-CSharp.dll.
        /// Example real names: "RoleManager", "TeamManager", "GameMode", "GameManager"
        /// </summary>
        public const string TeamManagerClass      = "TeamManager";             // ← REPLACE

        /// <summary>
        /// TODO: Method that distributes role assignments at round/lobby start.
        /// Example real names: "AssignRoles", "SetupTeams", "DistributeRoles"
        /// </summary>
        public const string AssignRolesMethod     = "AssignRoles";             // ← REPLACE

        /// <summary>
        /// TODO: Integer field for the max number of cop-role players.
        /// Example real names: "maxCops", "copLimit", "_numCops"
        /// </summary>
        public const string MaxCopsField          = "maxCops";                 // ← REPLACE

        /// <summary>
        /// TODO: Integer field for the max number of suspect-role players.
        /// Example real names: "maxSuspects", "suspectLimit", "_numSuspects"
        /// </summary>
        public const string MaxSuspectsField      = "maxSuspects";             // ← REPLACE

        // ── Lobby UI ───────────────────────────────────────────────────────────
        /// <summary>
        /// TODO: MonoBehaviour class that draws the player-slot grid in the lobby.
        /// Search term: "RefreshSlots" or "slots" in Assembly-CSharp.dll.
        /// Example real names: "LobbyUI", "LobbyPanel", "MatchmakingScreen"
        /// </summary>
        public const string LobbyUIClass          = "LobbyUI";                 // ← REPLACE

        /// <summary>
        /// TODO: Method that creates or refreshes the visual player-slot objects.
        /// Example real names: "RefreshSlots", "UpdatePlayerList", "BuildSlots"
        /// </summary>
        public const string RefreshSlotsMethod    = "RefreshSlots";            // ← REPLACE
    }


    // =========================================================================
    // §B  STEAM LOBBY PATCHES
    //
    // These patches intercept the two primary Steamworks.NET call sites that
    // enforce a lobby member cap.  They are applied at runtime through
    // reflection (see §G PatchBootstrapper) so this file compiles without
    // Steamworks.NET as a direct project reference.
    //
    // Both methods run only on the host when CfgHostOnly is true, so guests
    // with an unmodded game build can join the enlarged lobby without any
    // changes on their end.
    // =========================================================================
    internal static class SteamLobbyPatches
    {
        // ── SteamMatchmaking.CreateLobby ──────────────────────────────────────
        // Steamworks.NET signature:
        //   public static SteamAPICall_t CreateLobby(ELobbyType eLobbyType, int cMaxMembers)
        //
        // Harmony injects `cMaxMembers` as a ref parameter; we override it with
        // the value from config before the original call executes.
        internal static bool CreateLobby_Prefix(ref int cMaxMembers)
        {
            if (Plugin.CfgHostOnly.Value && !SteamHostCheck.IsLocalPlayerHost())
                return true; // not host — let the original run unchanged

            Plugin.LogVerbose(
                $"SteamMatchmaking.CreateLobby: cMaxMembers {cMaxMembers} → {Plugin.CfgMaxPlayers.Value}");

            cMaxMembers = Plugin.CfgMaxPlayers.Value;
            return true; // true = execute original with the new parameter value
        }

        // ── SteamMatchmaking.SetLobbyMemberLimit ──────────────────────────────
        // Steamworks.NET signature:
        //   public static bool SetLobbyMemberLimit(CSteamID steamIDLobby, int cMaxMembers)
        //
        // Some games call this separately after CreateLobby to tighten the cap.
        internal static bool SetLobbyMemberLimit_Prefix(ref int cMaxMembers)
        {
            if (Plugin.CfgHostOnly.Value && !SteamHostCheck.IsLocalPlayerHost())
                return true;

            Plugin.LogVerbose(
                $"SteamMatchmaking.SetLobbyMemberLimit: cMaxMembers {cMaxMembers} → {Plugin.CfgMaxPlayers.Value}");

            cMaxMembers = Plugin.CfgMaxPlayers.Value;
            return true;
        }
    }

    // ── Steam host-detection helper ───────────────────────────────────────────
    // Resolved fully at runtime so no Steamworks.NET compile-time dependency.
    internal static class SteamHostCheck
    {
        private static readonly ManualLogSource _log =
            BepInEx.Logging.Logger.CreateLogSource("NBMPSteam");

        /// <summary>
        /// Returns <c>true</c> when the local Steam user is the current lobby owner,
        /// or when Steamworks types cannot be located (permissive fallback).
        /// </summary>
        internal static bool IsLocalPlayerHost()
        {
            try
            {
                var steamUser =
                    AccessTools.TypeByName("Steamworks.SteamUser")
                    ?? throw new TypeAccessException("Steamworks.SteamUser not found.");

                var getSteamID = AccessTools.Method(steamUser, "GetSteamID")
                    ?? throw new MissingMethodException("SteamUser.GetSteamID not found.");

                // CSteamID is a struct; we store it as object for runtime comparison.
                var localId = getSteamID.Invoke(null, null);

                // ── OPTIONAL IMPROVEMENT ────────────────────────────────────────
                // To do a precise owner comparison you also need the current lobby
                // CSteamID.  Resolve it like this once you know where the game
                // stores it (replace "LobbyManager" + "currentLobbyID" with real names):
                //
                //   var lobbyMgrType = AccessTools.TypeByName("LobbyManager");
                //   var lobbyIdField = AccessTools.Field(lobbyMgrType, "currentLobbyID");
                //   var currentLobbyId = lobbyIdField.GetValue(/* singleton instance */);
                //
                //   var matchmaking = AccessTools.TypeByName("Steamworks.SteamMatchmaking");
                //   var getLobbyOwner = AccessTools.Method(matchmaking, "GetLobbyOwner");
                //   var ownerId = getLobbyOwner.Invoke(null, new[] { currentLobbyId });
                //   return localId.Equals(ownerId);
                // ───────────────────────────────────────────────────────────────

                // Until the lobby ID is resolved, the check is non-deterministic.
                // Return true so the patch always applies (safe default for a host).
                Plugin.LogVerbose("IsLocalPlayerHost: lobby ID not yet resolved — defaulting to true.");
                return true;
            }
            catch (Exception ex)
            {
                _log.LogDebug($"IsLocalPlayerHost: {ex.Message} — defaulting to true (permissive).");
                return true;
            }
        }
    }


    // =========================================================================
    // §C  GAME LOBBY PATCHES                                   [NEEDS dnSpy]
    //
    // Targets the game's own lobby-manager class, which is a higher-level
    // wrapper over the Steamworks calls above.  Patching here is more robust
    // than patching only the Steam layer because the game may cache the value
    // in its own field before forwarding it to Steam.
    //
    // HOW TO ACTIVATE:
    //   1. Find the real class name in dnSpy (search "maxPlayers" or "CreateLobby").
    //   2. Replace "LobbyManager" in the [HarmonyPatch] attribute below with the
    //      actual class from Assembly-CSharp.dll.
    //   3. Replace GameIdentifiers.MaxPlayersField with the real field name.
    //   4. Add `using` for any namespace the class lives in if needed.
    //   5. Remove the surrounding /* ... */ comment delimiters.
    // =========================================================================

    /*
    // TODO: Change typeof(LobbyManager) to typeof(<real class from dnSpy>)
    [HarmonyPatch(typeof(LobbyManager), GameIdentifiers.CreateLobbyMethod)]
    internal static class Patch_LobbyManager_CreateLobby
    {
        /// <summary>
        /// Prefix — runs before LobbyManager.CreateLobby().
        ///
        /// Harmony injects the instance and the backing field as ref parameters.
        /// The field parameter name follows the convention: three underscores + field name.
        ///
        /// If the field name differs (e.g. "_maxPlayers"), rename the parameter
        /// accordingly: `ref int ____maxPlayers`.
        /// </summary>
        [HarmonyPrefix]
        private static void Prefix(
            LobbyManager __instance,    // TODO: update type to match real class
            ref int      ___maxPlayers) // TODO: rename to match real field (GameIdentifiers.MaxPlayersField)
        {
            // ── Host guard ────────────────────────────────────────────────────
            if (Plugin.CfgHostOnly.Value)
            {
                // TODO: Replace with the game's real host/server check.  Examples:
                //   Mirror    → if (!Mirror.NetworkServer.active) return;
                //   FishNet   → if (!FishNet.InstanceFinder.IsServerStarted) return;
                //   Photon    → if (!Photon.Pun.PhotonNetwork.IsMasterClient) return;
                //   Game-own  → if (!__instance.IsHost) return;
                //
                // If the game has no such property, leave this block empty or
                // rely on the Steam host check in SteamHostCheck.IsLocalPlayerHost().
            }

            Plugin.LogVerbose(
                $"LobbyManager.{GameIdentifiers.CreateLobbyMethod}: " +
                $"maxPlayers {___maxPlayers} → {Plugin.CfgMaxPlayers.Value}");

            ___maxPlayers = Plugin.CfgMaxPlayers.Value;

            // ── Alternative: use Traverse when field injection does not work ──
            // Some Unity builds strip field metadata.  If the ref parameter above
            // causes a Harmony injection error, replace it with:
            //   Traverse.Create(__instance)
            //           .Field(GameIdentifiers.MaxPlayersField)
            //           .SetValue(Plugin.CfgMaxPlayers.Value);
        }
    }
    */


    // =========================================================================
    // §D  TEAM ASSIGNMENT PATCHES                              [NEEDS dnSpy]
    //
    // The game distributes Cop / Suspect roles inside a manager class at round
    // start.  Patching the team-cap fields before that distribution runs lets
    // us control how many of each role can exist simultaneously.
    //
    // HOW TO ACTIVATE: same procedure as §C above.
    //   Search dnSpy for "maxCops" / "maxSuspects" / "AssignRoles".
    // =========================================================================

    /*
    // TODO: Change typeof(TeamManager) to typeof(<real class from dnSpy>)
    [HarmonyPatch(typeof(TeamManager), GameIdentifiers.AssignRolesMethod)]
    internal static class Patch_TeamManager_AssignRoles
    {
        [HarmonyPrefix]
        private static void Prefix(
            TeamManager __instance,         // TODO: update type to match real class
            ref int     ___maxCops,         // TODO: rename to real field name
            ref int     ___maxSuspects)     // TODO: rename to real field name
        {
            Plugin.LogVerbose(
                $"TeamManager.{GameIdentifiers.AssignRolesMethod}: " +
                $"cops {___maxCops}→{Plugin.CfgMaxCops.Value}, " +
                $"suspects {___maxSuspects}→{Plugin.CfgMaxSuspects.Value}");

            ___maxCops     = Plugin.CfgMaxCops.Value;
            ___maxSuspects = Plugin.CfgMaxSuspects.Value;
        }
    }
    */


    // =========================================================================
    // §E  LOBBY UI PATCHES                                     [NEEDS dnSpy]
    //
    // Without a UI patch, extra players join at the network level but the lobby
    // screen may only render 4 avatar slots (the hardcoded default).  This
    // patch overrides the slot-refresh method so the UI scales to MaxPlayers.
    //
    // Strategy note: if the original method reads a `maxPlayers` field when
    // building the slot list, a Prefix that overrides that field before the
    // method runs is safer than a Postfix that tries to insert extra slots.
    //
    // HOW TO ACTIVATE: search dnSpy for "RefreshSlots" or scan the lobby
    // screen MonoBehaviour for a for-loop bounded by a player-count integer.
    // =========================================================================

    /*
    // TODO: Change typeof(LobbyUI) to typeof(<real class from dnSpy>)
    [HarmonyPatch(typeof(LobbyUI), GameIdentifiers.RefreshSlotsMethod)]
    internal static class Patch_LobbyUI_RefreshSlots
    {
        /// <summary>
        /// Prefix — intercepts the slot-count before the loop executes.
        ///
        /// If RefreshSlots takes an explicit `int slotCount` parameter, add it as
        /// `ref int slotCount` here and set it to Plugin.CfgMaxPlayers.Value.
        ///
        /// If it reads from a field instead of a parameter, also add that field as
        /// a `ref int ___maxPlayers` parameter and override it here.
        /// </summary>
        [HarmonyPrefix]
        private static void Prefix(LobbyUI __instance) // TODO: update type
        {
            Plugin.LogVerbose($"LobbyUI.{GameIdentifiers.RefreshSlotsMethod} prefix ran.");

            // Example — if the method has an explicit parameter `int count`:
            //   (add `ref int count` to the signature above and uncomment:)
            //   count = Plugin.CfgMaxPlayers.Value;

            // Example — if the method reads from a field `maxSlots`:
            //   (add `ref int ___maxSlots` to the signature above and uncomment:)
            //   ___maxSlots = Plugin.CfgMaxPlayers.Value;
        }
    }
    */


    // =========================================================================
    // §F  ALTERNATIVE NETWORKING BACKEND PATCHES
    //
    // Uncomment exactly ONE section that matches what is present in the game's
    // Managed folder, then add the corresponding reference in the .csproj.
    //
    // How to identify the networking library:
    //   Mirror.dll            → §F-1  Mirror
    //   FishNet.Runtime.dll   → §F-2  FishNet
    //   Photon.Realtime.dll   → §F-3  Photon PUN 2
    //   Fusion.dll            → §F-4  Photon Fusion
    //   Unity.Netcode.*.dll   → §F-5  Netcode for GameObjects (NGO)
    // =========================================================================

    // ── §F-1  Mirror ─────────────────────────────────────────────────────────
    // Mirror stores the connection cap in NetworkManager.maxConnections.
    /*
    [HarmonyPatch(typeof(Mirror.NetworkManager), "maxConnections",
                  MethodType.Setter)]
    internal static class Patch_Mirror_MaxConnections
    {
        [HarmonyPrefix]
        private static void Prefix(ref int value)
        {
            Plugin.LogVerbose(
                $"Mirror.NetworkManager.maxConnections setter: {value} → {Plugin.CfgMaxPlayers.Value}");
            value = Plugin.CfgMaxPlayers.Value;
        }
    }
    */

    // ── §F-2  FishNet ─────────────────────────────────────────────────────────
    // FishNet exposes ServerManager.SetMaximumClients(ushort value).
    /*
    [HarmonyPatch(typeof(FishNet.Managing.Server.ServerManager), "SetMaximumClients")]
    internal static class Patch_FishNet_MaxClients
    {
        [HarmonyPrefix]
        private static void Prefix(ref ushort value)
        {
            Plugin.LogVerbose(
                $"FishNet.ServerManager.SetMaximumClients: {value} → {Plugin.CfgMaxPlayers.Value}");
            value = (ushort)Plugin.CfgMaxPlayers.Value;
        }
    }
    */

    // ── §F-3  Photon PUN 2 ────────────────────────────────────────────────────
    // PUN 2 sets the player cap via RoomOptions.MaxPlayers in CreateRoom.
    /*
    [HarmonyPatch(typeof(Photon.Pun.PhotonNetwork), "CreateRoom")]
    internal static class Patch_PhotonPUN2_CreateRoom
    {
        [HarmonyPrefix]
        private static void Prefix(ref Photon.Realtime.RoomOptions roomOptions)
        {
            if (roomOptions == null) return;
            Plugin.LogVerbose(
                $"PhotonNetwork.CreateRoom: MaxPlayers {roomOptions.MaxPlayers} → {Plugin.CfgMaxPlayers.Value}");
            roomOptions.MaxPlayers = (byte)Plugin.CfgMaxPlayers.Value;
        }
    }
    */

    // ── §F-4  Photon Fusion ───────────────────────────────────────────────────
    // Fusion uses StartGameArgs.PlayerCount in NetworkRunner.StartGame.
    /*
    [HarmonyPatch(typeof(Fusion.NetworkRunner), "StartGame")]
    internal static class Patch_PhotonFusion_StartGame
    {
        [HarmonyPrefix]
        private static void Prefix(ref Fusion.StartGameArgs args)
        {
            Plugin.LogVerbose(
                $"Fusion.NetworkRunner.StartGame: PlayerCount {args.PlayerCount} → {Plugin.CfgMaxPlayers.Value}");
            args.PlayerCount = Plugin.CfgMaxPlayers.Value;
        }
    }
    */

    // ── §F-5  Netcode for GameObjects (NGO) ───────────────────────────────────
    // NGO caps concurrent clients via NetworkManager.MaxConnectedClients.
    /*
    [HarmonyPatch(typeof(Unity.Netcode.NetworkManager), "MaxConnectedClients",
                  MethodType.Getter)]
    internal static class Patch_NGO_MaxConnectedClients
    {
        [HarmonyPostfix]
        private static void Postfix(ref int __result)
        {
            Plugin.LogVerbose(
                $"NGO NetworkManager.MaxConnectedClients getter → {Plugin.CfgMaxPlayers.Value}");
            __result = Plugin.CfgMaxPlayers.Value;
        }
    }
    */


    // =========================================================================
    // §G  PATCH BOOTSTRAPPER
    //
    // Called from Plugin.Awake() BEFORE Harmony.PatchAll().  Applies patches
    // that cannot use [HarmonyPatch] attributes because their target assemblies
    // are not compile-time dependencies (e.g. Steamworks.NET).
    //
    // Add additional TryPatch* methods here for any other runtime-resolved type.
    // =========================================================================
    internal static class PatchBootstrapper
    {
        private static readonly ManualLogSource _log =
            BepInEx.Logging.Logger.CreateLogSource("NBMPBoot");

        /// <summary>
        /// Entry point called from <see cref="Plugin.Awake"/>.
        /// Chains all dynamic patch registration attempts.
        /// </summary>
        internal static void ApplyDynamicPatches(Harmony harmony)
        {
            TryPatchSteamworks(harmony);
            // Add future dynamic patch calls here, e.g.:
            //   TryPatchMirror(harmony);
        }

        // ── Steam ─────────────────────────────────────────────────────────────
        private static void TryPatchSteamworks(Harmony harmony)
        {
            var steamMatchmaking = AccessTools.TypeByName("Steamworks.SteamMatchmaking");

            if (steamMatchmaking == null)
            {
                _log.LogDebug("Steamworks.SteamMatchmaking not found — Steam patches skipped.");
                return;
            }

            // Patch CreateLobby
            var createLobby = AccessTools.Method(steamMatchmaking, "CreateLobby");
            if (createLobby != null)
            {
                harmony.Patch(
                    createLobby,
                    prefix: new HarmonyMethod(
                        typeof(SteamLobbyPatches),
                        nameof(SteamLobbyPatches.CreateLobby_Prefix)));

                _log.LogInfo("Patched SteamMatchmaking.CreateLobby");
            }
            else
            {
                _log.LogWarning("SteamMatchmaking.CreateLobby not found — skipped.");
            }

            // Patch SetLobbyMemberLimit
            var setLimit = AccessTools.Method(steamMatchmaking, "SetLobbyMemberLimit");
            if (setLimit != null)
            {
                harmony.Patch(
                    setLimit,
                    prefix: new HarmonyMethod(
                        typeof(SteamLobbyPatches),
                        nameof(SteamLobbyPatches.SetLobbyMemberLimit_Prefix)));

                _log.LogInfo("Patched SteamMatchmaking.SetLobbyMemberLimit");
            }
            else
            {
                _log.LogWarning("SteamMatchmaking.SetLobbyMemberLimit not found — skipped.");
            }
        }
    }
}
