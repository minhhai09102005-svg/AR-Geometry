
using System.Collections.Generic;
using UnityEngine;

public class HandCoordinateMapper : MonoBehaviour
{
    [Header("PC webcam - mirrored by Python")]
    [SerializeField]
    private bool mirrorHorizontal = false;

    [SerializeField]
    private bool mirrorVertical = false;

    [Header("Phone AR")]
    [SerializeField]
    private Vector3 movementScale =
        new Vector3(1.5f, 1.5f, 1f);

    [SerializeField]
    private bool invertDepth = true;

    private Vector3 calibrationOrigin;

    private readonly Dictionary<string, HandPoseSmoother>
        phoneFilters =
            new Dictionary<string, HandPoseSmoother>();

    public bool IsCalibrated
    {
        get;
        private set;
    }

    public string CalibratedHand
    {
        get;
        private set;
    }

    public bool UsesPhoneCamera
    {
        get;
        private set;
    }

    // ==========================================
    // RESET
    // ==========================================

    public void ResetCalibration()
    {
        IsCalibrated = false;
        CalibratedHand = null;
        UsesPhoneCamera = false;

        phoneFilters.Clear();
    }

    // ==========================================
    // MAIN MAPPING
    // ==========================================

    public bool TryMap(
        HandDataReceiver.HandData hand,
        out Vector3 point,
        out Vector3 indexTip,
        out bool hasDepth
    )
    {
        point = Vector3.zero;
        indexTip = Vector3.zero;
        hasDepth = false;

        if (
            hand == null ||
            hand.interaction == null
        )
        {
            return false;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

        // Windows PC:
        // BOTH hands can move independently.
        // No CalibratedHand ownership lock.
        return MapDesktop(
            hand,
            out point,
            out indexTip
        );

#else

        // Phone AR:
        // Retain the 3D coordinate mapping.
        if (
            hand.spatial != null &&
            hand.spatial.coordinate_system ==
                "phone_camera_m"
        )
        {
            UsesPhoneCamera = true;

            HandDataReceiver.SpatialData pose =
                hand.spatial;

            if (
                !pose.valid ||
                !TryVector(
                    pose.point,
                    out Vector3 cameraPoint
                ) ||
                !TryVector(
                    pose.index_tip,
                    out Vector3 cameraTip
                ) ||
                !PhoneCameraSender.TryMap(
                    pose.camera_frame_id,
                    cameraPoint,
                    out point
                ) ||
                !PhoneCameraSender.TryMap(
                    pose.camera_frame_id,
                    cameraTip,
                    out indexTip
                )
            )
            {
                return false;
            }

            string name =
                string.IsNullOrEmpty(hand.hand)
                    ? "Unknown"
                    : hand.hand;

            if (
                !phoneFilters.TryGetValue(
                    name,
                    out HandPoseSmoother smoother
                )
            )
            {
                smoother = new HandPoseSmoother();

                phoneFilters[name] = smoother;
            }

            if (
                !smoother.TrySmooth(
                    ref point,
                    ref indexTip,
                    Time.unscaledTime
                )
            )
            {
                return false;
            }

            hasDepth = true;
            IsCalibrated = true;

            return true;
        }

        // Legacy 3D mapping.
        if (
            hand.spatial == null ||
            !hand.spatial.valid ||
            hand.spatial.coordinate_system !=
                "mirrored_camera_m" ||
            !TryVector(
                hand.spatial.point,
                out Vector3 rawPoint
            ) ||
            !TryVector(
                hand.spatial.index_tip,
                out Vector3 rawTip
            )
        )
        {
            return false;
        }

        if (!IsCalibrated)
        {
            if (
                hand.gesture != "PINCH_START" &&
                hand.gesture != "PINCH_HOLD"
            )
            {
                return false;
            }

            calibrationOrigin = rawPoint;
            CalibratedHand = hand.hand;
            IsCalibrated = true;
        }

        if (hand.hand != CalibratedHand)
            return false;

        Vector3 localPoint = Vector3.Scale(
            rawPoint - calibrationOrigin,
            movementScale
        );

        Vector3 localTip = Vector3.Scale(
            rawTip - calibrationOrigin,
            movementScale
        );

        if (invertDepth)
        {
            localPoint.z = -localPoint.z;
            localTip.z = -localTip.z;
        }

        point = transform.TransformPoint(
            localPoint
        );

        indexTip = transform.TransformPoint(
            localTip
        );

        hasDepth = true;
        return true;

#endif
    }

    // ==========================================
    // WINDOWS DESKTOP
    // ==========================================

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

    private bool MapDesktop(
        HandDataReceiver.HandData hand,
        out Vector3 point,
        out Vector3 indexTip
    )
    {
        point = Vector3.zero;
        indexTip = Vector3.zero;

        if (
            !Finite(hand.interaction.x) ||
            !Finite(hand.interaction.y)
        )
        {
            return false;
        }

        SpatialWorkspace workspace =
            GetComponent<SpatialWorkspace>();

        Camera camera =
            workspace != null &&
            workspace.ViewCamera != null
                ? workspace.ViewCamera
                : Camera.main;

        if (camera == null)
            return false;

        float x = Mathf.Clamp01(
            hand.interaction.x
        );

        float y = Mathf.Clamp01(
            hand.interaction.y
        );

        if (mirrorHorizontal)
            x = 1f - x;

        if (mirrorVertical)
            y = 1f - y;

        // MediaPipe: top-left origin.
        // Unity: bottom-left origin.
        Vector2 screenPoint = new Vector2(
            x * Screen.width,
            (1f - y) * Screen.height
        );

        Ray ray = camera.ScreenPointToRay(
            screenPoint
        );

        Plane playPlane;

        if (workspace != null)
        {
            playPlane = new Plane(
                workspace.transform.forward,
                workspace.transform.position
            );
        }
        else
        {
            playPlane = new Plane(
                camera.transform.forward,
                camera.transform.position +
                camera.transform.forward * 0.75f
            );
        }

        if (
            !playPlane.Raycast(
                ray,
                out float hitDistance
            ) ||
            hitDistance <= 0f
        )
        {
            return false;
        }

        point = ray.GetPoint(hitDistance);
        indexTip = point;

        // Auto-calibrated PC mode:
        // The current point is always mapped
        // directly from the camera image.
        IsCalibrated = true;

        // No permanent Left/Right lock.
        CalibratedHand = null;

        UsesPhoneCamera = false;

        return true;
    }

#endif

    // ==========================================
    // VALIDATION
    // ==========================================

    private static bool TryVector(
        HandDataReceiver.SpatialPoint raw,
        out Vector3 value
    )
    {
        value = Vector3.zero;

        if (
            raw == null ||
            !Finite(raw.x) ||
            !Finite(raw.y) ||
            !Finite(raw.z)
        )
        {
            return false;
        }

        value = new Vector3(
            raw.x,
            raw.y,
            raw.z
        );

        return true;
    }

    private static bool Finite(float value)
    {
        return
            !float.IsNaN(value) &&
            !float.IsInfinity(value);
    }
}
