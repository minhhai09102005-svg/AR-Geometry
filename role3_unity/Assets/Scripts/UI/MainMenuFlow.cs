using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        ShowHome();
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
            "Chuẩn bị mô hình hình lập phương.\n\n" +
            "Đặt mô hình trong vùng camera.\n\n" +
            "Đảm bảo các đỉnh và cạnh có thể quan sát rõ.\n\n" +
            "Nhấn Bắt đầu quét khi sẵn sàng."
        );
    }

    public void SelectRectangularPrism()
    {
        GameSession.SelectedShape = ShapeType.RectangularPrism;

        ShowInstruction(
            "HÌNH HỘP CHỮ NHẬT",
            "Chuẩn bị mô hình hình hộp chữ nhật.\n\n" +
            "Đặt mô hình trong vùng camera.\n\n" +
            "Đảm bảo các đỉnh và cạnh có thể quan sát rõ.\n\n" +
            "Nhấn Bắt đầu quét khi sẵn sàng."
        );
    }

    public void SelectSquarePyramid()
    {
        GameSession.SelectedShape = ShapeType.SquarePyramid;

        ShowInstruction(
            "HÌNH CHÓP TỨ GIÁC ĐỀU",
            "Chuẩn bị mô hình hình chóp tứ giác đều.\n\n" +
            "Đặt mô hình trong vùng camera.\n\n" +
            "Đảm bảo phần đáy và đỉnh hình chóp được quan sát rõ.\n\n" +
            "Nhấn Bắt đầu quét khi sẵn sàng."
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
        SceneManager.LoadScene("ARMain");
    }
}