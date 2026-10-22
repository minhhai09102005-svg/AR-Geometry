using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ARObjectPlacement : MonoBehaviour
{
    [SerializeField]
    private GameObject objectPrefab;

    private ARRaycastManager raycastManager;

    private List<ARRaycastHit> hits = new List<ARRaycastHit>();

    private void Awake()
    {
        raycastManager = GetComponent<ARRaycastManager>();
    }

    private void Update()
    {
        if (Touchscreen.current == null)
            return;

        var touch = Touchscreen.current.primaryTouch;

        if (!touch.press.wasPressedThisFrame)
            return;

        Vector2 touchPosition = touch.position.ReadValue();

        Debug.Log("Touch: " + touchPosition);

        if (raycastManager.Raycast(
            touchPosition,
            hits,
            TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = hits[0].pose;

            Debug.Log("Plane detected!");

            GameObject obj = Instantiate(
                objectPrefab,
                hitPose.position,
                hitPose.rotation
            );

            Debug.Log("Cube created: " + obj.name);
        }
        else
        {
            Debug.Log("No plane at touch position.");
        }
    }
}