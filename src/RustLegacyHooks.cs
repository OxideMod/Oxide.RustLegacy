﻿using Oxide.Core;
using Oxide.Core.Plugins;
using Rust;
using uLink;

namespace Oxide.Game.RustLegacy
{
    /// <summary>
    /// Game hooks and wrappers for the core Rust Legacy plugin
    /// </summary>
    public partial class RustLegacyCore : CSPlugin
    {
        #region Player Hooks

        /// <summary>
        /// Called when the player is attempting to connect
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="approval"></param>
        /// <param name="acceptor"></param>
        [HookMethod("IOnUserApprove")]
        private object IOnUserApprove(ClientConnection connection, NetworkPlayerApproval approval, ConnectionAcceptor acceptor)
        {
            // Reject invalid connections
            if (connection.UserID == 0 || string.IsNullOrEmpty(connection.UserName))
            {
                approval.Deny(uLink.NetworkConnectionError.ConnectionBanned);
                return true;
            }

            // "Already connected" fix
            var netUser = NetUser.FindByUserID(connection.UserID);
            if (netUser != null)
            {
                Interface.Oxide.LogInfo($"Kicking existing {netUser.displayName} ({netUser.userID}) player (already connected fix)");
                netUser.Kick(NetError.AlreadyConnectedToAnotherServer, false);
            }

            var id = connection.UserID.ToString();
            var ip = approval.ipAddress;

            Covalence.PlayerManager.PlayerJoin(connection.UserID, connection.UserName); // TODO: Handle this automatically

            // Call out and see if we should reject
            var loginSpecific = Interface.Call("CanClientLogin", connection);
            var loginCovalence = Interface.Call("CanUserLogin", connection.UserName, id, ip);
            var canLogin = loginSpecific ?? loginCovalence; // TODO: Fix 'RustLegacyCore' hook conflict when both return

            // Check if player can login
            if (canLogin is string || (canLogin is bool && !(bool)canLogin))
            {
                // Reject the player with the message
                Notice.Popup(connection.netUser.networkPlayer, "", canLogin is string ? canLogin.ToString() : "Connection was rejected", 10f); // TODO: Localization
                approval.Deny(uLink.NetworkConnectionError.NoError);
                return true;
            }

            // Call the approval hooks
            var approvedSpecific = Interface.Call("OnUserApprove", connection, approval, acceptor);
            var approvedCovalence = Interface.Call("OnUserApproved", connection.UserName, id, ip);
            return approvedSpecific ?? approvedCovalence; // TODO: Fix 'RustLegacyCore' hook conflict when both return
        }

        /// <summary>
        /// Called when the player has connected
        /// </summary>
        /// <param name="netUser"></param>
        [HookMethod("OnPlayerConnected")]
        private void OnPlayerConnected(NetUser netUser)
        {
            // Update player's permissions group and name
            if (permission.IsLoaded)
            {
                var id = netUser.userID.ToString();
                permission.UpdateNickname(id, netUser.displayName);
                var defaultGroups = Interface.Oxide.Config.Options.DefaultGroups;
                if (!permission.UserHasGroup(id, defaultGroups.Players)) permission.AddUserGroup(id, defaultGroups.Players);
                if (netUser.CanAdmin() && !permission.UserHasGroup(id, defaultGroups.Administrators)) permission.AddUserGroup(id, defaultGroups.Administrators);
            }

            // Let covalence know
            Covalence.PlayerManager.PlayerConnected(netUser);
            var iplayer = Covalence.PlayerManager.FindPlayerById(netUser.userID.ToString());
            if (iplayer != null)
            {
                netUser.IPlayer = iplayer;
                Interface.Call("OnUserConnected", iplayer);
            }
        }

        /// <summary>
        /// Called when the player has disconnected
        /// </summary>
        /// <param name="netPlayer"></param>
        [HookMethod("OnPlayerDisconnected")]
        private void OnPlayerDisconnected(uLink.NetworkPlayer netPlayer)
        {
            var netUser = netPlayer.GetLocalData() as NetUser;
            if (netUser == null) return;

            // Let covalence know
            Interface.Call("OnUserDisconnected", netUser.IPlayer, "Unknown"); // TODO: Localization
            Covalence.PlayerManager.PlayerDisconnected(netUser);

            // Delay removing player until OnPlayerDisconnect has fired in plugins
            Interface.Oxide.NextTick(() =>
            {
                if (playerData.ContainsKey(netUser)) playerData.Remove(netUser);
            });
        }

        /// <summary>
        /// Called when the player spawns
        /// </summary>
        /// <param name="client"></param>
        [HookMethod("OnPlayerSpawn")]
        private void OnPlayerSpawn(PlayerClient client)
        {
            // Call universal hook
            Interface.Call("OnUserSpawn", Covalence.PlayerManager.FindPlayerById(client.userID.ToString()));
        }

        /// <summary>
        /// Called when the player has spawned
        /// </summary>
        /// <param name="client"></param>
        [HookMethod("OnPlayerSpawned")]
        private void OnPlayerSpawned(PlayerClient client)
        {
            var netUser = client.netUser;
            if (!playerData.ContainsKey(netUser)) playerData.Add(netUser, new PlayerData());
            playerData[netUser].character = client.controllable.GetComponent<Character>();
            playerData[netUser].inventory = client.controllable.GetComponent<PlayerInventory>();

            // Call universal hook
            Interface.Call("OnUserSpawned", client.netUser.IPlayer);
        }

        /// <summary>
        /// Called when the player is speaking
        /// </summary>
        /// <param name="netUser"></param>
        [HookMethod("IOnPlayerVoice")]
        private object IOnPlayerVoice(NetUser netUser) => (int?)Interface.Call("OnPlayerVoice", netUser, VoiceCom.playerList);

        #endregion Player Hooks
    }

    public class OnServerInitHook : UnityEngine.MonoBehaviour
    {
        public void OnDestroy() => Interface.Call("OnServerInitialized", null);
    }
}
