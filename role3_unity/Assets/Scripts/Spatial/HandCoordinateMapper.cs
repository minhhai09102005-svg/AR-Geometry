using System.Collections.Generic;
using UnityEngine;

public class HandCoordinateMapper : MonoBehaviour
{
    [Header("Laptop webcam facing the user")]
    [SerializeField] private Vector3 movementScale = new Vector3(1.5f, 1.5f, 1f);
    [SerializeField] private bool invertDepth = true;

    private Vector3 origin;
    private readonly Dictionary<string, HandPoseSmoother> phoneFilters =
        new Dictionary<string, HandPoseSmoother>();

    public bool IsCalibrated { get; private set; }
    public string CalibratedHand { get; private set; }
    public bool UsesPhoneCamera { get; private set; }

    public void ResetCalibration()
    {
        IsCalibrated = false;
        CalibratedHand = null;
        UsesPhoneCamera = false;
        phoneFilters.Clear();
    }

    public bool TryMap(
        HandDataReceiver.HandData hand,
        out Vector3 point,
        out Vector3 indexTip,
        out bool hasDepth
    )
    {
        point = indexTip = Vector3.zero;
        hasDepth = false;

        if (hand == null || hand.interaction == null)
            return false;

        if (hand.spatial == null)
        {
            // Older backends still work as a pointer on the work plane.
            if (!Finite(hand.interaction.x) || !Finite(hand.interaction.y))
                return false;

            point = transform.TransformPoint(new Vector3(
                (Mathf.Clamp01(hand.interaction.x) - 0.5f) * 0.64f,
                (0.5f - Mathf.Clamp01(hand.interaction.y)) * 0.36f,
                0f
            ));

            indexTip = point;
            return true;
        }

        HandDataReceiver.SpatialData pose = hand.spatial;

        if (pose.coordinate_system == "phone_camera_m")
        {
            UsesPhoneCamera = true;
            if (!pose.valid || !TryVector(pose.point, out Vector3 phonePoint) ||
                !TryVector(pose.index_tip, out Vector3 phoneTip) ||
                !PhoneCameraSender.TryMap(pose.camera_frame_id, phonePoint, out point) ||
                !PhoneCameraSender.TryMap(pose.camera_frame_id, phoneTip, out indexTip))
                return false;

            string handName = hand.hand ?? "Unknown";
            if (!phoneFilters.TryGetValue(handName, out HandPoseSmoother filter))
            {
                filter = new HandPoseSmoother();
                phoneFilters[handName] = filter;
            }

            // Smooth after the capture pose maps the hand into the room.
            // Camera-relative filtering would mix different phone poses.
            if (!filter.TrySmooth(ref point, ref indexTip, Time.unscaledTime))
                return false;

            IsCalibrated = hasDepth = true;
            return true;
        }

        if (!pose.valid || pose.coordinate_system != "mirrored_camera_m" ||
            !TryVector(pose.point, out Vector3 cameraPoint) ||
            !TryVector(pose.index_tip, out Vector3 cameraTip))
            return false;

        if (!IsCalibrated)
        {
            // Pinching chooses the working hand instead of the first open
            // hand in the packet (which may be holding the phone).
            if (hand.gesture != "PINCH_START" && hand.gesture != "PINCH_HOLD")
                return false;

            // This is a control-volume calibration, not room registration.
            origin = cameraPoint;
            CalibratedHand = hand.hand;
            IsCalibrated = true;
            SpatialWorkspace.LogInteraction("Hand calibrated: " + CalibratedHand +
                "; origin=" + origin.ToString("F3"));
        }

        if (hand.hand != CalibratedHand)
            return false;

        point = MapCameraPoint(cameraPoint);
        indexTip = MapCameraPoint(cameraTip);
        hasDepth = true;
        return true;
    }

    private Vector3 MapCameraPoint(Vector3 point)
    {
        Vector3 local = Vector3.Scale(point - origin, movementScale);

        if (invertDepth)
            local.z = -local.z;

        return transform.TransformPoint(local);
    }

    private static bool TryVector(
        HandDataReceiver.SpatialPoint value,
        out Vector3 point
    )
    {
        point = Vector3.zero;

        if (value == null || !Finite(value.x) ||
            !Finite(value.y) || !Finite(value.z))
            return false;

        point = new Vector3(value.x, value.y, value.z);
        return true;
    }

    private static bool Finite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
