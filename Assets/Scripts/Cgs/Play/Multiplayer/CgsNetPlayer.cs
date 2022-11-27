/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CardGameDef;
using CardGameDef.Unity;
using Cgs.CardGameView;
using Cgs.CardGameView.Multiplayer;
using Cgs.Play.Drawer;
using Unity.Netcode;
using UnityEngine;

namespace Cgs.Play.Multiplayer
{
    public class CgsNetPlayer : NetworkBehaviour
    {
        public const string GameSelectionErrorMessage = "The host has selected a game that is not available!";
        public const string ShareDeckRequest = "Would you like to share the host's deck?";

        public ClientRpcParams OwnerClientRpcParams => new()
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] {OwnerClientId}
            }
        };

        public string Name
        {
            get => _name.Value;
            private set => _name.Value = value;
        }

        private readonly NetworkVariable<string> _name = new();

        public int Points
        {
            get => _points.Value;
            private set => _points.Value = value;
        }

        private readonly NetworkVariable<int> _points = new();

        public GameObject CurrentDeck
        {
            get => _currentDeck.Value;
            private set => _currentDeck.Value = value;
        }

        private readonly NetworkVariable<GameObject> _currentDeck = new();

        public bool IsDeckShared
        {
            get => _isDeckShared.Value;
            private set => _isDeckShared.Value = value;
        }

        private readonly NetworkVariable<bool> _isDeckShared = new();

        public int CurrentHand
        {
            get => _currentHand.Value;
            private set => _currentHand.Value = value;
        }

        private readonly NetworkVariable<int> _currentHand = new();

        public int DefaultRotation { get; private set; }

        public string GetHandCount()
        {
            var handCards = HandCards;
            return handCards.Count > 0 && CurrentHand >= 0 && CurrentHand < handCards.Count
                ? handCards[CurrentHand].Count.ToString()
                : string.Empty;
        }

        public IReadOnlyList<IReadOnlyList<UnityCard>> HandCards
        {
            // This getter is slow, so it should be cached when appropriate
            get
            {
                List<IReadOnlyList<UnityCard>> handCards = new();
                foreach (var stringList in _handCards)
                {
                    var cardList = stringList.ToListString().Select(cardId => CardGameManager.Current.Cards[cardId])
                        .ToList();
                    handCards.Add(cardList);
                }

                return handCards;
            }
        }

        private readonly NetworkList<CgsNetStringList> _handCards = new();

        private readonly NetworkList<CgsNetString> _handNames = new();

        public CardModel RemovedCard { get; set; }

        #region StartGame

        public override void OnNetworkSpawn()
        {
            if (!GetComponent<NetworkObject>().IsOwner)
                return;

            Debug.Log("[CgsNet Player] Starting local player...");
            CgsNetManager.Instance.LocalPlayer = this;
            RequestNameUpdate(PlayerPrefs.GetString(Scoreboard.PlayerNamePlayerPrefs, Scoreboard.DefaultPlayerName));
            RequestNewHand(CardDrawer.DefaultHandName);
            if (IsServer)
                CgsNetManager.Instance.playController.ShowDeckMenu();
            else
                RequestCardGameSelection();

            Debug.Log("[CgsNet Player] Started local player!");
        }

        private void RequestCardGameSelection()
        {
            Debug.Log("[CgsNet Player] Requesting game id...");
            SelectCardGameServerRpc();
        }

        [ServerRpc]
        private void SelectCardGameServerRpc()
        {
            Debug.Log("[CgsNet Player] Sending game id...");
            SelectCardGameOwnerClientRpc(CardGameManager.Current.Id,
                CardGameManager.Current.AutoUpdateUrl?.OriginalString, OwnerClientRpcParams);
        }

        [ClientRpc]
        // ReSharper disable once UnusedParameter.Local
        private void SelectCardGameOwnerClientRpc(string gameId, string autoUpdateUrl,
            // ReSharper disable once UnusedParameter.Local
            ClientRpcParams clientRpcParams = default)
        {
            Debug.Log($"[CgsNet Player] Game id is {gameId}! Loading game details...");
            if (!CardGameManager.Instance.AllCardGames.ContainsKey(gameId))
            {
                if (!Uri.IsWellFormedUriString(autoUpdateUrl, UriKind.Absolute))
                {
                    Debug.LogError(GameSelectionErrorMessage);
                    CardGameManager.Instance.Messenger.Show();
                    return;
                }

                StartCoroutine(DownloadGame(autoUpdateUrl));
            }
            else
            {
                CardGameManager.Instance.Select(gameId);
                StartCoroutine(WaitToStartGame());
            }

            ApplyPlayerRotationServerRpc();
        }

        [ServerRpc]
        private void ApplyPlayerRotationServerRpc()
        {
            ApplyPlayerRotationOwnerClientRpc(NetworkManager.Singleton.ConnectedClients.Count, OwnerClientRpcParams);
        }

        [ClientRpc]
        // ReSharper disable once UnusedParameter.Local
        private void ApplyPlayerRotationOwnerClientRpc(int playerCount, ClientRpcParams clientRpcParams = default)
        {
            if (playerCount % 4 == 0)
                DefaultRotation = 270;
            else if (playerCount % 3 == 0)
                DefaultRotation = 90;
            else if (playerCount % 2 == 0)
                DefaultRotation = 180;
            else
                DefaultRotation = 0;
            Debug.Log("[CgsNet Player] Set PlayMat rotation based off player count: " + DefaultRotation);
            CgsNetManager.Instance.playController.playArea.CurrentRotation = DefaultRotation;
        }

        private IEnumerator DownloadGame(string url)
        {
            Debug.Log($"[CgsNet Player] Downloading game from {url}...");
            yield return CardGameManager.Instance.GetCardGame(url);
            yield return WaitToStartGame();
        }

        private IEnumerator WaitToStartGame()
        {
            while (CardGameManager.Current.IsDownloading)
                yield return null;

            Debug.Log("[CgsNet Player] Game loaded and ready!");

            switch (CardGameManager.Current.DeckSharePreference)
            {
                case SharePreference.Individual:
                    CgsNetManager.Instance.playController.ShowDeckMenu();
                    break;
                case SharePreference.Share:
                    RequestSharedDeck();
                    break;
                case SharePreference.Ask:
                default:
                    CardGameManager.Instance.Messenger.Ask(ShareDeckRequest,
                        CgsNetManager.Instance.playController.ShowDeckMenu, RequestSharedDeck);
                    break;
            }

            RequestNewHand(CardDrawer.DefaultHandName);
        }

        #endregion

        #region Score

        public void RequestNameUpdate(string playerName)
        {
            UpdateNameServerRpc(playerName);
        }

        [ServerRpc]
        private void UpdateNameServerRpc(string playerName)
        {
            Name = playerName;
        }

        public void RequestPointsUpdate(int points)
        {
            UpdatePointsServerRpc(points);
        }

        [ServerRpc]
        private void UpdatePointsServerRpc(int points)
        {
            Points = points;
        }

        #endregion

        #region CardStacks

        public void RequestNewDeck(string deckName, IEnumerable<UnityCard> cards)
        {
            Debug.Log($"[CgsNet Player] Requesting new deck {deckName}...");
            CreateCardStackServerRpc(deckName, cards.Select(card => (CgsNetString) card.Id).ToArray(), true,
                CgsNetManager.Instance.playController.NewDeckPosition);
        }

        public void RequestNewCardStack(string stackName, IEnumerable<UnityCard> cards, Vector2 position)
        {
            Debug.Log($"[CgsNet Player] Requesting new card stack {stackName}...");
            CreateCardStackServerRpc(stackName, cards.Select(card => (CgsNetString) card.Id).ToArray(), false,
                position);
        }

        [ServerRpc]
        // ReSharper disable once ParameterTypeCanBeEnumerable.Local
        private void CreateCardStackServerRpc(string stackName, CgsNetString[] cardIds, bool isDeck, Vector2 position)
        {
            Debug.Log($"[CgsNet Player] Creating new card stack {stackName}...");
            var cardStack = CgsNetManager.Instance.playController.CreateCardStack(stackName,
                cardIds.Select(cardId => CardGameManager.Current.Cards[cardId]).ToList(), position);
            cardStack.MyNetworkObject.Spawn();
            if (isDeck)
                CurrentDeck = cardStack.gameObject;
            Debug.Log($"[CgsNet Player] Created new card stack {stackName}!");
        }

        private void RequestSharedDeck()
        {
            Debug.Log("[CgsNet Player] Requesting shared deck..");
            ShareDeckServerRpc();
        }

        [ServerRpc]
        private void ShareDeckServerRpc()
        {
            Debug.Log("[CgsNet Player] Sending shared deck...");
            ShareDeckOwnerClientRpc(CgsNetManager.Instance.LocalPlayer.CurrentDeck, OwnerClientRpcParams);
        }

        [ClientRpc]
        // ReSharper disable once UnusedParameter.Local
        private void ShareDeckOwnerClientRpc(NetworkObjectReference deckStack,
            // ReSharper disable once UnusedParameter.Local
            ClientRpcParams clientRpcParams = default)
        {
            Debug.Log("[CgsNet Player] Received shared deck!");
            CurrentDeck = ((NetworkObject) deckStack).gameObject;
            IsDeckShared = true;
            CgsNetManager.Instance.playController.PromptForHand();
        }

        public void RequestShuffle(GameObject toShuffle)
        {
            Debug.Log("[CgsNet Player] Requesting shuffle...");
            ShuffleServerRpc(toShuffle);
        }

        [ServerRpc]
        // ReSharper disable once MemberCanBeMadeStatic.Local
        private void ShuffleServerRpc(NetworkObjectReference toShuffle)
        {
            Debug.Log("[CgsNet Player] Shuffling!");
            var cardStack = ((NetworkObject) toShuffle).GetComponent<CardStack>();
            cardStack.DoShuffle();
        }

        public void RequestInsert(GameObject stack, int index, string cardId)
        {
            Debug.Log($"[CgsNet Player] Requesting insert {cardId} at {index}...");
            InsertServerRpc(stack, index, cardId);
        }

        [ServerRpc]
        // ReSharper disable once MemberCanBeMadeStatic.Local
        private void InsertServerRpc(NetworkObjectReference stack, int index, string cardId)
        {
            Debug.Log($"[CgsNet Player] Insert {cardId} at {index}!");
            var cardStack = ((NetworkObject) stack).GetComponent<CardStack>();
            cardStack.Insert(index, cardId);
        }

        public void RequestRemoveAt(GameObject stack, int index)
        {
            Debug.Log($"[CgsNet Player] Requesting remove at {index}...");
            RemoveAtServerRpc(stack, index);
        }

        [ServerRpc]
        // ReSharper disable once MemberCanBeMadeStatic.Local
        private void RemoveAtServerRpc(NetworkObjectReference stack, int index)
        {
            Debug.Log($"[CgsNet Player] Remove at {index}!");
            var cardStack = ((NetworkObject) stack).GetComponent<CardStack>();
            var removedCardId = cardStack.RemoveAt(index);
            SyncRemovedCardOwnerClientRpc(removedCardId, OwnerClientRpcParams);
        }

        [ClientRpc]
        // ReSharper disable once UnusedParameter.Local
        private void SyncRemovedCardOwnerClientRpc(string removedCardId, ClientRpcParams clientRpcParams = default)
        {
            if (RemovedCard != null)
                RemovedCard.Value = CardGameManager.Current.Cards[removedCardId];
            RemovedCard = null;
        }

        public void RequestDeal(GameObject stack, int count)
        {
            Debug.Log($"[CgsNet Player] Requesting deal {count}...");
            DealServerRpc(stack, count);
        }

        [ServerRpc]
        // ReSharper disable once MemberCanBeMadeStatic.Local
        private void DealServerRpc(NetworkObjectReference stack, int count)
        {
            Debug.Log($"[CgsNet Player] Dealing {count}!");
            var cardStack = ((NetworkObject) stack).GetComponent<CardStack>();
            var cardIds = new CgsNetString[count];
            for (var i = 0; i < count && cardStack.Cards.Count > 0; i++)
                cardIds[i] = cardStack.PopCard();
            DealClientRpc(cardIds, OwnerClientRpcParams);
        }

        [ClientRpc]
        // ReSharper disable once ParameterTypeCanBeEnumerable.Local
        // ReSharper disable once MemberCanBeMadeStatic.Local
        // ReSharper disable once UnusedParameter.Local
        private void DealClientRpc(CgsNetString[] cardIds, ClientRpcParams clientRpcParams = default)
        {
            Debug.Log($"[CgsNet Player] Dealt {cardIds}!");
            CgsNetManager.Instance.playController.AddCardsToHand(
                cardIds.Where(cardId => !string.IsNullOrEmpty(cardId) && !UnityCard.Blank.Id.Equals(cardId))
                    .Select(cardId => CardGameManager.Current.Cards[cardId]));
        }

        #endregion

        #region Hands

        public void RequestNewHand(string handName)
        {
            Debug.Log($"[CgsNet Player] Requesting new hand {handName}...");
            AddHandServerRpc(handName);
        }

        [ServerRpc]
        private void AddHandServerRpc(string handName)
        {
            Debug.Log($"[CgsNet Player] Add hand {handName}!");
            _handCards.Add(new CgsNetStringList());
            _handNames.Add(handName);
            CurrentHand = _handNames.Count - 1;
            UseHandClientRpc(CurrentHand, OwnerClientRpcParams);
        }

        [ClientRpc]
        // ReSharper disable once MemberCanBeMadeStatic.Local
        // ReSharper disable once UnusedParameter.Local
        private void UseHandClientRpc(int handIndex, ClientRpcParams clientRpcParams = default)
        {
            CgsNetManager.Instance.playController.drawer.SelectTab(handIndex);
        }

        public void RequestUseHand(int handIndex)
        {
            Debug.Log($"[CgsNet Player] Requesting use hand {handIndex}...");
            UseHandServerRpc(handIndex);
        }

        [ServerRpc]
        private void UseHandServerRpc(int handIndex)
        {
            Debug.Log($"[CgsNet Player] Use hand {handIndex}!");
            CurrentHand = handIndex;
        }

        public void RequestSyncHand(int handIndex, CgsNetString[] cardIds)
        {
            Debug.Log($"[CgsNet Player] Requesting sync hand {handIndex} to {cardIds.Length}...");
            SyncHandServerRpc(handIndex, cardIds);
        }

        [ServerRpc]
        private void SyncHandServerRpc(int handIndex, CgsNetString[] cardIds)
        {
            Debug.Log($"[CgsNet Player] Sync hand {handIndex} to {cardIds.Length} cards on Server!");
            if (handIndex < 0 || handIndex >= _handCards.Count)
            {
                Debug.LogError($"[CgsNet Player] {handIndex} is out of bounds of {_handCards.Count}");
                return;
            }

            _handCards[handIndex] = CgsNetStringList.Of(cardIds);
            SyncHandClientRpc(handIndex, cardIds, OwnerClientRpcParams);
        }

        [ClientRpc]
        // ReSharper disable once MemberCanBeMadeStatic.Local
        // ReSharper disable once UnusedParameter.Local
        private void SyncHandClientRpc(int handIndex, CgsNetString[] cardIds, ClientRpcParams clientRpcParams = default)
        {
            Debug.Log($"[CgsNet Player] Sync hand {handIndex} to {cardIds.Length} cards on client!");
            CgsNetManager.Instance.playController.drawer.SyncHand(handIndex, cardIds);
        }

        #endregion

        #region Cards

        public void MoveCardToServer(CardZone cardZone, CardModel cardModel)
        {
            var cardModelTransform = cardModel.transform;
            cardModelTransform.SetParent(cardZone.transform);
            cardModel.SnapToGrid();
            cardModel.Position = ((RectTransform) cardModelTransform).localPosition;
            cardModel.Rotation = cardModelTransform.rotation;
            SpawnCardServerRpc(cardModel.Id, cardModel.Position, cardModel.Rotation, cardModel.IsFacedown);
            if (cardModel.IsOnline && cardModel.LacksOwnership)
                DespawnCardServerRpc(cardModel.gameObject);
            Destroy(cardModel.gameObject);
        }

        [ServerRpc]
        // ReSharper disable once MemberCanBeMadeStatic.Local
        private void SpawnCardServerRpc(string cardId, Vector3 position, Quaternion rotation, bool isFacedown)
        {
            var playController = CgsNetManager.Instance.playController;
            var newCardGameObject = Instantiate(playController.cardModelPrefab, playController.playMat.transform);
            var cardModel = newCardGameObject.GetComponent<CardModel>();
            cardModel.Value = CardGameManager.Current.Cards[cardId];
            cardModel.Position = position;
            cardModel.Rotation = rotation;
            cardModel.IsFacedown = isFacedown;
            PlayController.SetPlayActions(cardModel);
            cardModel.MyNetworkObject.Spawn();
            cardModel.HideHighlightClientRpc();
        }

        [ServerRpc]
        // ReSharper disable once MemberCanBeMadeStatic.Local
        private void DespawnCardServerRpc(NetworkObjectReference toDespawn)
        {
            var go = ((NetworkObject) toDespawn).gameObject;
            go.GetComponent<NetworkObject>().Despawn();
            Destroy(go);
        }

        #endregion

        #region Dice

        public void RequestNewDie(int min, int max)
        {
            CreateDieServerRpc(min, max);
        }

        [ServerRpc]
        // ReSharper disable once MemberCanBeMadeStatic.Local
        private void CreateDieServerRpc(int min, int max)
        {
            var die = CgsNetManager.Instance.playController.CreateDie(min, max);
            die.MyNetworkObject.Spawn();
        }

        #endregion

        #region RestartGame

        public void RequestRestart()
        {
            Debug.Log("[CgsNet Player] Requesting restart!...");
            RestartServerRpc();
        }

        [ServerRpc]
        // ReSharper disable once MemberCanBeMadeStatic.Local
        private void RestartServerRpc()
        {
            Debug.Log("[CgsNet Player] Game server to restart!...");
            CgsNetManager.Instance.Restart();
        }

        [ClientRpc]
        // ReSharper disable once UnusedParameter.Global
        public void RestartClientRpc(ClientRpcParams clientRpcParams = default)
        {
            Debug.Log("[CgsNet Player] Game is restarting!...");
            CgsNetManager.Instance.playController.ResetPlayArea();
            CgsNetManager.Instance.playController.drawer.Clear();
            CurrentDeck = null;
            StartCoroutine(WaitToRestartGame());
        }

        private IEnumerator WaitToRestartGame()
        {
            if (IsServer || CardGameManager.Current.DeckSharePreference == SharePreference.Individual)
            {
                CgsNetManager.Instance.playController.ShowDeckMenu();
                Debug.Log("[CgsNet Player] Game restarted!");
                yield break;
            }

            yield return null;

            Debug.Log("[CgsNet Player] Game restarted!");

            switch (CardGameManager.Current.DeckSharePreference)
            {
                case SharePreference.Individual:
                    CgsNetManager.Instance.playController.ShowDeckMenu();
                    break;
                case SharePreference.Share:
                    RequestSharedDeck();
                    break;
                case SharePreference.Ask:
                default:
                    CardGameManager.Instance.Messenger.Ask(ShareDeckRequest,
                        CgsNetManager.Instance.playController.ShowDeckMenu, RequestSharedDeck);
                    break;
            }
        }

        #endregion
    }
}
