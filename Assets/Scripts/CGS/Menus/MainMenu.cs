﻿/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

using CardGameDef;

namespace CGS.Menus
{
    public class MainMenu : MonoBehaviour
    {
        public const int MainMenuSceneIndex = 1;
        public const int PlayModeSceneIndex = 2;
        public const int DeckEditorSceneIndex = 3;
        public const int CardsExplorerSceneIndex = 4;
        public const string VersionMessage = "Ver. ";

        public Image currentBannerImage;
        public Image previousCardImage;
        public Image nextCardImage;
        public List<GameObject> selectableButtons;
        public GameObject quitButton;
        public Text versionText;

        private bool _wasLeft;
        private bool _wasRight;

        void OnEnable()
        {
            CardGameManager.Instance.OnSceneActions.Add(ResetGameSelectionCarousel);
        }

        void Start()
        {
#if (UNITY_ANDROID || UNITY_IOS)
            quitButton.SetActive(false);
#else
            quitButton.SetActive(true);
#endif
            versionText.text = VersionMessage + Application.version;
        }

        void Update()
        {
            if (CardGameManager.Instance.TopMenuCanvas != null)
                return;

            if ((Input.GetButtonDown(Inputs.Horizontal) || Input.GetAxis(Inputs.Horizontal) != 0) &&
                    (EventSystem.current.currentSelectedGameObject == null
                    || EventSystem.current.currentSelectedGameObject == selectableButtons[0].gameObject))
            {
                if (Input.GetAxis(Inputs.Horizontal) < 0 && !_wasLeft)
                    SelectPrevious();
                else if (Input.GetAxis(Inputs.Horizontal) > 0 && !_wasRight)
                    SelectNext();
            }
            else if ((Input.GetButtonDown(Inputs.Vertical) || Input.GetAxis(Inputs.Vertical) != 0)
                    && !selectableButtons.Contains(EventSystem.current.currentSelectedGameObject))
                EventSystem.current.SetSelectedGameObject(selectableButtons[1].gameObject);

            if (Input.GetKeyDown(Inputs.BluetoothReturn))
                EventSystem.current.currentSelectedGameObject?.GetComponent<Button>()?.onClick?.Invoke();
            else if (Input.GetButtonDown(Inputs.Sort))
                ShowGameSelectionMenu();
            else if (Input.GetButtonDown(Inputs.New))
                StartGame();
            else if (Input.GetButtonDown(Inputs.Load))
                JoinGame();
            else if (Input.GetButtonDown(Inputs.Save))
                EditDeck();
            else if (Input.GetButtonDown(Inputs.Filter))
                ExploreCards();
            else if (Input.GetKeyDown(KeyCode.Escape) || Input.GetButtonDown(Inputs.Cancel))
                Quit();

            _wasLeft = Input.GetAxis(Inputs.Horizontal) < 0;
            _wasRight = Input.GetAxis(Inputs.Horizontal) > 0;
        }

        public void ResetGameSelectionCarousel()
        {
            currentBannerImage.sprite = CardGameManager.Current.BannerImageSprite;
            previousCardImage.sprite = CardGameManager.Instance.Previous.CardBackImageSprite;
            nextCardImage.sprite = CardGameManager.Instance.Next.CardBackImageSprite;
        }

        public void SelectPrevious()
        {
            CardGameManager.Instance.Select(CardGameManager.Instance.Previous.Id);
        }

        public void SelectNext()
        {
            CardGameManager.Instance.Select(CardGameManager.Instance.Next.Id);
        }

        public void ShowGameSelectionMenu()
        {
            if (Time.timeSinceLevelLoad < 0.1)
                return;
            CardGameManager.Instance.Selector.Show();
        }

        public void StartGame()
        {
            if (Time.timeSinceLevelLoad < 0.1)
                return;
            CardGameManager.Instance.Discovery.Stop();
            SceneManager.LoadScene(PlayModeSceneIndex);
        }

        public void JoinGame()
        {
            if (Time.timeSinceLevelLoad < 0.1)
                return;
            CardGameManager.Instance.Discovery.SearchForHost();
            SceneManager.LoadScene(PlayModeSceneIndex);
        }

        public void EditDeck()
        {
            if (Time.timeSinceLevelLoad < 0.1)
                return;
            SceneManager.LoadScene(DeckEditorSceneIndex);
        }

        public void ExploreCards()
        {
            if (Time.timeSinceLevelLoad < 0.1)
                return;
            SceneManager.LoadScene(CardsExplorerSceneIndex);
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_WSA
            System.Diagnostics.Process.GetCurrentProcess().Kill();
#else
            Application.Quit();
#endif
        }
    }
}
