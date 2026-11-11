using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;

public static class PhoneMenuCamera
{
    private static bool loadingMenuCamera;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
#endif
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MainMenu")
        {
            // Reuse the project's existing ARSession, XR Origin and camera.
            // The menu needs the rear-camera feed too, for direct hand input.
            loadingMenuCamera = true;
            SceneManager.LoadSceneAsync("ARMain", LoadSceneMode.Additive);
            return;
        }

        if (scene.name != "ARMain" || mode != LoadSceneMode.Additive || !loadingMenuCamera)
            return;

        loadingMenuCamera = false;
        ARCameraManager manager = null;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (ScanScreenController controller in root.GetComponentsInChildren<ScanScreenController>(true))
                controller.enabled = false;
            foreach (ARObjectPlacement placement in root.GetComponentsInChildren<ARObjectPlacement>(true))
                placement.enabled = false;
            foreach (MockDetectionProvider mock in root.GetComponentsInChildren<MockDetectionProvider>(true))
                mock.enabled = false;
            foreach (ARPlaneManager planes in root.GetComponentsInChildren<ARPlaneManager>(true))
                planes.enabled = false;
            foreach (EventSystem events in root.GetComponentsInChildren<EventSystem>(true))
                events.gameObject.SetActive(false);
            foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas.name == "ScanCanvas")
                    canvas.gameObject.SetActive(false);
            }
            if (root.name == "ScanCanvas" || root.name == "AR Default Plane")
                root.SetActive(false);
            if (manager == null)
                manager = root.GetComponentInChildren<ARCameraManager>(true);
        }

        if (manager == null)
            return;

        Camera rear = manager.GetComponent<Camera>();
        foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            if (camera != rear && camera.gameObject.scene.name == "MainMenu")
                camera.enabled = false;
        }

        SpatialWorkspace.Ensure().UseCamera(rear);
        PhoneCameraSender.Ensure(manager);
    }
}
