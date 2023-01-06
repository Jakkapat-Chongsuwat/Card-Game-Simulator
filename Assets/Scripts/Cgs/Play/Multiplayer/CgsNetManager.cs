/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace Cgs.Play.Multiplayer
{
    public class CgsNetManager : NetworkManager
    {
        public const string GenericConnectionErrorMessage =
            "Exception thrown when attempting to connect to Server. Exception: ";

        public const int DefaultPort = 7777;
        private const int MaxPlayers = 10;

        public static CgsNetManager Instance => (CgsNetManager) Singleton;

        public bool IsOnline => IsHost || IsConnectedClient;

        public CgsNetPlayer LocalPlayer { get; set; }

        public UnityTransport Transport
        {
            get => (UnityTransport) NetworkConfig.NetworkTransport;
            set => NetworkConfig.NetworkTransport = value;
        }

        public Transports Transports { get; private set; }

        public string RoomIdIp => "127.0.0.1".Equals(Transport.ConnectionData.Address,
            StringComparison.Ordinal)
            ? RoomId
            : Transport.ConnectionData.Address;

        private string RoomId => PlayController.Instance != null && PlayController.Instance.Lobby != null &&
                                 !string.IsNullOrEmpty(CurrentLobby?.LobbyCode)
            ? CurrentLobby.LobbyCode
            : Dns.GetHostEntry(Dns.GetHostName()).AddressList.First(f => f.AddressFamily == AddressFamily.InterNetwork)
                .ToString();

        private Lobby CurrentLobby
        {
            get => _currentLobby;
            set
            {
                if (_currentLobby != null)
                {
                    var playerId = AuthenticationService.Instance.PlayerId;
                    if (_currentLobby.HostId == playerId)
                        LobbyService.Instance.DeleteLobbyAsync(_currentLobby.Id);
                    else
                        LobbyService.Instance.RemovePlayerAsync(_currentLobby.Id, playerId);
                }

                _currentLobby = value;
            }
        }

        private Lobby _currentLobby;

        private void Start()
        {
            Transports = GetComponent<Transports>();
            SignInAnonymouslyAsync();
        }

        private static async void SignInAnonymouslyAsync()
        {
            try
            {
                await UnityServices.InitializeAsync();
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log("[CgsNet] Sign in anonymously succeeded!");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Debug.LogError("[CgsNet] Sign in anonymously failed!");
            }
        }

        public void StartBroadcastHost()
        {
            StartCoroutine(BroadcastHostCoroutine());
        }

        private IEnumerator BroadcastHostCoroutine()
        {
            var serverRelayUtilityTask = AllocateRelayServerAndGetCode(MaxPlayers);
            while (!serverRelayUtilityTask.IsCompleted)
                yield return null;

            if (serverRelayUtilityTask.IsFaulted)
            {
                Debug.LogError(GenericConnectionErrorMessage + serverRelayUtilityTask.Exception?.Message);
                CardGameManager.Instance.Messenger.Show(GenericConnectionErrorMessage +
                                                        serverRelayUtilityTask.Exception?.Message);
                yield break;
            }

            var relayServerData = serverRelayUtilityTask.Result;
            Transport = Transports.relayUnityTransport;
            Transport.SetRelayServerData(relayServerData.Item1);
            StartHost();

            yield return null;

            Debug.Log($"[CgsNet] Creating lobby with relay code {relayServerData.Item2}...");

            var createLobbyUtilityTask = CreateLobby(relayServerData.Item2);
            while (!createLobbyUtilityTask.IsCompleted)
                yield return null;

            if (createLobbyUtilityTask.IsFaulted)
            {
                Debug.LogError(GenericConnectionErrorMessage + createLobbyUtilityTask.Exception?.Message);
                CardGameManager.Instance.Messenger.Show(GenericConnectionErrorMessage +
                                                        createLobbyUtilityTask.Exception?.Message);
                yield break;
            }

            Debug.Log($"[CgsNet] Created lobby with lobby code {createLobbyUtilityTask.Result.LobbyCode} success!");

            CurrentLobby = createLobbyUtilityTask.Result;

            while (IsOnline)
            {
                LobbyService.Instance.SendHeartbeatPingAsync(CurrentLobby.Id);
                yield return new WaitForSecondsRealtime(15);
            }

            CurrentLobby = null;
        }

        private static async Task<Tuple<RelayServerData, string>> AllocateRelayServerAndGetCode(int maxConnections,
            string region = null)
        {
            Allocation allocation;
            try
            {
                allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections, region);
            }
            catch (Exception e)
            {
                Debug.LogError($"Relay allocation request failed {e.Message}");
                throw;
            }

            Debug.Log($"[CgsNet] server: {allocation.ConnectionData[0]} {allocation.ConnectionData[1]}");
            Debug.Log($"[CgsNet] server: {allocation.AllocationId}");

            string relayCode;
            try
            {
                relayCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                Debug.Log($"[CgsNet] RelayCode: {relayCode}");
            }
            catch
            {
                Debug.LogError("Relay create join code request failed");
                throw;
            }

            return Tuple.Create(new RelayServerData(allocation, "dtls"), relayCode);
        }

        private static async Task<Lobby> CreateLobby(string relayCode)
        {
            Lobby lobby;
            var data = new Dictionary<string, DataObject>
            {
                [LobbyData.KeyRelayCode] = new(DataObject.VisibilityOptions.Public, relayCode)
            };
            var createLobbyOptions = new CreateLobbyOptions
            {
                Data = data,
                Player = new Player(id: AuthenticationService.Instance.PlayerId)
            };
            try
            {
                lobby = await LobbyService.Instance.CreateLobbyAsync(CardGameManager.Current.Name, MaxPlayers,
                    createLobbyOptions);
            }
            catch
            {
                Debug.LogError("Lobby create request failed");
                throw;
            }

            return lobby;
        }

        public void StartJoin(string address, ushort port = DefaultPort)
        {
            Transport = Transports.unityTransport;
            Transport.SetConnectionData(address, port);
            StartClient();
        }

        public void StartJoinRelay(string relayCode)
        {
            StartCoroutine(JoinRelayCoroutine(relayCode));
        }

        private IEnumerator JoinRelayCoroutine(string relayCode)
        {
            var clientRelayUtilityTask = JoinRelayServerFromJoinCode(relayCode);
            while (!clientRelayUtilityTask.IsCompleted)
                yield return null;

            if (clientRelayUtilityTask.IsFaulted)
            {
                Debug.LogError(GenericConnectionErrorMessage + clientRelayUtilityTask.Exception?.Message);
                CardGameManager.Instance.Messenger.Show(GenericConnectionErrorMessage +
                                                        clientRelayUtilityTask.Exception?.Message);
                yield break;
            }

            var relayServerData = clientRelayUtilityTask.Result;

            Transport = Transports.relayUnityTransport;
            Transport.SetRelayServerData(relayServerData);
            StartClient();
        }

        private static async Task<RelayServerData> JoinRelayServerFromJoinCode(string joinCode)
        {
            JoinAllocation allocation;
            try
            {
                allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            }
            catch
            {
                Debug.LogError("Relay create join code request failed");
                throw;
            }

            Debug.Log($"[CgsNet] client: {allocation.ConnectionData[0]} {allocation.ConnectionData[1]}");
            Debug.Log($"[CgsNet] host: {allocation.HostConnectionData[0]} {allocation.HostConnectionData[1]}");
            Debug.Log($"[CgsNet] client: {allocation.AllocationId}");

            return new RelayServerData(allocation, "dtls");
        }

        public void StartJoinLobby(string lobbyCode)
        {
            StartCoroutine(JoinLobbyCoroutine(lobbyCode));
        }

        private IEnumerator JoinLobbyCoroutine(string lobbyCode)
        {
            var joinLobbyTask = JoinLobby(lobbyCode);
            while (!joinLobbyTask.IsCompleted)
                yield return null;

            if (joinLobbyTask.IsFaulted)
            {
                Debug.LogError(GenericConnectionErrorMessage + joinLobbyTask.Exception?.Message);
                CardGameManager.Instance.Messenger.Show(
                    GenericConnectionErrorMessage + joinLobbyTask.Exception?.Message);
                yield break;
            }

            var lobby = joinLobbyTask.Result;
            if (lobby is {Data: { }} && lobby.Data.TryGetValue(LobbyData.KeyRelayCode, out var dataObject))
            {
                yield return JoinRelayCoroutine(dataObject.Value);
            }
            else
            {
                Debug.LogError(LobbyMenu.InvalidServerErrorMessage);
                CardGameManager.Instance.Messenger.Show(LobbyMenu.InvalidServerErrorMessage);
            }
        }

        private static async Task<Lobby> JoinLobby(string lobbyCode)
        {
            Lobby lobby;
            try
            {
                lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode);
            }
            catch
            {
                Debug.LogError("Lobby join request failed");
                throw;
            }

            return lobby;
        }

        public void Stop()
        {
            Shutdown();
            CurrentLobby = null;
            StopAllCoroutines();
        }

        private void OnApplicationQuit()
        {
            CurrentLobby = null;
        }
    }
}
