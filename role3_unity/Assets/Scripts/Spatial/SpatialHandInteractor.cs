using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SpatialHandInteractor : MonoBehaviour
{
    public struct HandFrame
    {
        public string hand;
        public string gesture;
        public Vector3 position;
        public Vector3 indexTip;
        public bool hasDepth;
        public bool overButton;
    }

    public static event Action<HandFrame> HandUpdated;
    public static event Action TrackingLost;

    private HandCoordinateMapper mapper;
    private SpatialWorkspace workspace;
    private GameObject cursor;
    private Renderer cursorRenderer;
    private Material openMaterial;
    private Material pinchMaterial;
    private Material contactMaterial;
    private string activeHand;
    private string trackedHand;
    private Vector3 cursorTarget;
    private Vector3 handPosition;
    private float lastPacket;
    private float lastPhoneHand;
    private Button hovered;
    private Button pressed;
    private float nextPressTime;

    private void OnEnable()
    {
        mapper = GetComponent<HandCoordinateMapper>();
        HandDataReceiver.PacketReceived += ReceiveHands;
        HandDataReceiver.ConnectionChanged += OnConnectionChanged;
    }

    private void Start()
    {
        workspace = GetComponent<SpatialWorkspace>();
        openMaterial = workspace.MakeMaterial(new Color(1f, 0.75f, 0.15f));
        pinchMaterial = workspace.MakeMaterial(new Color(0.1f, 0.95f, 0.75f));
        contactMaterial = workspace.MakeMaterial(new Color(0.1f, 0.8f, 1f));

        cursor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        cursor.name = "SpatialHandCursor";
        cursor.transform.SetParent(transform, false);
        cursor.transform.localScale = Vector3.one * 0.014f;
        Destroy(cursor.GetComponent<Collider>());
        cursorRenderer = cursor.GetComponent<Renderer>();
        cursor.SetActive(false);
    }

    private void Update()
    {
        float grace = mapper.UsesPhoneCamera ? 0.3f : 0.6f;
        if (lastPacket > 0f && Time.unscaledTime - lastPacket > grace)
            CancelTracking();

        if (cursor != null && cursor.activeSelf && mapper.UsesPhoneCamera)
        {
            cursor.transform.localScale = Vector3.one *
                (0.014f / Mathf.Max(0.001f, transform.lossyScale.x));
            float amount = 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.035f);
            cursor.transform.position = Vector3.Lerp(cursor.transform.position, cursorTarget, amount);
        }
    }

    private void OnConnectionChanged(bool connected)
    {
        if (!connected)
            CancelTracking();
    }

    public bool TryGetPhoneHandPosition(out Vector3 position)
    {
        position = handPosition;
        return mapper.UsesPhoneCamera && lastPhoneHand > 0f &&
            Time.unscaledTime - lastPhoneHand < 1.5f;
    }

    private void ReceiveHands(HandDataReceiver.HandPacket packet)
    {
        if (packet == null || packet.hands == null ||
            packet.coordinate_system != "normalized_image")
            return;

        HandDataReceiver.HandData hand = null;
        string preferredHand = activeHand ??
            (mapper.UsesPhoneCamera ? trackedHand : mapper.CalibratedHand);

        foreach (HandDataReceiver.HandData candidate in packet.hands)
        {
            if (candidate == null || candidate.interaction == null)
                continue;

            if (mapper.UsesPhoneCamera && activeHand == null &&
                (candidate.gesture == "PINCH_START" || candidate.gesture == "PINCH_HOLD"))
            {
                hand = candidate;
                break;
            }

            if (preferredHand == null || candidate.hand == preferredHand)
            {
                hand = candidate;
                break;
            }
        }

        if (hand == null || !mapper.TryMap(
            hand, out Vector3 point, out Vector3 indexTip, out bool hasDepth))
        {
            // A short MediaPipe gap holds the visual cursor without
            // emitting an old release/placement or changing hands.
            if (!mapper.UsesPhoneCamera || lastPacket <= 0f ||
                Time.unscaledTime - lastPacket > 0.3f)
                CancelTracking();
            return;
        }

        // Synthetic PINCH_END after tracking loss has no spatial pose.
        // Do not place a held object at a stale/flat position in that case.
        if (activeHand != null && hand.spatial == null && mapper.IsCalibrated)
        {
            CancelTracking();
            return;
        }

        bool pinching = hand.gesture == "PINCH_START" ||
            hand.gesture == "PINCH_HOLD";

        if (pinching)
            activeHand = hand.hand;

        lastPacket = Time.unscaledTime;
        trackedHand = hand.hand;
        handPosition = point;
        if (mapper.UsesPhoneCamera)
            lastPhoneHand = Time.unscaledTime;

        if (cursor != null)
        {
            cursorTarget = pinching ? point : indexTip;
            if (!cursor.activeSelf || !mapper.UsesPhoneCamera)
                cursor.transform.position = cursorTarget;
            cursor.SetActive(true);
            bool contact = false;
            foreach (GrabbableObject target in FindObjectsByType<GrabbableObject>(FindObjectsSortMode.None))
            {
                if (target.DistanceTo(cursorTarget) <= 0.035f)
                {
                    contact = true;
                    break;
                }
            }
            cursorRenderer.sharedMaterial = pinching ? pinchMaterial :
                (contact ? contactMaterial : openMaterial);

            if (workspace.ViewCamera != null)
            {
                PhoneDepthOcclusion occlusion = workspace.ViewCamera.GetComponent<PhoneDepthOcclusion>();
                if (occlusion != null)
                    occlusion.ConfigurePointer(cursor);
            }
        }

        HandFrame frame = new HandFrame
        {
            hand = hand.hand,
            gesture = hand.gesture,
            position = point,
            indexTip = indexTip,
            hasDepth = hasDepth
        };

        frame.overButton = UpdateButtons(frame);
        HandUpdated?.Invoke(frame);

        if (!pinching)
            activeHand = null;
    }

    private bool UpdateButtons(HandFrame frame)
    {
        Button contact = null;
        float nearest = 0.035f;

        foreach (Button button in FindObjectsByType<Button>(FindObjectsSortMode.None))
        {
            if (!button.isActiveAndEnabled || !button.IsInteractable())
                continue;

            Canvas canvas = button.GetComponentInParent<Canvas>();
            RectTransform rect = button.transform as RectTransform;

            if (canvas == null || !canvas.isActiveAndEnabled ||
                canvas.renderMode != RenderMode.WorldSpace ||
                rect == null)
                continue;

            Vector3 local = rect.InverseTransformPoint(frame.indexTip);
            float distance = Mathf.Abs(local.z * rect.lossyScale.z);

            if (distance < nearest && rect.rect.Contains(new Vector2(local.x, local.y)))
            {
                nearest = distance;
                contact = button;
            }
        }

        if (contact != hovered)
        {
            SetHover(hovered, false);
            hovered = contact;
            SetHover(hovered, true);
            pressed = null;
        }

        // A held rod/block passing a button must not activate navigation.
        bool canPress = frame.gesture != "PINCH_HOLD" &&
            (frame.hasDepth || frame.gesture == "PINCH_START");

        if (contact != null && contact != pressed && canPress &&
            Time.unscaledTime >= nextPressTime)
        {
            pressed = contact;
            nextPressTime = Time.unscaledTime + 0.4f;
            contact.onClick.Invoke();
        }

        return contact != null;
    }

    private static void SetHover(Button button, bool value)
    {
        if (button == null || EventSystem.current == null)
            return;

        PointerEventData pointer = new PointerEventData(EventSystem.current);

        if (value)
            button.OnPointerEnter(pointer);
        else
            button.OnPointerExit(pointer);
    }

    public void CancelTracking()
    {
        activeHand = null;
        trackedHand = null;
        lastPacket = 0f;
        pressed = null;
        SetHover(hovered, false);
        hovered = null;

        if (cursor != null)
            cursor.SetActive(false);

        TrackingLost?.Invoke();
    }

    private void OnDisable()
    {
        HandDataReceiver.PacketReceived -= ReceiveHands;
        HandDataReceiver.ConnectionChanged -= OnConnectionChanged;
        CancelTracking();
    }
}
