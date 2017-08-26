﻿using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CardImageRepository
{
    private static Dictionary<string, Sprite> allCardImages = new Dictionary<string, Sprite>();

    public static string GetCardImageName(Card card)
    {
        char[] cardNameAlphaNum = card.Name.Where(c => (char.IsLetterOrDigit(c) ||
                                  char.IsWhiteSpace(c) ||
                                  c == '-')).ToArray(); 
        string cardImageName = new string(cardNameAlphaNum);
        cardImageName = cardImageName.Replace(" ", "_").Replace("-", "_").ToLower() + ".jpg";
        return cardImageName;

    }

    public static bool TryGetCachedCardImage(Card card, out Sprite image)
    {
        string cardImageName = GetCardImageName(card);
        bool imageCached = allCardImages.TryGetValue(cardImageName, out image);
        return imageCached;
    }

    public static IEnumerator GetAndCacheCardImage(Card card)
    {
        string cardImageName = GetCardImageName(card);
        Debug.Log("Loading and caching card image: " + cardImageName);

        string imageFilePath = CardGameManager.CurrentCardGame.FilePathBase + "/" + card.SetCode;
        string imageFileURL = "file://" + imageFilePath + "/" + cardImageName;
        Debug.Log(" Attempting to load card image from: " + imageFileURL);
        WWW loadImage = new WWW(imageFileURL);
        yield return loadImage;

        if (!string.IsNullOrEmpty(loadImage.error)) {
            string imageWebURL = CardGameManager.CurrentCardGame.CardImageBaseURL + cardImageName;
            Debug.Log(" Attempting to load card image from: " + imageWebURL);
            loadImage = new WWW(imageWebURL);
            yield return loadImage;

            if (!string.IsNullOrEmpty(loadImage.error)) {
                Debug.LogWarning("Had an error loading from web: " + loadImage.error);
                // TODO: HANDLING FOR WHEN WE FAIL TO LOAD FROM WEB
            }

            Debug.Log(" Saving image to file");
            if (!System.IO.Directory.Exists(imageFilePath)) {
                Debug.Log(" Image file directory does not exist, so creating it");
                Directory.CreateDirectory(imageFilePath);
            }
            File.WriteAllBytes(imageFilePath + "/" + cardImageName, loadImage.bytes);
            Debug.Log(" Image saved to file");
        }

        Debug.Log(" Finalizing image sprite and caching it");
        Sprite cardImage = Sprite.Create(loadImage.texture, new Rect(0, 0, loadImage.texture.width, loadImage.texture.height), new Vector2(0.5f, 0.5f));
        allCardImages [cardImageName] = cardImage;
    }

    public static Sprite DefaultImage {
        get { return CardGameManager.CurrentCardGame.CardBackImage; }
    }
}
