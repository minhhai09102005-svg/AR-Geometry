using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuFlow : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject homePanel;
    [SerializeField] private GameObject shapeSelectionPanel;
    [SerializeField] private GameObject instructionPanel;

    [Header("Instruction")]
    [SerializeField] private TMP_Text instructionShapeTitle;
    [SerializeField] private TMP_Text instructionText;

    private void Start()
    {
        ConfigureSpatialMenu();

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

    private void ConfigureSpatialMenu()
    {
        // Match the phone-sized work area instead of the scene's wide
        // desktop camera. A future stereo camera supplies its own projection.
        Camera viewCamera = Camera.main;

        if (viewCamera != null && !viewCamera.stereoEnabled &&
            viewCamera.GetComponent<UnityEngine.XR.ARFoundation.ARCameraManager>() == null)
        {
            viewCamera.fieldOfView = 32f;
            viewCamera.clearFlags = CameraClearFlags.SolidColor;
            viewCamera.backgroundColor = new Color(0.025f, 0.05f, 0.09f);
        }

        Canvas canvas = homePanel.GetComponentInParent<Canvas>().rootCanvas;
        SpatialWorkspace.Ensure().ConfigureCanvas(canvas);

        foreach (GameObject panel in new[]
        {
            homePanel, shapeSelectionPanel, instructionPanel
        })
        {
            RectTransform rect = panel.transform as RectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            Image background = panel.GetComponent<Image>();

            if (background != null)
                background.color = new Color(0.03f, 0.10f, 0.17f, 0.15f);

            foreach (Image image in panel.GetComponentsInChildren<Image>(true))
            {
                if (image.GetComponentInParent<Button>(true) == null)
                    image.raycastTarget = false;
            }
        }

        // The old full-height illustration overlaps the spatial home texts.
        foreach (Image image in homePanel.GetComponentsInChildren<Image>(true))
        {
            if (image.transform != homePanel.transform &&
                image.GetComponentInParent<Button>(true) == null)
                image.enabled = false;
        }

        LayoutPanel(homePanel);
        LayoutPanel(instructionPanel);

        TMP_Text title = shapeSelectionPanel.transform.Find("ShapeTitleText")
            ?.GetComponent<TMP_Text>();

        LayoutText(title, new Vector2(0f, 310f), new Vector2(1200f, 90f), 48f);
        LayoutText(instructionShapeTitle, new Vector2(0f, 300f),
            new Vector2(1200f, 90f), 46f);
        LayoutText(instructionText, new Vector2(0f, 10f),
            new Vector2(1200f, 360f), 32f);

        foreach (Button button in shapeSelectionPanel.GetComponentsInChildren<Button>(true))
        {
            if (button.name == "CubeButton" ||
                button.name == "RectangularPrismButton" ||
                button.name == "SquarePyramidButton" ||
                button.name == "ShapeDropButton")
                continue;

            LayoutRect(button.transform as RectTransform,
                new Vector2(-560f, -390f), new Vector2(330f, 76f));
        }
    }

    private static void LayoutPanel(GameObject panel)
    {
        int row = 0;

        foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.GetComponentInParent<Button>(true) == null)
            {
                text.transform.SetParent(panel.transform, false);
                LayoutText(text, new Vector2(0f, 220f - row * 120f),
                    new Vector2(1250f, 120f), row == 0 ? 50f : 30f);
                row++;
            }
        }

        Button[] buttons = panel.GetComponentsInChildren<Button>(true);

        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].transform.SetParent(panel.transform, false);
            LayoutRect(buttons[i].transform as RectTransform,
                new Vector2((i - (buttons.Length - 1) * 0.5f) * 430f, -280f),
                new Vector2(390f, 100f));

            TMP_Text text = buttons[i].GetComponentInChildren<TMP_Text>(true);

            if (text != null)
            {
                text.fontSize = 32f;
                text.raycastTarget = false;
                text.alignment = TextAlignmentOptions.Center;
                text.transform.SetParent(buttons[i].transform, false);
                text.rectTransform.anchorMin = Vector2.zero;
                text.rectTransform.anchorMax = Vector2.one;
                text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            }

            for (int eventIndex = 0;
                eventIndex < buttons[i].onClick.GetPersistentEventCount(); eventIndex++)
            {
                if (text != null &&
                    buttons[i].onClick.GetPersistentMethodName(eventIndex) == "StartScan")
                    text.text = "BẮT ĐẦU";
            }
        }
    }

    private static void LayoutText(
        TMP_Text text, Vector2 position, Vector2 size, float fontSize)
    {
        if (text == null)
            return;

        LayoutRect(text.rectTransform, position, size);
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
    }

    private static void LayoutRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

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
        GameSession.SelectedShape = ShapeType.Cube;

        ShowInstruction(
            "HÌNH LẬP PHƯƠNG",
            "Khung lập phương và khay cạnh sẽ xuất hiện trước mặt.\n\n" +
            "Chụm tay để gắp cạnh, đưa tới khung rồi mở tay để thả.\n\n" +
            "Nắm khung để xoay. Nhấn Bắt đầu khi sẵn sàng."
        );
    }

    public void SelectRectangularPrism()
    {
        GameSession.SelectedShape = ShapeType.RectangularPrism;

        ShowInstruction(
            "HÌNH HỘP CHỮ NHẬT",
            "Khung hộp chữ nhật và khay cạnh sẽ xuất hiện trước mặt.\n\n" +
            "Chụm tay để gắp cạnh, đưa tới khung rồi mở tay để thả.\n\n" +
            "Nắm khung để xoay. Nhấn Bắt đầu khi sẵn sàng."
        );
    }

    public void SelectSquarePyramid()
    {
        GameSession.SelectedShape = ShapeType.SquarePyramid;

        ShowInstruction(
            "HÌNH CHÓP TỨ GIÁC ĐỀU",
            "Khung chóp và khay cạnh sẽ xuất hiện trước mặt.\n\n" +
            "Chụm tay để gắp cạnh đáy hoặc cạnh bên, đưa tới khung rồi mở tay.\n\n" +
            "Nắm khung để xoay. Nhấn Bắt đầu khi sẵn sàng."
        );
    }

    private void ShowInstruction(string title, string message)
    {
        homePanel.SetActive(false);
        shapeSelectionPanel.SetActive(false);
        instructionPanel.SetActive(true);

        instructionShapeTitle.text = title;
        instructionText.text = message;
    }

    public void BackToShapeSelection()
    {
        ShowShapeSelection();
    }

    public void StartScan()
    {
        SpatialWorkspace.LogInteraction("Start shape: " + GameSession.SelectedShape);
        SceneManager.LoadScene("ARMain");
    }
}
