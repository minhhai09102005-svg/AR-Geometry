
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
        public Vector2 startPosition;
        public bool completed;
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
    private RectTransform canvasRect;

    private TMP_Text totalText;
    private TMP_Text feedbackText;
    private RectTransform progressBar;

    private RectTransform handCursor;
    private Image handCursorImage;

    private Piece dragging;
    private Piece pendingPiece;

    private int dragSource;
    private int pendingHole = -1;
    private int requestCounter;
    private int pendingRequestId;
    private int completedCount;

    private string sessionId;
    private string activeHand;

    private float lastHandPacket;
    private float pendingSince;

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
            new GameObject("ShapeDrop_Menu")
                .AddComponent<ShapeDropFeature>();
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
        HandDataReceiver.PacketReceived += OnHandPacket;
        HandDataReceiver.GeometryResultReceived += OnGeometryResult;
    }

    private void OnDisable()
    {
        HandDataReceiver.PacketReceived -= OnHandPacket;
        HandDataReceiver.GeometryResultReceived -= OnGeometryResult;
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
        Canvas menuCanvas =
            UnityEngine.Object.FindAnyObjectByType<Canvas>();

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
                text.text = "MENU";
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

        canvasRect =
            root.GetComponent<RectTransform>();

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

        for (int i = 0; i < 4; i++)
        {
            holes[i] = Panel(
                canvas.transform,
                "Hole_" + ShapeIds[i],
                holePositions[i],
                holeSizes[i],
                Color.white
            );

            Image image =
                holes[i].GetComponent<Image>();

            image.sprite = ShapeSprite(
                (Shape)i,
                Color.black,
                Color.black
            );

            image.raycastTarget = false;
            holeImages[i] = image;
        }

        // =================================================
        // BỐN KHỐI MÀU
        //
        // ĐÃ BỎ HOÀN TOÀN LỚP BACK / SHADOW
        // MỖI KHỐI CHỈ CÒN MỘT LỚP MÀU DUY NHẤT
        // =================================================

        Vector2[] starts =
        {
            new Vector2(-530, -295),
            new Vector2(-230, -295),
            new Vector2(75, -295),
            new Vector2(385, -295)
        };

        for (int i = 0; i < 4; i++)
        {
            Piece piece = new Piece();

            piece.index = i;
            piece.startPosition = starts[i];

            RectTransform group = Container(
                canvas.transform,
                "Piece_" + ShapeIds[i],
                starts[i],
                new Vector2(165, 165)
            );

            // Chỉ tạo hình phía trước.
            // Không còn hình tối lệch phía sau.
            RectTransform front = Panel(
                group,
                "Front",
                Vector2.zero,
                new Vector2(145, 145),
                Color.white
            );

            front.GetComponent<Image>().sprite =
                ShapeSprite(
                    (Shape)i,
                    ShapeColors[i],
                    ShapeColors[i]
                );

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
            new Vector2(0, -208),
            new Vector2(750, 60),
            26,
            Color.white
        );

        // =================================================
        // CON TRỎ PINCH
        // =================================================

        handCursor = Panel(
            canvas.transform,
            "HandCursor",
            Vector2.zero,
            new Vector2(19, 19),
            Yellow
        );

        handCursorImage =
            handCursor.GetComponent<Image>();

        handCursorImage.sprite =
            ShapeSprite(
                Shape.Circle,
                Color.white,
                Color.white
            );

        handCursor.gameObject.SetActive(false);
        handCursor.SetAsLastSibling();

        UpdateProgress();
    }

    // =====================================================
    // UI HELPERS
    // =====================================================

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
        // MOUSE
        // ===============================

        if (Mouse.current != null)
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
        // TOUCH
        // ===============================

        if (
            Touchscreen.current != null &&
            Touchscreen.current.primaryTouch
                .press.wasPressedThisFrame
        )
        {
            BeginDrag(
                Touchscreen.current.primaryTouch
                    .position.ReadValue(),
                1
            );
        }

        if (
            Touchscreen.current != null &&
            Touchscreen.current.primaryTouch
                .press.isPressed
        )
        {
            MoveDrag(
                Touchscreen.current.primaryTouch
                    .position.ReadValue(),
                1
            );
        }

        if (
            Touchscreen.current != null &&
            Touchscreen.current.primaryTouch
                .press.wasReleasedThisFrame
        )
        {
            EndDrag(
                Touchscreen.current.primaryTouch
                    .position.ReadValue(),
                1
            );
        }

        // ===============================
        // LOST HAND TRACKING
        // ===============================

        if (
            dragSource == 2 &&
            Time.unscaledTime -
                lastHandPacket > 0.7f
        )
        {
            CancelDrag();
            activeHand = null;
        }

        if (
            handCursor != null &&
            lastHandPacket > 0 &&
            Time.unscaledTime -
                lastHandPacket > 0.7f
        )
        {
            handCursor.gameObject.SetActive(false);
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
            feedbackText.text =
                "Đang chờ kết quả kiểm tra từ bộ xử lý...";
        }
    }

    // =====================================================
    // ROLE 1: HAND TRACKING
    // =====================================================

    private void OnHandPacket(
        HandDataReceiver.HandPacket packet)
    {
        if (
            !gameScreen ||
            packet == null ||
            packet.hands == null ||
            packet.coordinate_system !=
                "normalized_image"
        )
            return;

        HandDataReceiver.HandData hand = null;

        foreach (
            HandDataReceiver.HandData candidate
            in packet.hands
        )
        {
            if (
                candidate == null ||
                candidate.interaction == null
            )
                continue;

            if (
                activeHand == null ||
                candidate.hand == activeHand
            )
            {
                hand = candidate;
                break;
            }
        }

        if (hand == null)
            return;

        lastHandPacket = Time.unscaledTime;

        Vector2 point = new Vector2(
            Mathf.Clamp01(hand.interaction.x) *
                Screen.width,

            (
                1f -
                Mathf.Clamp01(hand.interaction.y)
            ) * Screen.height
        );

        if (handCursor != null)
        {
            handCursor.gameObject.SetActive(true);

            PositionInCanvas(
                handCursor,
                point
            );

            bool pinching =
                hand.gesture == "PINCH_START" ||
                hand.gesture == "PINCH_HOLD";

            handCursorImage.color =
                pinching ? Green : Yellow;
        }

        if (
            hand.gesture == "PINCH_START" ||
            hand.gesture == "PINCH_HOLD"
        )
        {
            if (activeHand == null)
                activeHand = hand.hand;

            if (dragSource == 0)
                BeginDrag(point, 2);
            else
                MoveDrag(point, 2);
        }
        else if (hand.gesture == "PINCH_END")
        {
            if (dragSource == 2)
                EndDrag(point, 2);

            activeHand = null;
        }
    }

    // =====================================================
    // SCREEN POSITION
    // =====================================================

    private bool PositionInCanvas(
        RectTransform rect,
        Vector2 screenPoint)
    {
        Vector2 local;

        if (!RectTransformUtility
            .ScreenPointToLocalPointInRectangle(
                canvasRect,
                screenPoint,
                null,
                out local
            ))
            return false;

        rect.anchoredPosition = local;

        return true;
    }

    // =====================================================
    // BEGIN DRAG
    // =====================================================

    private void BeginDrag(
        Vector2 point,
        int inputSource)
    {
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
                    null
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

        dragging = null;
        dragSource = 0;

        // Tìm lỗ gần nhất.
        // Unity KHÔNG tự kiểm tra đúng sai.

        int nearestHole = 0;

        float best =
            float.PositiveInfinity;

        Vector2 bestOffset = Vector2.zero;

        for (int i = 0; i < holes.Length; i++)
        {
            Vector2 offset;

            if (!RectTransformUtility
                .ScreenPointToLocalPointInRectangle(
                    holes[i],
                    point,
                    null,
                    out offset
                ))
                continue;

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
            pendingPiece = null;
            pendingHole = -1;

            piece.rect.anchoredPosition =
                piece.startPosition;

            feedbackText.text =
                "Chưa kết nối bộ xử lý. Hãy kiểm tra backend.";

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
            piece.rect.anchoredPosition =
                piece.startPosition;

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

        Vector2 from =
            piece.rect.anchoredPosition;

        Vector2 to =
            holes[hole].anchoredPosition;

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

            piece.rect.anchoredPosition =
                Vector2.Lerp(
                    from,
                    to,
                    t
                );

            piece.rect.localScale =
                Vector3.Lerp(
                    originalScale,
                    Vector3.one * 0.15f,
                    t
                );

            yield return null;
        }

        piece.rect.gameObject.SetActive(false);

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
            dragging.rect.anchoredPosition =
                dragging.startPosition;
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
