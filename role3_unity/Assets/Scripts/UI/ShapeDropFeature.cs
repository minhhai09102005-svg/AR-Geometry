
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

public class ShapeDropFeature : MonoBehaviour
{
    private enum Shape
    {
        Square,
        Triangle,
        Rectangle,
        Circle
    }

    private class Piece
    {
        public int index;
        public RectTransform rect;
        public bool completed;
        public GrabbableObject body;
    }

    [Serializable]
    private class DropRequest
    {
        public string source = "role3_unity";
        public string type = "shape_drop_request";
        public string session_id;
        public int request_id;
        public string piece_id;
        public string hole_id;
        public float rotation_deg;
        public float offset_x;
        public float offset_y;
    }

    [Serializable]
    private class DropResult
    {
        public string source;
        public string type;
        public string session_id;
        public int request_id;
        public string piece_id;
        public string hole_id;
        public bool is_correct;
        public string error_type;
        public string message;
        public int completed_count;
        public int total_count;
    }

    private static bool shapeDropMode;

    private static readonly string[] ShapeIds =
    {
        "square",
        "triangle",
        "rectangle",
        "circle"
    };

    private static readonly string[] ShapeNames =
    {
        "Hình vuông",
        "Hình tam giác",
        "Hình chữ nhật",
        "Hình tròn"
    };

    private static readonly Color[] ShapeColors =
    {
        new Color32(0, 178, 246, 255),
        new Color32(255, 199, 24, 255),
        new Color32(0, 220, 129, 255),
        new Color32(255, 77, 90, 255)
    };

    private static readonly Color Navy =
        new Color32(7, 29, 53, 240);

    private static readonly Color Green =
        new Color32(0, 225, 139, 255);

    private static readonly Color Yellow =
        new Color32(255, 211, 33, 255);

    private readonly Piece[] pieces =
        new Piece[4];

    private readonly RectTransform[] holes =
        new RectTransform[4];

    private readonly Image[] holeImages =
        new Image[4];

    private readonly TMP_Text[] counters =
        new TMP_Text[4];

    private readonly List<UnityEngine.Object> generatedAssets =
        new List<UnityEngine.Object>();

    private Canvas canvas;
    private SpatialWorkspace workspace;
    private Vector3 handPosition;
    private bool handHasDepth;

    private readonly Sprite[] emptyHoleSprites = new Sprite[4];

    private TMP_Text totalText;
    private TMP_Text feedbackText;
    private RectTransform progressBar;

    private Piece dragging;
    private Piece pendingPiece;

    private int dragSource;
    private int pendingHole = -1;
    private int requestCounter;
    private int pendingRequestId;
    private int completedCount;
    private int activeTouchId = -1;

    private string sessionId;
    private string activeHand;
    private string pendingRequestJson;

    private float lastHandPacket;
    private float pendingSince;
    private float nextReachLog;

    private bool pending;
    private bool gameScreen;

    // =====================================================
    // AUTO INSTALL
    // =====================================================

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(
        Scene scene,
        LoadSceneMode mode)
    {
        if (scene.name == "MainMenu")
        {
            if (UnityEngine.Object.FindAnyObjectByType<
                ShapeDropFeature>() == null)
            {
                new GameObject("ShapeDrop_Menu")
                    .AddComponent<ShapeDropFeature>();
            }
        }
        else if (
            scene.name == "ARMain" &&
            shapeDropMode)
        {
            // Không chạy logic ghép cạnh trong THẢ HÌNH.
            ScanScreenController oldController =
                UnityEngine.Object.FindAnyObjectByType<
                    ScanScreenController>();

            if (oldController != null)
                oldController.enabled = false;

            ARObjectPlacement oldPlacement =
                UnityEngine.Object.FindAnyObjectByType<
                    ARObjectPlacement>();

            if (oldPlacement != null)
                oldPlacement.enabled = false;

            GameObject scanCanvas =
                GameObject.Find("ScanCanvas");

            if (scanCanvas != null)
                scanCanvas.SetActive(false);

            GameObject oldPlane =
                GameObject.Find("AR Default Plane");

            if (oldPlane != null)
                oldPlane.SetActive(false);

            new GameObject("ShapeDrop_Game")
                .AddComponent<ShapeDropFeature>();
        }
    }

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
        SpatialHandInteractor.HandUpdated += OnHandPacket;
        SpatialHandInteractor.TrackingLost += CancelHandDrag;
        HandDataReceiver.GeometryResultReceived += OnGeometryResult;
    }

    private void OnDisable()
    {
        EnhancedTouchSupport.Disable();
        activeTouchId = -1;
        SpatialHandInteractor.HandUpdated -= OnHandPacket;
        SpatialHandInteractor.TrackingLost -= CancelHandDrag;
        HandDataReceiver.GeometryResultReceived -= OnGeometryResult;
        CancelHandDrag();
    }

    private void Start()
    {
        string currentScene =
            SceneManager.GetActiveScene().name;

        if (currentScene == "MainMenu")
        {
            InstallMenu();
        }
        else if (
            currentScene == "ARMain" &&
            shapeDropMode)
        {
            gameScreen = true;
            InstallGame();
        }
    }

    // =====================================================
    // MENU
    // =====================================================

    private void InstallMenu()
    {
        Canvas menuCanvas = null;

        foreach (Canvas candidate in UnityEngine.Object.FindObjectsByType<Canvas>(
            FindObjectsSortMode.None))
        {
            if (candidate.transform.Find("ShapeSelectionPanel") != null)
            {
                menuCanvas = candidate;
                break;
            }
        }

        if (menuCanvas == null)
        {
            Debug.LogError("Không tìm thấy MainMenu Canvas");
            return;
        }

        Transform panel =
            menuCanvas.transform.Find(
                "ShapeSelectionPanel"
            );

        if (panel == null)
        {
            Debug.LogError(
                "Không tìm thấy ShapeSelectionPanel"
            );
            return;
        }

        Transform title =
            panel.Find("ShapeTitleText");

        if (title != null)
        {
            TMP_Text text =
                title.GetComponent<TMP_Text>();

            if (text != null)
                text.text = "CHỌN CHẾ ĐỘ";
        }

        Transform cube = panel.Find("CubeButton");
        Transform prism =
            panel.Find("RectangularPrismButton");
        Transform pyramid =
            panel.Find("SquarePyramidButton");

        if (
            cube == null ||
            prism == null ||
            pyramid == null)
        {
            Debug.LogError(
                "Không tìm thấy 3 nút chọn hình cũ"
            );
            return;
        }

        ConfigureMenuButton(
            cube,
            "GHÉP HÌNH LẬP PHƯƠNG",
            new Vector2(-280, 85)
        );

        ConfigureMenuButton(
            prism,
            "GHÉP HÌNH HỘP CHỮ NHẬT",
            new Vector2(280, 85)
        );

        ConfigureMenuButton(
            pyramid,
            "GHÉP HÌNH CHÓP TỨ GIÁC ĐỀU",
            new Vector2(-280, -110)
        );

        // Giữ nguyên event gốc của các nút ghép.
        foreach (Transform item in new[]
        {
            cube, prism, pyramid
        })
        {
            Button button =
                item.GetComponent<Button>();

            if (button != null)
            {
                button.onClick.AddListener(
                    () => shapeDropMode = false
                );
            }
        }

        // Nút THẢ HÌNH
        GameObject obj = new GameObject(
            "ShapeDropButton",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button)
        );

        obj.transform.SetParent(panel, false);

        RectTransform rect =
            obj.GetComponent<RectTransform>();

        rect.anchorMin = rect.anchorMax =
            new Vector2(0.5f, 0.5f);

        rect.sizeDelta =
            new Vector2(490, 150);

        rect.anchoredPosition =
            new Vector2(280, -110);

        Image image = obj.GetComponent<Image>();

        Image oldImage =
            cube.GetComponent<Image>();

        if (oldImage != null)
        {
            image.sprite = oldImage.sprite;
            image.type = oldImage.type;
        }

        image.color =
            new Color32(0, 132, 148, 255);

        Button buttonNew =
            obj.GetComponent<Button>();

        buttonNew.targetGraphic = image;

        TMP_Text oldText =
            cube.GetComponentInChildren<TMP_Text>(true);

        if (oldText != null)
        {
            GameObject label = Instantiate(
                oldText.gameObject,
                obj.transform,
                false
            );

            label.name = "ShapeDropLabel";

            TMP_Text text =
                label.GetComponent<TMP_Text>();

            text.text = "THẢ HÌNH";
            text.fontSize = 38f;
            text.alignment =
                TextAlignmentOptions.Center;
            text.raycastTarget = false;

            RectTransform labelRect =
                text.rectTransform;

            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }
        else
        {
            AddText(
                obj.transform,
                "THẢ HÌNH",
                Vector2.zero,
                new Vector2(490, 150),
                38,
                Color.white
            );
        }

        buttonNew.onClick.AddListener(() =>
        {
            shapeDropMode = true;
            SceneManager.LoadScene("ARMain");
        });
    }

    private static void ConfigureMenuButton(
        Transform button,
        string label,
        Vector2 position)
    {
        RectTransform rect =
            button.GetComponent<RectTransform>();

        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta =
            new Vector2(490f, 150f);

        TMP_Text text =
            button.GetComponentInChildren<TMP_Text>(true);

        if (text != null)
        {
            text.text = label;
            text.fontSize = 34f;
            text.alignment =
                TextAlignmentOptions.Center;
        }
    }

    // =====================================================
    // GAME
    // =====================================================

    private void InstallGame()
    {
        sessionId = Guid.NewGuid().ToString("N");

        GameObject root = new GameObject(
            "ShapeDropCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );

        canvas = root.GetComponent<Canvas>();

        canvas.renderMode =
            RenderMode.ScreenSpaceOverlay;

        canvas.overrideSorting = true;
        canvas.sortingOrder = 100;

        CanvasScaler scaler =
            root.GetComponent<CanvasScaler>();

        scaler.uiScaleMode =
            CanvasScaler.ScaleMode.ScaleWithScreenSize;

        scaler.referenceResolution =
            new Vector2(1600, 900);

        scaler.screenMatchMode =
            CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

        scaler.matchWidthOrHeight = 0.5f;

        workspace = SpatialWorkspace.Ensure();
        workspace.ConfigureCanvas(canvas);

        BuildInterface();

        // Dùng receiver tích hợp đang có.
        if (
            UnityEngine.Object.FindAnyObjectByType<
                HandDataReceiver>() == null
        )
        {
            new GameObject("ShapeDrop_Receiver")
                .AddComponent<HandDataReceiver>();
        }
    }

    private void BuildInterface()
    {
        // =================================================
        // HEADER
        // =================================================

        RectTransform header = Panel(
            canvas.transform,
            "Header",
            Vector2.zero,
            new Vector2(1600, 108),
            new Color32(58, 62, 74, 214)
        );

        header.anchorMin = new Vector2(0.5f, 1f);
        header.anchorMax = new Vector2(0.5f, 1f);
        header.pivot = new Vector2(0.5f, 1f);

        AddText(
            header,
            "THẢ HÌNH",
            new Vector2(0, -50),
            new Vector2(700, 56),
            42,
            Color.white
        );

        MakeButton(
            header,
            "QUAY LẠI",
            new Vector2(-675, -52),
            new Vector2(195, 65),
            BackToMenu
        );

        MakeButton(
            header,
            "LÀM LẠI",
            new Vector2(610, -52),
            new Vector2(195, 65),
            ResetGame
        );

        // =================================================
        // BỐN LỖ ĐEN
        // =================================================

        Vector2[] holePositions =
        {
            new Vector2(-70, 135),
            new Vector2(235, 135),
            new Vector2(-70, -75),
            new Vector2(235, -75)
        };

        Vector2[] holeSizes =
        {
            new Vector2(150, 150),
            new Vector2(155, 155),
            new Vector2(215, 135),
            new Vector2(150, 150)
        };

        Material boardMaterial = workspace.MakeMaterial(new Color32(29, 53, 72, 255));

        for (int i = 0; i < 4; i++)
        {
            holes[i] = Panel(
                canvas.transform,
                "Hole_" + ShapeIds[i],
                holePositions[i],
                holeSizes[i],
                Color.white
            );

            holes[i].GetComponent<Image>().enabled = false;

            RectTransform back = Panel(
                holes[i], "BackOfHole", Vector2.zero, holeSizes[i], Color.white
            );
            back.localPosition = new Vector3(0f, 0f, 105f);

            Image image = back.GetComponent<Image>();

            image.sprite = ShapeSprite(
                (Shape)i,
                Color.black,
                Color.black
            );

            image.raycastTarget = false;
            holeImages[i] = image;
            emptyHoleSprites[i] = image.sprite;

            GameObject rim = MakeSolid(
                holes[i], "HoleRim3D", i, holeSizes[i], 100f, true, boardMaterial
            );
            rim.transform.localPosition = new Vector3(0f, 0f, 50f);
        }

        // =================================================
        // BỐN KHỐI MÀU CÓ ĐỘ DÀY
        // =================================================

        Vector2[] starts =
        {
            new Vector2(-530, -245),
            new Vector2(-230, -245),
            new Vector2(75, -245),
            new Vector2(385, -245)
        };

        RectTransform tray = Panel(
            canvas.transform, "PieceTray", new Vector2(-85f, -245f),
            new Vector2(1190f, 210f), Navy
        );
        tray.localPosition += Vector3.forward * 120f;

        for (int i = 0; i < 4; i++)
        {
            Piece piece = new Piece();

            piece.index = i;

            RectTransform group = Container(
                canvas.transform,
                "Piece_" + ShapeIds[i],
                starts[i],
                holeSizes[i]
            );

            MakeSolid(
                group, "SolidPiece3D", i, holeSizes[i] * 0.94f, 70f, false,
                workspace.MakeMaterial(ShapeColors[i])
            );

            group.localPosition = new Vector3(starts[i].x, starts[i].y, -50f);
            piece.body = group.gameObject.AddComponent<GrabbableObject>();
            piece.body.RememberHome();

            AddText(
                group,
                ShapeNames[i],
                new Vector2(0, -98),
                new Vector2(215, 36),
                21,
                Color.white
            );

            piece.rect = group;
            pieces[i] = piece;
        }

        // =================================================
        // BẢNG TRẠNG THÁI
        // =================================================

        RectTransform status = Panel(
            canvas.transform,
            "StatusPanel",
            new Vector2(-18, -145),
            new Vector2(290, 460),
            Navy
        );

        status.anchorMin = new Vector2(1, 1);
        status.anchorMax = new Vector2(1, 1);
        status.pivot = new Vector2(1, 1);

        AddText(
            status,
            "TRẠNG THÁI",
            new Vector2(0, 195),
            new Vector2(270, 44),
            26,
            Color.white
        );

        totalText = AddText(
            status,
            "Đã đặt: 0/4",
            new Vector2(0, 150),
            new Vector2(270, 36),
            22,
            Color.white
        );

        Panel(
            status,
            "ProgressBackground",
            new Vector2(0, 114),
            new Vector2(255, 13),
            new Color32(80, 94, 110, 255)
        );

        progressBar = Panel(
            status,
            "ProgressFill",
            new Vector2(-127.5f, 114),
            new Vector2(0, 13),
            Green
        );

        progressBar.pivot =
            new Vector2(0, 0.5f);

        for (int i = 0; i < 4; i++)
        {
            RectTransform row = Panel(
                status,
                "Row_" + i,
                new Vector2(0, 53 - i * 75),
                new Vector2(267, 65),
                new Color32(33, 53, 77, 255)
            );

            RectTransform icon = Panel(
                row,
                "Icon",
                new Vector2(-108, 0),
                new Vector2(36, 36),
                Color.white
            );

            icon.GetComponent<Image>().sprite =
                ShapeSprite(
                    (Shape)i,
                    ShapeColors[i],
                    ShapeColors[i]
                );

            AddText(
                row,
                ShapeNames[i],
                new Vector2(-6, 0),
                new Vector2(165, 38),
                17,
                Color.white
            );

            counters[i] = AddText(
                row,
                "0/1",
                new Vector2(108, 0),
                new Vector2(50, 38),
                19,
                Color.white
            );
        }

        // =================================================
        // THÔNG BÁO KẾT QUẢ
        // =================================================

        feedbackText = AddText(
            canvas.transform,
            "Chọn một khối để bắt đầu",
            new Vector2(-520, 70),
            new Vector2(400, 240),
            24,
            Color.white
        );

        // =================================================
        // CON TRỎ PINCH
        // =================================================

        // SpatialHandInteractor draws one shared 3D hand cursor.

        UpdateProgress();
    }

    // =====================================================
    // UI HELPERS
    // =====================================================

    private GameObject MakeSolid(
        Transform parent,
        string name,
        int shape,
        Vector2 size,
        float depth,
        bool hole,
        Material material)
    {
        Mesh mesh = SpatialShapeMesh.Make(
            shape, size, depth, hole, new Vector2(300f, 210f)
        );
        generatedAssets.Add(mesh);

        GameObject obj = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        obj.transform.SetParent(parent, false);
        obj.GetComponent<MeshFilter>().sharedMesh = mesh;
        obj.GetComponent<MeshRenderer>().sharedMaterial = material;

        if (!hole)
        {
            MeshCollider collider = obj.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = true;
        }

        return obj;
    }

    private static RectTransform Container(
        Transform parent,
        string name,
        Vector2 position,
        Vector2 size)
    {
        GameObject obj = new GameObject(
            name,
            typeof(RectTransform)
        );

        obj.transform.SetParent(parent, false);

        RectTransform rect =
            obj.GetComponent<RectTransform>();

        rect.anchorMin = rect.anchorMax =
            new Vector2(0.5f, 0.5f);

        rect.pivot =
            new Vector2(0.5f, 0.5f);

        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        return rect;
    }

    private static RectTransform Panel(
        Transform parent,
        string name,
        Vector2 position,
        Vector2 size,
        Color color)
    {
        RectTransform rect =
            Container(parent, name, position, size);

        Image image =
            rect.gameObject.AddComponent<Image>();

        image.color = color;
        image.raycastTarget = false;

        return rect;
    }

    private static TMP_Text AddText(
        Transform parent,
        string value,
        Vector2 position,
        Vector2 size,
        float fontSize,
        Color color)
    {
        RectTransform rect = Container(
            parent,
            "Text",
            position,
            size
        );

        TextMeshProUGUI text =
            rect.gameObject.AddComponent<TextMeshProUGUI>();

        text.text = value;
        text.fontSize = fontSize;
        text.color = color;

        text.alignment =
            TextAlignmentOptions.Center;

        text.textWrappingMode =
            TextWrappingModes.Normal;

        text.raycastTarget = false;

        return text;
    }

    private static void MakeButton(
        Transform parent,
        string label,
        Vector2 position,
        Vector2 size,
        Action callback)
    {
        RectTransform rect = Panel(
            parent,
            "Button_" + label,
            position,
            size,
            Color.white
        );

        Image image =
            rect.GetComponent<Image>();

        image.raycastTarget = true;

        Button button =
            rect.gameObject.AddComponent<Button>();

        button.targetGraphic = image;

        button.onClick.AddListener(
            () => callback()
        );

        AddText(
            rect,
            label,
            Vector2.zero,
            size,
            23,
            new Color32(27, 45, 66, 255)
        );
    }

    // =====================================================
    // SHAPE SPRITES
    // =====================================================

    private Sprite ShapeSprite(
        Shape shape,
        Color fill,
        Color outline)
    {
        const int size = 160;

        Texture2D texture = new Texture2D(
            size,
            size,
            TextureFormat.RGBA32,
            false
        );

        texture.filterMode =
            FilterMode.Bilinear;

        Color32[] pixels =
            new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px =
                    (x + 0.5f) / size * 2f - 1f;

                float py =
                    (y + 0.5f) / size * 2f - 1f;

                bool inside =
                    InsideShape(shape, px, py);

                pixels[y * size + x] =
                    inside ? fill : Color.clear;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f)
        );

        generatedAssets.Add(texture);
        generatedAssets.Add(sprite);

        return sprite;
    }

    private static bool InsideShape(
        Shape shape,
        float x,
        float y)
    {
        switch (shape)
        {
            case Shape.Square:
                return Mathf.Abs(x) <= 0.84f &&
                       Mathf.Abs(y) <= 0.84f;

            case Shape.Rectangle:
                return Mathf.Abs(x) <= 0.94f &&
                       Mathf.Abs(y) <= 0.61f;

            case Shape.Circle:
                return x * x + y * y <=
                       0.85f * 0.85f;

            case Shape.Triangle:
                if (y < -0.84f || y > 0.87f)
                    return false;

                float halfWidth =
                    (0.87f - y) / 1.71f * 0.88f;

                return Mathf.Abs(x) <= halfWidth;
        }

        return false;
    }

    // =====================================================
    // INPUT: MOUSE / TOUCH / HAND TRACKING
    // =====================================================

    private void Update()
    {
        if (!gameScreen)
            return;

        // ===============================
        // TOUCH / MOUSE
        // ===============================

        if (SpatialTouchInput.Read(ref activeTouchId, out Vector2 touchPoint,
            out TouchPhase touchPhase))
        {
            if (touchPhase == TouchPhase.Began)
                BeginDrag(touchPoint, 1);
            else if (touchPhase == TouchPhase.Ended)
                EndDrag(touchPoint, 1);
            else if (touchPhase == TouchPhase.Canceled)
            {
                if (dragSource == 1)
                    CancelDrag();
            }
            else
                MoveDrag(touchPoint, 1);
        }
        else if (Mouse.current != null && !PhoneHeadsetDisplay.IsActive)
        {
            Vector2 point =
                Mouse.current.position.ReadValue();

            if (
                Mouse.current.leftButton
                    .wasPressedThisFrame
            )
            {
                BeginDrag(point, 1);
            }

            if (Mouse.current.leftButton.isPressed)
                MoveDrag(point, 1);

            if (
                Mouse.current.leftButton
                    .wasReleasedThisFrame
            )
            {
                EndDrag(point, 1);
            }
        }

        // ===============================
        // LOST HAND TRACKING
        // ===============================

        if (
            activeHand != null &&
            Time.unscaledTime -
                lastHandPacket > 0.7f
        )
        {
            CancelHandDrag();
        }

        // ===============================
        // WAIT FOR ROLE 2
        // ===============================

        if (
            pending &&
            pendingPiece != null &&
            Time.unscaledTime > pendingSince + 5f
        )
        {
            // Role 2 caches results by request_id. Retry the same request
            // after a lost response or reconnect without placing it twice.
            pendingSince = Time.unscaledTime;

            bool sent = HandDataReceiver.SendGeometryJson(
                pendingRequestJson
            );

            feedbackText.text = sent
                ? "Đang gửi lại yêu cầu kiểm tra hình học..."
                : "Mất kết nối bộ xử lý. Đang kết nối và gửi lại...";
        }
    }

    // =====================================================
    // ROLE 1: HAND TRACKING
    // =====================================================

    private void OnHandPacket(
        SpatialHandInteractor.HandFrame hand)
    {
        if (!gameScreen || canvas == null || canvas.worldCamera == null)
            return;

        if (activeHand != null && hand.hand != activeHand)
            return;

        lastHandPacket = Time.unscaledTime;
        handPosition = hand.position;
        handHasDepth = hand.hasDepth;

        Vector3 projected = canvas.worldCamera.WorldToScreenPoint(handPosition);

        if (projected.z <= 0f)
        {
            CancelHandDrag();
            return;
        }

        Vector2 point = new Vector2(projected.x, projected.y);

        if (
            hand.gesture == "PINCH_START" ||
            hand.gesture == "PINCH_HOLD"
        )
        {
            if (dragSource == 0)
            {
                if (!hand.overButton)
                {
                    if (handHasDepth)
                        BeginSpatialDrag();
                    else
                        BeginDrag(point, 2);
                }
            }
            else if (dragSource == 2 && handHasDepth && dragging != null)
                dragging.body.MoveGrab(handPosition);
            else if (dragSource == 2)
                MoveDrag(point, 2);

            // A pinch away from a piece must not reserve this hand.
            if (dragSource == 2)
                activeHand = hand.hand;
        }
        else if (hand.gesture == "PINCH_END" || hand.gesture == "OPEN")
        {
            if (dragSource == 2)
                EndDrag(point, 2);

            activeHand = null;
        }
    }

    private void BeginSpatialDrag()
    {
        if (pending)
            return;

        Piece nearest = null;
        float radius = 0.035f;
        float closestDistance = float.PositiveInfinity;
        Vector3 closestPosition = Vector3.zero;

        foreach (Piece piece in pieces)
        {
            if (piece.completed)
                continue;

            float distance = piece.body.DistanceTo(handPosition);

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestPosition = piece.rect.position;
            }

            if (distance <= radius)
            {
                radius = distance;
                nearest = piece;
            }
        }

        if (nearest == null)
        {
            if (Time.unscaledTime >= nextReachLog && !float.IsInfinity(closestDistance))
            {
                nextReachLog = Time.unscaledTime + 2f;
                Transform view = canvas.worldCamera.transform;
                SpatialWorkspace.LogInteraction("Hand reach: handDepth=" +
                    view.InverseTransformPoint(handPosition).z.ToString("F3") +
                    "; pieceDepth=" + view.InverseTransformPoint(closestPosition).z.ToString("F3") +
                    "; gap=" + closestDistance.ToString("F3"));
            }
            return;
        }

        dragging = nearest;
        dragSource = 2;
        nearest.body.BeginGrab(handPosition);
        SpatialWorkspace.LogInteraction("Hand grab: " + ShapeIds[nearest.index]);
    }

    private void CancelHandDrag()
    {
        if (dragSource == 2)
            CancelDrag();

        activeHand = null;
        lastHandPacket = 0f;
    }

    // =====================================================
    // SCREEN POSITION
    // =====================================================

    private bool PositionInCanvas(
        RectTransform rect,
        Vector2 screenPoint)
    {
        Vector3 world;

        if (!RectTransformUtility
            .ScreenPointToWorldPointInRectangle(
                rect,
                screenPoint,
                canvas.worldCamera,
                out world
            ))
            return false;

        rect.position = world;

        return true;
    }

    // =====================================================
    // BEGIN DRAG
    // =====================================================

    private void BeginDrag(
        Vector2 point,
        int inputSource)
    {
        SpatialWorkspace.LogInteraction("Drop pointer: " + inputSource + " " + point);

        if (
            dragSource != 0 ||
            pending
        )
            return;

        for (int i = 0; i < pieces.Length; i++)
        {
            Piece piece = pieces[i];

            if (piece.completed)
                continue;

            if (
                RectTransformUtility
                .RectangleContainsScreenPoint(
                    piece.rect,
                    point,
                    canvas.worldCamera
                )
            )
            {
                dragging = piece;
                dragSource = inputSource;

                piece.rect.SetAsLastSibling();

                MoveDrag(point, inputSource);

                return;
            }
        }
    }

    // =====================================================
    // MOVE DRAG
    // =====================================================

    private void MoveDrag(
        Vector2 point,
        int inputSource)
    {
        if (
            dragSource != inputSource ||
            dragging == null
        )
            return;

        PositionInCanvas(
            dragging.rect,
            point
        );
    }

    // =====================================================
    // DROP -> SEND TO ROLE 2
    // =====================================================

    private void EndDrag(
        Vector2 point,
        int inputSource)
    {
        if (
            dragSource != inputSource ||
            dragging == null
        )
            return;

        Piece piece = dragging;

        // The release packet may arrive without a final HOLD packet.
        if (inputSource == 2 && handHasDepth)
            piece.body.MoveGrab(handPosition);

        dragging = null;
        dragSource = 0;

        if (inputSource == 1 || !handHasDepth)
        {
            Plane board = new Plane(workspace.transform.forward, workspace.transform.position);
            Ray ray = canvas.worldCamera.ScreenPointToRay(point);

            if (board.Raycast(ray, out float distance))
                piece.rect.position = ray.GetPoint(distance);
        }

        // Tìm lỗ gần nhất.
        // Giữ phần quyết định đúng/sai của hình cho Role 2.

        int nearestHole = 0;

        float best =
            float.PositiveInfinity;

        Vector2 bestOffset = Vector2.zero;

        for (int i = 0; i < holes.Length; i++)
        {
            Vector3 offset = holes[i].InverseTransformPoint(piece.rect.position);

            Vector2 normalized = new Vector2(
                offset.x /
                    Mathf.Max(
                        1f,
                        holes[i].rect.width
                    ),

                offset.y /
                    Mathf.Max(
                        1f,
                        holes[i].rect.height
                    )
            );

            float distance =
                normalized.sqrMagnitude;

            if (distance < best)
            {
                best = distance;
                nearestHole = i;
                bestOffset = normalized;
            }
        }

        // Proximity is handled in Unity; Role 2 still decides shape,
        // in-plane offset and rotation using the original JSON contract.
        float depth = Mathf.Abs(
            holes[nearestHole].InverseTransformPoint(piece.rect.position).z *
            holes[nearestHole].lossyScale.z
        );

        if (inputSource == 2)
            SpatialWorkspace.LogInteraction("Hand release: " + ShapeIds[piece.index] +
                "; depth=" + depth.ToString("F3"));

        if (inputSource == 2 && handHasDepth && depth > 0.055f)
        {
            piece.body.ReturnHome();
            feedbackText.text = "Đưa khối sát mặt bảng trước khi thả";
            return;
        }

        DropRequest request =
            new DropRequest();

        request.session_id = sessionId;

        request.request_id =
            ++requestCounter;

        request.piece_id =
            ShapeIds[piece.index];

        request.hole_id =
            ShapeIds[nearestHole];

        request.offset_x =
            bestOffset.x;

        request.offset_y =
            bestOffset.y;

        request.rotation_deg =
            piece.rect.localEulerAngles.z;

        string json =
            JsonUtility.ToJson(request);

        pendingRequestJson = json;

        pendingPiece = piece;

        pendingHole =
            nearestHole;

        pendingRequestId =
            request.request_id;

        bool sent =
            HandDataReceiver.SendGeometryJson(
                json
            );

        if (!sent)
        {
            pendingRequestJson = null;
            pendingPiece = null;
            pendingHole = -1;

            piece.body.ReturnHome();

            feedbackText.text =
                "Chưa kết nối máy tính. Hãy kiểm tra Wi-Fi.";

            return;
        }

        pending = true;

        pendingSince =
            Time.unscaledTime;

        feedbackText.text =
            "Đang kiểm tra hình học...";
    }

    // =====================================================
    // ROLE 2 RESULT
    // =====================================================

    private void OnGeometryResult(string json)
    {
        if (
            !gameScreen ||
            !pending ||
            pendingPiece == null ||
            string.IsNullOrEmpty(json)
        )
            return;

        DropResult result;

        try
        {
            result =
                JsonUtility.FromJson<DropResult>(
                    json
                );
        }
        catch (Exception)
        {
            return;
        }

        if (result == null)
            return;

        // Chỉ nhận đúng phản hồi Role 2.
        if (
            result.source !=
                "role2_geometry_reasoning" ||

            result.type !=
                "shape_drop_result" ||

            result.session_id != sessionId ||

            result.request_id !=
                pendingRequestId ||

            result.piece_id !=
                ShapeIds[pendingPiece.index] ||

            result.hole_id !=
                ShapeIds[pendingHole]
        )
            return;

        Piece piece = pendingPiece;
        int hole = pendingHole;

        pending = false;
        pendingRequestJson = null;
        pendingPiece = null;
        pendingHole = -1;

        // Role 2 quyết định.
        if (result.is_correct)
        {
            StartCoroutine(
                AcceptPiece(
                    piece,
                    hole
                )
            );
        }
        else
        {
            piece.body.ReturnHome();

            feedbackText.text =
                string.IsNullOrEmpty(
                    result.message
                )
                ? "Chưa chính xác, hãy thử lại"
                : result.message;
        }
    }

    // =====================================================
    // CORRECT: PIECE ENTERS HOLE
    // =====================================================

    private IEnumerator AcceptPiece(
        Piece piece,
        int hole)
    {
        piece.completed = true;
        piece.body.Available = false;

        Vector3 from = piece.rect.position;

        Vector3 to = holes[hole].position + workspace.transform.forward * 0.02f;

        foreach (TMP_Text label in piece.rect.GetComponentsInChildren<TMP_Text>(true))
            label.gameObject.SetActive(false);

        Vector3 originalScale =
            piece.rect.localScale;

        float duration = 0.24f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed / duration
                );

            piece.rect.position =
                Vector3.Lerp(
                    from,
                    to,
                    t
                );

            piece.rect.localScale =
                Vector3.Lerp(
                    originalScale,
                    originalScale * 0.97f,
                    t
                );

            yield return null;
        }

        piece.rect.position = to;

        holeImages[hole].sprite =
            ShapeSprite(
                (Shape)hole,
                ShapeColors[hole],
                ShapeColors[hole]
            );

        completedCount++;

        UpdateProgress();

        feedbackText.text =
            completedCount == 4
                ? "CHÚC MỪNG! ĐÃ HOÀN THÀNH 4/4!"
                : "Chính xác! Tiếp tục với khối tiếp theo.";
    }

    // =====================================================
    // CANCEL DRAG
    // =====================================================

    private void CancelDrag()
    {
        if (dragging != null)
        {
            dragging.body.ReturnHome();
        }

        dragging = null;
        dragSource = 0;
    }

    // =====================================================
    // UPDATE PROGRESS
    // =====================================================

    private void UpdateProgress()
    {
        if (totalText != null)
        {
            totalText.text =
                "Đã đặt: " +
                completedCount +
                "/4";
        }

        if (progressBar != null)
        {
            progressBar.sizeDelta =
                new Vector2(
                    255f *
                    completedCount / 4f,
                    13f
                );
        }

        for (int i = 0; i < 4; i++)
        {
            if (counters[i] != null)
            {
                counters[i].text =
                    pieces[i].completed
                    ? "1/1"
                    : "0/1";
            }
        }
    }

    // =====================================================
    // BACK TO MENU
    // =====================================================

    private void ResetGame()
    {
        StopAllCoroutines();
        CancelDrag();

        sessionId = Guid.NewGuid().ToString("N");
        pending = false;
        pendingPiece = null;
        pendingHole = -1;
        pendingRequestJson = null;
        requestCounter = pendingRequestId = completedCount = 0;
        activeHand = null;

        for (int i = 0; i < pieces.Length; i++)
        {
            Piece piece = pieces[i];
            piece.completed = false;
            piece.body.Available = true;
            piece.body.ReturnHome();
            holeImages[i].sprite = emptyHoleSprites[i];

            foreach (TMP_Text label in piece.rect.GetComponentsInChildren<TMP_Text>(true))
                label.gameObject.SetActive(true);
        }

        UpdateProgress();
        feedbackText.text = "Chọn một khối để bắt đầu";
    }

    private void BackToMenu()
    {
        CancelDrag();

        shapeDropMode = false;

        GameSession.ReturnToShapeSelection = true;

        SceneManager.LoadScene(
            "MainMenu"
        );
    }

    // =====================================================
    // CLEANUP
    // =====================================================

    private void OnDestroy()
    {
        foreach (
            UnityEngine.Object asset
            in generatedAssets
        )
        {
            if (asset != null)
            {
                Destroy(asset);
            }
        }

        generatedAssets.Clear();
    }
}
