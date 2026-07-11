# Pinder End-to-End Unity Game Flow: Technical Blueprints & Implementation Plans

This document provides high-level technical blueprints and implementation plans for the 6 outstanding Unity stages of the Pinder dating game. These blueprints are designed for direct insertion into Atlassian Jira tickets, matching existing Epic and Story keys.

All blueprints follow the core-boundary guidelines: the Unity client remains decoupled from downstream LLM reasoning and relies entirely on pinder-core for mechanics, turn resolution, rules, and local state management.

---

## 1. Booting & Intro Video (PIND-01 / Epic PNDR-2)

### 1.1 White Box Version

The Boot scene manages initial startup, verifies version alignment with the FastAPI backend, loads local static game definitions from StreamingAssets, and plays the cinematic intro.

```csharp
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;
using PinderBridge;

public class BootController : MonoBehaviour
{
    [Header("UI & Video References")]
    [SerializeField] private VideoPlayer introVideoPlayer;
    [SerializeField] private GameObject loadingSpinner;
    [SerializeField] private GameObject errorMessagePanel;
    [SerializeField] private TMPro.TextMeshProUGUI errorText;

    [Header("Pinder Data Loader")]
    [SerializeField] private PinderCoreLoader coreLoader;

    private const string ServerStatusEndpoint = "https://api.pinder.games/api/v1/status";
    private const int ExpectedApiVersion = 1;

    [System.Serializable]
    private class ServerStatusResponse
    {
        public string status;
        public int apiVersion;
        public int[] supportedVersions;
    }

    private void Start()
    {
        loadingSpinner.SetActive(true);
        errorMessagePanel.SetActive(false);
        StartCoroutine(BootSequenceCoroutine());
    }

    private IEnumerator BootSequenceCoroutine()
    {
        // Step 1: Handshake with backend to verify API version contract compatibility
        yield return StartCoroutine(CheckServerCompatibilityCoroutine());

        // Step 2: Initialize PinderCoreLoader to parse local JSON & YAML rules
        if (coreLoader != null && !coreLoader.IsLoaded)
        {
            yield return StartCoroutine(coreLoader.LoadCoroutine());
        }

        // Step 3: Trigger cinematic intro video playback
        loadingSpinner.SetActive(false);
        if (introVideoPlayer != null)
        {
            introVideoPlayer.loopPointReached += OnVideoFinished;
            introVideoPlayer.Play();
        }
        else
        {
            OnVideoFinished(null);
        }
    }

    private IEnumerator CheckServerCompatibilityCoroutine()
    {
        string requestPayload = $"{{\"apiVersion\": {ExpectedApiVersion}}}";
        using var request = new UnityWebRequest(ServerStatusEndpoint, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(requestPayload);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            HandleBootError($"Network Error: {request.error}. Please check your connection.");
            yield break;
        }

        try
        {
            var response = JsonUtility.FromJson<ServerStatusResponse>(request.downloadHandler.text);
            if (response.apiVersion != ExpectedApiVersion)
            {
                HandleBootError($"Version Mismatch! Client expects API version {ExpectedApiVersion}, server supports [{string.Join(", ", response.supportedVersions)}]. Please update.");
                yield break;
            }
        }
        catch (Exception ex)
        {
            HandleBootError($"Deserialization Error: Unable to parse compatibility handshake. {ex.Message}");
            yield break;
        }
    }

    private void HandleBootError(string message)
    {
        Debug.LogError($"[BootController] {message}");
        loadingSpinner.SetActive(false);
        errorText.text = message;
        errorMessagePanel.SetActive(true);
    }

    public void SkipVideo()
    {
        if (introVideoPlayer != null && introVideoPlayer.isPlaying)
        {
            introVideoPlayer.Stop();
        }
        OnVideoFinished(null);
    }

    private void OnVideoFinished(VideoPlayer source)
    {
        Debug.Log("[BootController] Intro finished. Transitioning to Main Menu.");
        if (GameManager.Instance != null)
        {
            GameManager.Instance.ChangeState(GameManager.GameState.MainMenu);
        }
    }
}
```

### 1.2 Art Assets Draft List

- `vid_intro_placeholder`: 15-second draft storyboard video featuring rotating character silhouettes and the Pinder logo (replaces blank VideoPlayer source).
- `tex_splash_logo`: Black-and-white stylized sketch of the Pinder title logo (replaces default raw text in loading screens).
- `tex_loading_spinner`: Circular hand-drawn dotted outline animation representing a loading cycle (replaces standard Unity progress ring).

### 1.3 Integration/Wiring

- Connects from: Operating System/Executable launch (startup entry point scene: `BootScene`).
- Connects to: `GameState.MainMenu` via `GameManager.Instance.ChangeState`.
- Endpoints: `POST /api/v1/status` (handshake containing `apiVersion` request contract).

---

## 2. Main Menu & progression/wardrobe shop (PIND-12 / Epic PNDR-13 & PIND-02 / Epic PNDR-3)

### 2.1 White Box Version

The Main Menu holds the wardrobe shop, which uses XP earned from date sessions to unlock customizable items (accessories/outfits/hair/arms) from `PinderCoreLoader.ItemRepository`.

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Pinder.Core.Characters;
using PinderBridge;

public class WardrobeShopController : MonoBehaviour
{
    [Header("UI Labels")]
    [SerializeField] private TMPro.TextMeshProUGUI xpBalanceText;
    [SerializeField] private Transform itemContainer;
    [SerializeField] private GameObject shopItemButtonPrefab;

    private PinderCoreLoader _loader;
    private int _playerXpBalance;
    private List<string> _unlockedItemIds = new List<string>();

    private void Start()
    {
        _loader = FindObjectOfType<PinderCoreLoader>();
        LoadLocalProgression();
        PopulateShopUI();
    }

    private void LoadLocalProgression()
    {
        // Load player's earned currency/XP balance
        _playerXpBalance = PlayerPrefs.GetInt("pinder.xp_balance", 0);
        xpBalanceText.text = $"{_playerXpBalance} XP";

        // Load unlocked items (comma-separated IDs)
        string unlockedCsv = PlayerPrefs.GetString("pinder.unlocked_items", "starter_collar,starter_ribbon");
        _unlockedItemIds = new List<string>(unlockedCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private void SaveLocalProgression()
    {
        PlayerPrefs.SetInt("pinder.xp_balance", _playerXpBalance);
        PlayerPrefs.SetString("pinder.unlocked_items", string.Join(",", _unlockedItemIds));
        PlayerPrefs.Save();
        xpBalanceText.text = $"{_playerXpBalance} XP";
    }

    private void PopulateShopUI()
    {
        if (_loader == null || !_loader.IsLoaded) return;

        // Clear existing buttons
        foreach (Transform child in itemContainer)
        {
            Destroy(child.gameObject);
        }

        // List all items registered in pinder-core and create buttons
        foreach (var item in _loader.ItemRepository.GetAll())
        {
            GameObject btnObj = Instantiate(shopItemButtonPrefab, itemContainer);
            var text = btnObj.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            
            bool isUnlocked = _unlockedItemIds.Contains(item.Id);
            int xpCost = DetermineItemXpCost(item.Id);

            text.text = $"{item.Name}\n{(isUnlocked ? "UNLOCKED" : $"{xpCost} XP")}";

            var button = btnObj.GetComponent<UnityEngine.UI.Button>();
            button.onClick.AddListener(() => OnItemClicked(item.Id, xpCost));
        }
    }

    private int DetermineItemXpCost(string itemId)
    {
        // Mock cost logic (can be synced/loaded from server configs)
        if (itemId.StartsWith("starter")) return 0;
        if (itemId.Contains("gold") || itemId.Contains("legendary")) return 500;
        return 150;
    }

    private void OnItemClicked(string itemId, int cost)
    {
        if (_unlockedItemIds.Contains(itemId))
        {
            EquipItem(itemId);
        }
        else if (_playerXpBalance >= cost)
        {
            _playerXpBalance -= cost;
            _unlockedItemIds.Add(itemId);
            SaveLocalProgression();
            PopulateShopUI();
            Debug.Log($"[WardrobeShop] Unlocked item: {itemId}");
        }
        else
        {
            Debug.LogWarning("[WardrobeShop] Insufficient XP!");
        }
    }

    private void EquipItem(string itemId)
    {
        if (GameManager.Instance == null) return;

        CharacterData data = GameManager.Instance.CurrentCharacterData ?? new CharacterData();
        
        // Handle specialized categories or simple multi-accessory list
        if (itemId.Contains("outfit"))
        {
            data.equippedOutfitId = itemId;
        }
        else if (itemId.Contains("hair"))
        {
            data.equippedHairId = itemId;
        }
        else
        {
            if (!data.equippedAccessoryIds.Contains(itemId))
            {
                data.equippedAccessoryIds.Add(itemId);
            }
            else
            {
                data.equippedAccessoryIds.Remove(itemId); // Toggle behavior
            }
        }

        GameManager.Instance.CurrentCharacterData = data;
        Debug.Log($"[WardrobeShop] Equipped item state updated for: {itemId}");
    }
}
```

### 2.2 Art Assets Draft List

- `tex_shop_icon_placeholder`: Stylized gray outlines representing individual physical accessories (hats, piercings, collars) for the store layout.
- `tex_menu_background`: Rough sketch of a character dressing lounge or runway showroom (replaces the solid color backdrop in standard menus).
- `tex_xp_symbol`: A draft graphic showing a stylized yellow star currency silhouette (replaces the raw "XP" character label).

### 2.3 Integration/Wiring

- Connects from: `BootScene` (on successful launch) or `ResultsScene` (when clicking "Return to Menu").
- Connects to: `CreationBench` (to customize proportions), `OpponentsLobby` (to select an opponent), or `PhotoBooth` (to snap a portrait).
- Endpoints: Optional inventory storage sync to `POST /api/v1/profile/inventory` carrying the serializable unlocked list.

---

## 3. Photo Booth, Screenshot Export & Upload (PIND-04 / Epic PNDR-5)

### 3.1 White Box Version

Captures a high-resolution portrait of the customized character in the scene, saves the resulting PNG locally to persistent storage, and uploads it to the backend media gallery.

```csharp
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class PhotoExportAndUploadController : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Camera screenshotCamera;
    [SerializeField] private RawImage previewImage;
    [SerializeField] private GameObject uploadSuccessIndicator;

    private const string UploadEndpoint = "https://api.pinder.games/api/v1/media/upload";
    private Texture2D _capturedTexture;

    public void TriggerCaptureAndUpload()
    {
        StartCoroutine(CaptureSequenceCoroutine());
    }

    private IEnumerator CaptureSequenceCoroutine()
    {
        yield return new WaitForEndOfFrame();

        const int width = 512;
        const int height = 640;

        // Render scene to a temporary texture
        var rt = new RenderTexture(width, height, 24);
        screenshotCamera.targetTexture = rt;
        screenshotCamera.Render();
        RenderTexture.active = rt;

        if (_capturedTexture != null) Destroy(_capturedTexture);
        _capturedTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        _capturedTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        _capturedTexture.Apply();

        // Clear render target states
        screenshotCamera.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);

        // Display on-screen preview
        previewImage.texture = _capturedTexture;

        // Step 1: Save local backup copy (screenshot export)
        byte[] pngBytes = _capturedTexture.EncodeToPNG();
        string localFilename = $"pinder_avatar_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
        string localPath = Path.Combine(Application.persistentDataPath, localFilename);
        File.WriteAllBytes(localPath, pngBytes);
        Debug.Log($"[PhotoBooth] Exported avatar locally to: {localPath}");

        // Step 2: Upload to backend media storage
        yield return StartCoroutine(UploadAvatarImageCoroutine(pngBytes, localFilename));
    }

    private IEnumerator UploadAvatarImageCoroutine(byte[] pngBytes, string filename)
    {
        var form = new WWWForm();
        form.AddBinaryData("file", pngBytes, filename, "image/png");

        using var request = UnityWebRequest.Post(UploadEndpoint, form);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[PhotoBooth] Upload failed: {request.error}");
            yield break;
        }

        string serverResponse = request.downloadHandler.text;
        Debug.Log($"[PhotoBooth] Uploaded successfully: {serverResponse}");

        // Cache the live photo reference in GameManager
        if (GameManager.Instance != null)
        {
            GameManager.Instance.CharacterPhoto = _capturedTexture;
            _capturedTexture = null; // Transfer ownership to prevent garbage collection
        }

        if (uploadSuccessIndicator != null)
        {
            uploadSuccessIndicator.SetActive(true);
        }
    }

    private void OnDestroy()
    {
        if (_capturedTexture != null) Destroy(_capturedTexture);
    }
}
```

### 3.2 Art Assets Draft List

- `tex_camera_overlay`: A draft frame showing camera focus lines, battery indicator silhouette, and recording dot sketch (replaces raw layout grids).
- `tex_frame_polaroid`: High-contrast card frame outline with a slight drop shadow (replaces default solid white raw preview box).
- `tex_upload_success_checkmark`: Stylized sketchy green tick animation celebrating successful uploads (replaces default plain button label).

### 3.3 Integration/Wiring

- Connects from: `CreationBenchScene` (after finalizing custom sliders and wardrobe accessories).
- Connects to: `OpponentsLobbyScene` (transition to matching interface, showing the user's fresh profile photo).
- Endpoints: `POST /api/v1/media/upload` (accepts multipart/form-data files, returning serialized image URLs).

---

## 4. Server Selection Lobby / Matchmaker (PIND-05 / Epic PNDR-6)

### 4.1 White Box Version

Queries the FastAPI backend lobby endpoint to get a live list of opponent datees, displays their customized profile cards, and manages partner selection prior to starting PinderChat.

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using PinderBridge;

public class MatchmakerLobbyController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Transform cardContainer;
    [SerializeField] private GameObject opponentCardPrefab;
    [SerializeField] private GameObject loadingIndicator;

    private const string LobbyEndpoint = "https://api.pinder.games/api/v1/lobby/opponents";

    [System.Serializable]
    private class OpponentProfileDto
    {
        public string id;
        public string name;
        public string bio;
        public int difficulty;
        public string portraitUrl;
    }

    [System.Serializable]
    private class LobbyOpponentsResponse
    {
        public OpponentProfileDto[] opponents;
    }

    private void Start()
    {
        loadingIndicator.SetActive(true);
        StartCoroutine(FetchOpponentsCoroutine());
    }

    private IEnumerator FetchOpponentsCoroutine()
    {
        using var request = UnityWebRequest.Get(LobbyEndpoint);
        yield return request.SendWebRequest();

        loadingIndicator.SetActive(false);

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[Matchmaker] Failed to query lobby. Falling back to local profiles. Error: {request.error}");
            LoadLocalFallbackProfiles();
            yield break;
        }

        try
        {
            var response = JsonUtility.FromJson<LobbyOpponentsResponse>(request.downloadHandler.text);
            PopulateOpponentsUI(response.opponents);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Matchmaker] Failed to parse lobby response: {ex.Message}");
            LoadLocalFallbackProfiles();
        }
    }

    private void PopulateOpponentsUI(OpponentProfileDto[] opponents)
    {
        foreach (Transform child in cardContainer) Destroy(child.gameObject);

        foreach (var opp in opponents)
        {
            GameObject cardObj = Instantiate(opponentCardPrefab, cardContainer);
            
            // Wire text labels
            var labels = cardObj.GetComponentsInChildren<TMPro.TextMeshProUGUI>();
            if (labels.Length >= 2)
            {
                labels[0].text = opp.name;
                labels[1].text = $"{opp.bio}\nDifficulty: {opp.difficulty}/5";
            }

            // Bind selection button action
            var button = cardObj.GetComponentInChildren<UnityEngine.UI.Button>();
            if (button != null)
            {
                button.onClick.AddListener(() => OnOpponentSelected(opp.id));
            }
        }
    }

    private void LoadLocalFallbackProfiles()
    {
        // Fallback: search streaming assets directly
        var fallbackList = new List<OpponentProfileDto>
        {
            new OpponentProfileDto { id = "brick", name = "Brick", bio = "Hard-headed and stoic.", difficulty = 2 },
            new OpponentProfileDto { id = "whisky", name = "Whisky", bio = "Spicy, refined, and aromatic.", difficulty = 4 }
        };
        PopulateOpponentsUI(fallbackList.ToArray());
    }

    private void OnOpponentSelected(string opponentId)
    {
        Debug.Log($"[Matchmaker] Opponent selected: {opponentId}");
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SelectedOpponentId = opponentId;
            GameManager.Instance.ChangeState(GameManager.GameState.PinderChat);
        }
    }
}
```

### 4.2 Art Assets Draft List

- `tex_lobby_bg`: Detailed grayscale blueprint sketch representing an interactive phone UI showing background swiping (replaces raw empty solid canvas).
- `tex_opponent_card_silhouette`: Generic dark grey head-and-shoulder avatar contour (replaces missing datee icons on load failures).
- `tex_match_stamp`: A hand-stamped vector silhouette reading "MATCH CONFIRMED!" (replaces default standard system button highlight states).

### 4.3 Integration/Wiring

- Connects from: `PhotoBoothScene` (upon completing profile capture) or `MainMenuScene` (direct menu lobby access).
- Connects to: `PinderChatScene` via `GameManager.Instance.ChangeState` after securing a match and populating `SelectedOpponentId`.
- Endpoints: `GET /api/v1/lobby/opponents` (queries candidate roster).

---

## 5. Session End, XP Handback & Menu return (PIND-12 / Epic PNDR-13)

### 5.1 White Box Version

Executed inside the Results scene when a date completes. Gathers the final game session statistics from the snapshot, calculates total XP rewards, commits stats to the server, and returns the player to the Main Menu.

```csharp
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using Pinder.Core.Conversation;
using PinderBridge;

public class SessionEndController : MonoBehaviour
{
    [Header("UI Display References")]
    [SerializeField] private TMPro.TextMeshProUGUI outcomeHeadlineText;
    [SerializeField] private TMPro.TextMeshProUGUI breakdownStatsText;
    [SerializeField] private TMPro.TextMeshProUGUI earnedXpText;

    private const string EndSessionEndpoint = "https://api.pinder.games/api/v1/session/end";

    [System.Serializable]
    private class SessionMetricsDto
    {
        public string dateeId;
        public string finalOutcome;
        public int finalInterest;
        public int totalTurns;
        public int earnedXp;
    }

    public void InitializeResults(GameSession finishedSession)
    {
        if (finishedSession == null)
        {
            Debug.LogError("[Results] No active session available to parse.");
            return;
        }

        var snapshot = finishedSession.CreateSnapshot();
        bool isVictory = snapshot.Interest > 15; // Mock win condition check

        // Step 1: Calculate Progression Rewards (XP Math)
        int baseTurnBonus = snapshot.TurnNumber * 10;
        int interestMultiplier = Mathf.Max(0, snapshot.Interest) * 15;
        int outcomeBonus = isVictory ? 250 : 50;
        int totalEarnedXp = baseTurnBonus + interestMultiplier + outcomeBonus;

        outcomeHeadlineText.text = isVictory ? "OUTSTANDING MATCH!" : "REJECTED!";
        breakdownStatsText.text = $"Interest reached: {snapshot.Interest}\nTurns taken: {snapshot.TurnNumber}";
        earnedXpText.text = $"+{totalEarnedXp} XP";

        // Update persistent PlayerPrefs currency
        int currentXpBalance = PlayerPrefs.GetInt("pinder.xp_balance", 0);
        PlayerPrefs.SetInt("pinder.xp_balance", currentXpBalance + totalEarnedXp);
        PlayerPrefs.Save();

        // Step 2: Post session outcome metrics to backend server
        var metrics = new SessionMetricsDto
        {
            dateeId = GameManager.Instance?.SelectedOpponentId ?? "unknown",
            finalOutcome = isVictory ? "VICTORY" : "DEFEAT",
            finalInterest = snapshot.Interest,
            totalTurns = snapshot.TurnNumber,
            earnedXp = totalEarnedXp
        };

        StartCoroutine(SyncOutcomeWithServerCoroutine(metrics));
    }

    private IEnumerator SyncOutcomeWithServerCoroutine(SessionMetricsDto metrics)
    {
        string payload = JsonUtility.ToJson(metrics);
        using var request = new UnityWebRequest(EndSessionEndpoint, "POST");
        byte[] rawBody = System.Text.Encoding.UTF8.GetBytes(payload);
        request.uploadHandler = new UploadHandlerRaw(rawBody);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[Results] Server progression sync failed. Fallback to offline local: {request.error}");
        }
        else
        {
            Debug.Log("[Results] Server progression sync completed.");
        }
    }

    public void OnContinueToMainMenuClicked()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.ResetGame(); // Returns to Main Menu resetting active configs
        }
    }
}
```

### 5.2 Art Assets Draft List

- `tex_victory_banner`: Stylized high-contrast celebratory graphic sketch with laurel leaves (replaces generic green success label).
- `tex_defeat_banner`: Melancholic sketch showing a cracked heart silhouette (replaces basic red defeat text).
- `tex_xp_fill_bar`: Sketchy horizontal thermometer-style empty progression tube (replaces standard Unity progress slider fills).

### 5.3 Integration/Wiring

- Connects from: `PinderChatScene` (triggered automatically when `result.IsGameOver` becomes true inside turn loop).
- Connects to: `MainMenuScene` upon clicking continue/return button on results overlay.
- Endpoints: `POST /api/v1/session/end` (submits match performance metrics).

---

## 6. Web Replay/Share Upload (PIND-13 / Epic PNDR-14)

### 6.1 White Box Version

Packages the entire compiled sequence of player interactions and LLM responses into a serializable JSON conversation replay payload, uploads it to the backend, and retrieves a shareable web replay URL.

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Pinder.Core.Conversation;

public class ReplayShareController : MonoBehaviour
{
    [Header("UI Feedback Labels")]
    [SerializeField] private TMPro.TextMeshProUGUI shareLinkText;
    [SerializeField] private GameObject uploadingOverlay;

    private const string ShareEndpoint = "https://api.pinder.games/api/v1/replay/share";

    [System.Serializable]
    private class TurnLogEntry
    {
        public int turnNumber;
        public string selectedOptionText;
        public string rolledStat;
        public int dieRoll;
        public bool isSuccess;
        public string deliveredMessage;
        public string dateeResponse;
        public int interestAfter;
    }

    [System.Serializable]
    private class ReplayPayload
    {
        public string playerAvatarName;
        public string dateeName;
        public List<TurnLogEntry> turnHistory;
    }

    [System.Serializable]
    private class ShareResponse
    {
        public string shareCode;
        public string replayUrl;
    }

    private List<TurnLogEntry> _turnLogs = new List<TurnLogEntry>();

    // Call from PinderChatController after each ResolveTurnAsync completes
    public void RecordTurn(int turnNumber, TurnStart options, TurnResult result)
    {
        var activeOption = options.Options[result.StateAfter.LastOptionPickedIndex];
        
        var log = new TurnLogEntry
        {
            turnNumber = turnNumber,
            selectedOptionText = activeOption.IntendedText,
            rolledStat = activeOption.Stat.ToString(),
            dieRoll = result.Roll.UsedDieRoll,
            isSuccess = result.Roll.IsSuccess,
            deliveredMessage = result.DeliveredMessage,
            dateeResponse = result.DateeMessage,
            interestAfter = result.StateAfter.Interest
        };

        _turnLogs.Add(log);
        Debug.Log($"[ReplayShare] Recorded turn {turnNumber} for share payload.");
    }

    public void TriggerUploadReplay()
    {
        if (_turnLogs.Count == 0)
        {
            Debug.LogWarning("[ReplayShare] No turn logs recorded! Unable to upload.");
            return;
        }

        uploadingOverlay.SetActive(true);
        StartCoroutine(UploadReplayCoroutine());
    }

    private IEnumerator UploadReplayCoroutine()
    {
        var payload = new ReplayPayload
        {
            playerAvatarName = "Paulie", // Can map from active CharacterData
            dateeName = "Brick",
            turnHistory = _turnLogs
        };

        string jsonPayload = JsonUtility.ToJson(payload);
        using var request = new UnityWebRequest(ShareEndpoint, "POST");
        byte[] rawBody = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
        request.uploadHandler = new UploadHandlerRaw(rawBody);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        uploadingOverlay.SetActive(false);

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[ReplayShare] Failed to upload dialogue logs: {request.error}");
            shareLinkText.text = "Share Failed. Try again.";
            yield break;
        }

        try
        {
            var response = JsonUtility.FromJson<ShareResponse>(request.downloadHandler.text);
            shareLinkText.text = response.replayUrl;
            
            // Auto copy URL to player clipboard
            GUIUtility.systemCopyBuffer = response.replayUrl;
            Debug.Log($"[ReplayShare] Uploaded! URL copied to clipboard: {response.replayUrl}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ReplayShare] Error decoding share details: {ex.Message}");
        }
    }
}
```

### 6.2 Art Assets Draft List

- `tex_share_icon`: Sketchy hand-drawn icon representing network nodes or sharing hands (replaces blank generic menu slots).
- `tex_qrcode_placeholder`: A gray pixelated checkerboard square representing generating share links (replaces standard UI rects).
- `tex_copy_clipboard_success`: Soft stylized text contour reading "COPIED TO CLIPBOARD" (replaces plain unity logger strings).

### 6.3 Integration/Wiring

- Connects from: `ResultsScene` (as an optional overlay/button before finishing the match review).
- Connects to: Stays within Results scene overlay until completion, then routes to `MainMenuScene` on close.
- Endpoints: `POST /api/v1/replay/share` (delivers conversation history and stores database entries yielding web view URLs).
