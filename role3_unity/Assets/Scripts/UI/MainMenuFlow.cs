
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public class MainMenuFlow : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField]
    private GameObject homePanel;

    [SerializeField]
    private GameObject shapeSelectionPanel;

    [SerializeField]
    private GameObject instructionPanel;

    [Header("Instruction")]
    [SerializeField]
    private TMP_Text instructionShapeTitle;

    [SerializeField]
    private TMP_Text instructionText;

    // ==========================================
    // PC BACKEND AUTO START
    // ==========================================

    private const int BackendPort = 5053;

    private static System.Diagnostics.Process
        backendProcess;

    private static bool backendStartedByApp;

    private static bool backendExitReported;

    private static int lastConvertedFrame = -1;

    // ==========================================
    // INITIALIZATION
    // ==========================================

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;

        Canvas.preWillRenderCanvases -=
            ConvertRuntimeText;

        Canvas.preWillRenderCanvases +=
            ConvertRuntimeText;

        Application.quitting -= StopOwnedBackend;
        Application.quitting += StopOwnedBackend;

#if UNITY_EDITOR

        EditorApplication.playModeStateChanged -=
            OnEditorPlayModeChanged;

        EditorApplication.playModeStateChanged +=
            OnEditorPlayModeChanged;

#endif

        lastConvertedFrame = -1;
    }

    private void Start()
    {
        // Automatically start the complete PC backend.
        // No BAT file is required during normal use.
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

        EnsureBackendRunning();

#endif

#if UNITY_ANDROID && !UNITY_EDITOR

        ConfigureSpatialMenu();

#else

        RestoreHomeIllustration();

#endif

        ConvertAllText();

        if (GameSession.ReturnToShapeSelection)
        {
            GameSession.ReturnToShapeSelection = false;
            ShowShapeSelection();
        }
        else
        {
            ShowHome();
        }
    }

    private void Update()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

        // Report an unexpected Python startup failure.
        if (
            backendProcess != null &&
            !backendExitReported
        )
        {
            try
            {
                if (backendProcess.HasExited)
                {
                    backendExitReported = true;

                    Debug.LogError(
                        "[SmartGeoAR] Python backend " +
                        "stopped unexpectedly. Exit code: " +
                        backendProcess.ExitCode
                    );
                }
            }
            catch (Exception)
            {
            }
        }

#endif
    }

    // ==========================================
    // FIND PROJECT ROOT
    // ==========================================

    private static string FindProjectRoot()
    {
        DirectoryInfo directory =
            new DirectoryInfo(
                Application.dataPath
            );

        for (
            int i = 0;
            i < 12 && directory != null;
            i++
        )
        {
            string script = Path.Combine(
                directory.FullName,
                "smartgeoar_backend.py"
            );

            string handFolder = Path.Combine(
                directory.FullName,
                "role1_hand_tracking"
            );

            string reasoningFolder = Path.Combine(
                directory.FullName,
                "role2_geometry_reasoning"
            );

            if (
                File.Exists(script) &&
                Directory.Exists(handFolder) &&
                Directory.Exists(reasoningFolder)
            )
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    // ==========================================
    // CHECK BACKEND PORT
    // ==========================================

    private static bool IsBackendPortInUse()
    {
        TcpListener listener = null;

        try
        {
            listener = new TcpListener(
                IPAddress.Loopback,
                BackendPort
            );

            listener.Start();

            return false;
        }
        catch (SocketException)
        {
            return true;
        }
        finally
        {
            if (listener != null)
            {
                try
                {
                    listener.Stop();
                }
                catch (Exception)
                {
                }
            }
        }
    }

    // ==========================================
    // START PYTHON AUTOMATICALLY
    // ==========================================

    private static void EnsureBackendRunning()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

        // Keep the existing process if it is alive.
        if (backendProcess != null)
        {
            try
            {
                if (!backendProcess.HasExited)
                    return;
            }
            catch (Exception)
            {
            }

            try
            {
                backendProcess.Dispose();
            }
            catch (Exception)
            {
            }

            backendProcess = null;
            backendStartedByApp = false;
        }

        // An existing backend may already be running.
        // Never open a second copy on the same port.
        if (IsBackendPortInUse())
        {
            Debug.Log(
                "[SmartGeoAR] Port 5053 is in use. " +
                "Connecting to the existing backend."
            );

            return;
        }

        string root = FindProjectRoot();

        if (string.IsNullOrEmpty(root))
        {
            Debug.LogError(
                "[SmartGeoAR] Project backend not found. " +
                "Keep the Windows build inside the " +
                "SmartGeoAR_PC_BanGoc project folder."
            );

            return;
        }

        string python = null;

        // Prefer the environment created by
        // Setup_SmartGeoAR_PC.bat.
        string[] candidates =
        {
            Path.Combine(
                root,
                ".venv-cv",
                "Scripts",
                "pythonw.exe"
            ),

            Path.Combine(
                root,
                ".venv-cv",
                "Scripts",
                "python.exe"
            ),

            Path.Combine(
                root,
                ".venv",
                "Scripts",
                "pythonw.exe"
            ),

            Path.Combine(
                root,
                ".venv",
                "Scripts",
                "python.exe"
            )
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                python = candidate;
                break;
            }
        }

        if (string.IsNullOrEmpty(python))
        {
            Debug.LogError(
                "[SmartGeoAR] Python environment missing. " +
                "Run Setup_SmartGeoAR_PC.bat once " +
                "to install the required libraries."
            );

            return;
        }

        string script = Path.Combine(
            root,
            "smartgeoar_backend.py"
        );

        if (!File.Exists(script))
        {
            Debug.LogError(
                "[SmartGeoAR] Backend script missing: " +
                script
            );

            return;
        }

        try
        {
            var startInfo =
                new System.Diagnostics.ProcessStartInfo();

            startInfo.FileName = python;

            startInfo.Arguments =
                "-u \"" + script + "\"" +
                " --host 127.0.0.1" +
                " --port 5053" +
                " --camera-source webcam";

            startInfo.WorkingDirectory = root;

            // Hide the Python console.
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;

            startInfo.WindowStyle =
                System.Diagnostics.ProcessWindowStyle.Hidden;

            backendProcess =
                System.Diagnostics.Process.Start(
                    startInfo
                );

            if (backendProcess == null)
            {
                Debug.LogError(
                    "[SmartGeoAR] Failed to start Python."
                );

                return;
            }

            backendStartedByApp = true;
            backendExitReported = false;

            Debug.Log(
                "[SmartGeoAR] PC backend started " +
                "automatically. PID=" +
                backendProcess.Id
            );

            Debug.Log(
                "[SmartGeoAR] Webcam + MediaPipe + " +
                "Geometry backend starting on " +
                "127.0.0.1:5053"
            );
        }
        catch (Exception error)
        {
            Debug.LogError(
                "[SmartGeoAR] Backend auto-start failed: " +
                error.Message
            );
        }

#endif
    }

    // ==========================================
    // STOP PYTHON WHEN UNITY CLOSES
    // ==========================================

    private static void StopOwnedBackend()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

        // Never kill a backend that was launched
        // independently by another application.
        if (!backendStartedByApp)
            return;

        backendStartedByApp = false;

        if (backendProcess == null)
            return;

        try
        {
            if (!backendProcess.HasExited)
            {
                backendProcess.Kill();
                backendProcess.WaitForExit(1000);
            }
        }
        catch (Exception)
        {
        }

        try
        {
            backendProcess.Dispose();
        }
        catch (Exception)
        {
        }

        backendProcess = null;

#endif
    }

#if UNITY_EDITOR

    private static void OnEditorPlayModeChanged(
        PlayModeStateChange state
    )
    {
        if (
            state ==
            PlayModeStateChange.ExitingPlayMode
        )
        {
            StopOwnedBackend();
        }
    }

#endif

    // ==========================================
    // REMOVE VIETNAMESE ACCENTS
    // ==========================================

    private static void OnSceneLoaded(
        Scene scene,
        LoadSceneMode mode
    )
    {
        lastConvertedFrame = -1;
        ConvertAllText();
    }

    private static void ConvertRuntimeText()
    {
        if (
            lastConvertedFrame ==
            Time.frameCount
        )
        {
            return;
        }

        lastConvertedFrame =
            Time.frameCount;

        ConvertAllText();
    }

    private static void ConvertAllText()
    {
        TMP_Text[] tmpTexts =
            UnityEngine.Object.FindObjectsByType<TMP_Text>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        foreach (TMP_Text text in tmpTexts)
        {
            if (text == null)
                continue;

            string converted =
                ToAscii(text.text);

            if (converted != text.text)
            {
                text.text = converted;
            }
        }

        Text[] oldTexts =
            UnityEngine.Object.FindObjectsByType<Text>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        foreach (Text text in oldTexts)
        {
            if (text == null)
                continue;

            string converted =
                ToAscii(text.text);

            if (converted != text.text)
            {
                text.text = converted;
            }
        }
    }

    private static string ToAscii(
        string input
    )
    {
        if (string.IsNullOrEmpty(input))
            return input;

        string normalized = input.Normalize(
            NormalizationForm.FormD
        );

        StringBuilder output =
            new StringBuilder(
                normalized.Length
            );

        foreach (char character in normalized)
        {
            if (character == '\u0111')
            {
                output.Append('d');
                continue;
            }

            if (character == '\u0110')
            {
                output.Append('D');
                continue;
            }

            if (character == '\u2022')
            {
                output.Append('-');
                continue;
            }

            if (character == '\u2192')
            {
                output.Append("->");
                continue;
            }

            if (character == '\u2190')
            {
                output.Append("<-");
                continue;
            }

            if (
                character == '\u2013' ||
                character == '\u2014'
            )
            {
                output.Append('-');
                continue;
            }

            UnicodeCategory category =
                CharUnicodeInfo.GetUnicodeCategory(
                    character
                );

            if (
                category ==
                    UnicodeCategory.NonSpacingMark ||
                category ==
                    UnicodeCategory.SpacingCombiningMark ||
                category ==
                    UnicodeCategory.EnclosingMark
            )
            {
                continue;
            }

            if (
                character >= 32 &&
                character <= 126
            )
            {
                output.Append(character);
            }
            else if (
                character == '\n' ||
                character == '\r' ||
                character == '\t'
            )
            {
                output.Append(character);
            }
        }

        return output.ToString();
    }

    // ==========================================
    // PERMANENT EDITOR TEXT FIX
    // ==========================================

#if UNITY_EDITOR

    [MenuItem(
        "Tools/SmartGeoAR/Bo dau UI trong tat ca Scene"
    )]
    private static void ConvertSavedScenes()
    {
        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            EditorUtility.DisplayDialog(
                "SmartGeoAR",
                "Hay tat Play Mode truoc.",
                "OK"
            );

            return;
        }

        if (
            !EditorSceneManager
                .SaveCurrentModifiedScenesIfUserWantsTo()
        )
        {
            return;
        }

        string originalScene =
            SceneManager.GetActiveScene().path;

        string[] scenePaths =
        {
            "Assets/Scenes/MainMenu.unity",
            "Assets/Scenes/ARMain.unity"
        };

        int totalChanged = 0;

        foreach (string path in scenePaths)
        {
            SceneAsset asset =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    path
                );

            if (asset == null)
                continue;

            Scene scene =
                EditorSceneManager.OpenScene(
                    path,
                    UnityEditor.SceneManagement
                        .OpenSceneMode.Single
                );

            int changed = 0;

            foreach (
                GameObject root in
                scene.GetRootGameObjects()
            )
            {
                foreach (
                    TMP_Text text in
                    root.GetComponentsInChildren<TMP_Text>(
                        true
                    )
                )
                {
                    string value =
                        ToAscii(text.text);

                    if (value == text.text)
                        continue;

                    text.text = value;

                    EditorUtility.SetDirty(text);

                    changed++;
                }

                foreach (
                    Text text in
                    root.GetComponentsInChildren<Text>(
                        true
                    )
                )
                {
                    string value =
                        ToAscii(text.text);

                    if (value == text.text)
                        continue;

                    text.text = value;

                    EditorUtility.SetDirty(text);

                    changed++;
                }
            }

            if (changed > 0)
            {
                EditorSceneManager.MarkSceneDirty(
                    scene
                );

                EditorSceneManager.SaveScene(
                    scene
                );
            }

            totalChanged += changed;
        }

        if (!string.IsNullOrEmpty(originalScene))
        {
            EditorSceneManager.OpenScene(
                originalScene,
                UnityEditor.SceneManagement
                    .OpenSceneMode.Single
            );
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "SmartGeoAR",
            "Da bo dau UI.\n" +
            "Text objects changed: " +
            totalChanged,
            "OK"
        );
    }

#endif

    // ==========================================
    // MENU IMAGE
    // ==========================================

    private void RestoreHomeIllustration()
    {
        if (homePanel == null)
            return;

        Transform background =
            homePanel.transform.Find(
                "Background"
            );

        if (background == null)
            return;

        Image illustration =
            background.GetComponent<Image>();

        if (illustration == null)
            return;

#if UNITY_EDITOR

        Sprite sprite =
            AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Scripts/UI/Holographic AR Geometry Cube.png"
            );

        if (sprite != null)
        {
            illustration.sprite = sprite;
        }

#endif

        illustration.enabled = true;
        illustration.color = Color.white;
        illustration.type = Image.Type.Simple;
        illustration.preserveAspect = true;
        illustration.raycastTarget = false;
    }

    // ==========================================
    // PHONE MENU COMPATIBILITY
    // ==========================================

    private void ConfigureSpatialMenu()
    {
        Camera camera = Camera.main;

        if (
            camera != null &&
            !camera.stereoEnabled &&
            camera.GetComponent<
                UnityEngine.XR.ARFoundation.ARCameraManager
            >() == null
        )
        {
            camera.fieldOfView = 32f;

            camera.clearFlags =
                CameraClearFlags.SolidColor;

            camera.backgroundColor =
                new Color(
                    0.025f,
                    0.05f,
                    0.09f
                );
        }

        Canvas canvas =
            homePanel
                .GetComponentInParent<Canvas>()
                .rootCanvas;

        SpatialWorkspace.Ensure()
            .ConfigureCanvas(canvas);

        foreach (GameObject panel in new[]
        {
            homePanel,
            shapeSelectionPanel,
            instructionPanel
        })
        {
            RectTransform rect =
                panel.transform as RectTransform;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;

            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image background =
                panel.GetComponent<Image>();

            if (background != null)
            {
                background.color =
                    new Color(
                        0.03f,
                        0.10f,
                        0.17f,
                        0.15f
                    );
            }

            foreach (
                Image image in
                panel.GetComponentsInChildren<Image>(true)
            )
            {
                if (
                    image.GetComponentInParent<Button>(
                        true
                    ) == null
                )
                {
                    image.raycastTarget = false;
                }
            }
        }

        foreach (
            Image image in
            homePanel.GetComponentsInChildren<Image>(
                true
            )
        )
        {
            if (
                image.transform !=
                    homePanel.transform &&
                image.GetComponentInParent<Button>(
                    true
                ) == null
            )
            {
                image.enabled = false;
            }
        }

        LayoutPanel(homePanel);
        LayoutPanel(instructionPanel);

        TMP_Text title =
            shapeSelectionPanel.transform
                .Find("ShapeTitleText")
                ?.GetComponent<TMP_Text>();

        LayoutText(
            title,
            new Vector2(0f, 310f),
            new Vector2(1200f, 90f),
            48f
        );

        LayoutText(
            instructionShapeTitle,
            new Vector2(0f, 300f),
            new Vector2(1200f, 90f),
            46f
        );

        LayoutText(
            instructionText,
            new Vector2(0f, 10f),
            new Vector2(1200f, 360f),
            32f
        );

        foreach (
            Button button in
            shapeSelectionPanel
                .GetComponentsInChildren<Button>(true)
        )
        {
            if (
                button.name == "CubeButton" ||
                button.name == "RectangularPrismButton" ||
                button.name == "SquarePyramidButton" ||
                button.name == "ShapeDropButton"
            )
            {
                continue;
            }

            LayoutRect(
                button.transform as RectTransform,
                new Vector2(-560f, -390f),
                new Vector2(330f, 76f)
            );
        }
    }

    // ==========================================
    // PANEL LAYOUT
    // ==========================================

    private static void LayoutPanel(
        GameObject panel
    )
    {
        if (panel == null)
            return;

        int row = 0;

        foreach (
            TMP_Text text in
            panel.GetComponentsInChildren<TMP_Text>(
                true
            )
        )
        {
            if (
                text.GetComponentInParent<Button>(
                    true
                ) == null
            )
            {
                text.transform.SetParent(
                    panel.transform,
                    false
                );

                LayoutText(
                    text,
                    new Vector2(
                        0f,
                        220f - row * 120f
                    ),
                    new Vector2(1250f, 120f),
                    row == 0 ? 50f : 30f
                );

                row++;
            }
        }

        Button[] buttons =
            panel.GetComponentsInChildren<Button>(
                true
            );

        for (
            int i = 0;
            i < buttons.Length;
            i++
        )
        {
            buttons[i].transform.SetParent(
                panel.transform,
                false
            );

            LayoutRect(
                buttons[i].transform as RectTransform,
                new Vector2(
                    (
                        i -
                        (buttons.Length - 1) * 0.5f
                    ) * 430f,
                    -280f
                ),
                new Vector2(390f, 100f)
            );

            TMP_Text text =
                buttons[i]
                    .GetComponentInChildren<TMP_Text>(
                        true
                    );

            if (text != null)
            {
                text.fontSize = 32f;
                text.raycastTarget = false;

                text.alignment =
                    TextAlignmentOptions.Center;

                text.transform.SetParent(
                    buttons[i].transform,
                    false
                );

                text.rectTransform.anchorMin =
                    Vector2.zero;

                text.rectTransform.anchorMax =
                    Vector2.one;

                text.rectTransform.offsetMin =
                    Vector2.zero;

                text.rectTransform.offsetMax =
                    Vector2.zero;
            }

            for (
                int eventIndex = 0;
                eventIndex <
                buttons[i].onClick
                    .GetPersistentEventCount();
                eventIndex++
            )
            {
                if (
                    text != null &&
                    buttons[i].onClick
                        .GetPersistentMethodName(
                            eventIndex
                        ) == "StartScan"
                )
                {
                    text.text = "BAT DAU";
                }
            }
        }
    }

    private static void LayoutText(
        TMP_Text text,
        Vector2 position,
        Vector2 size,
        float fontSize
    )
    {
        if (text == null)
            return;

        LayoutRect(
            text.rectTransform,
            position,
            size
        );

        text.fontSize = fontSize;
        text.alignment =
            TextAlignmentOptions.Center;

        text.raycastTarget = false;
    }

    private static void LayoutRect(
        RectTransform rect,
        Vector2 position,
        Vector2 size
    )
    {
        if (rect == null)
            return;

        rect.anchorMin =
            rect.anchorMax =
                new Vector2(0.5f, 0.5f);

        rect.pivot =
            new Vector2(0.5f, 0.5f);

        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    // ==========================================
    // MENU NAVIGATION
    // ==========================================

    public void ShowHome()
    {
        homePanel.SetActive(true);
        shapeSelectionPanel.SetActive(false);
        instructionPanel.SetActive(false);
    }

    public void ShowShapeSelection()
    {
        homePanel.SetActive(false);
        shapeSelectionPanel.SetActive(true);
        instructionPanel.SetActive(false);
    }

    public void SelectCube()
    {
        GameSession.SelectedShape =
            ShapeType.Cube;

        ShowInstruction(
            "HINH LAP PHUONG",
            "Khung lap phuong va khay canh " +
            "se xuat hien truoc mat.\n\n" +
            "Chum tay de gap canh, dua toi khung " +
            "roi mo tay de tha.\n\n" +
            "Nam khung de xoay. Nhan BAT DAU " +
            "khi san sang."
        );
    }

    public void SelectRectangularPrism()
    {
        GameSession.SelectedShape =
            ShapeType.RectangularPrism;

        ShowInstruction(
            "HINH HOP CHU NHAT",
            "Khung hop chu nhat va khay canh " +
            "se xuat hien truoc mat.\n\n" +
            "Chum tay de gap canh, dua toi khung " +
            "roi mo tay de tha.\n\n" +
            "Nam khung de xoay. Nhan BAT DAU " +
            "khi san sang."
        );
    }

    public void SelectSquarePyramid()
    {
        GameSession.SelectedShape =
            ShapeType.SquarePyramid;

        ShowInstruction(
            "HINH CHOP TU GIAC DEU",
            "Khung chop va khay canh " +
            "se xuat hien truoc mat.\n\n" +
            "Chum tay de gap canh day hoac " +
            "canh ben, dua toi khung roi mo tay.\n\n" +
            "Nam khung de xoay. Nhan BAT DAU " +
            "khi san sang."
        );
    }

    private void ShowInstruction(
        string title,
        string message
    )
    {
        homePanel.SetActive(false);
        shapeSelectionPanel.SetActive(false);
        instructionPanel.SetActive(true);

        instructionShapeTitle.text =
            ToAscii(title);

        instructionText.text =
            ToAscii(message);
    }

    public void BackToShapeSelection()
    {
        ShowShapeSelection();
    }

    public void StartScan()
    {
        SpatialWorkspace.LogInteraction(
            "Start shape: " +
            GameSession.SelectedShape
        );

        SceneManager.LoadScene("ARMain");
    }
}
