using UnityEngine;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

public static class SpatialTouchInput
{
    // EnhancedTouch records transitions between frames. Keep the first
    // finger until it ends so a second finger cannot take over a grab.
    public static bool Read(
        ref int touchId, out Vector2 point, out TouchPhase phase)
    {
        point = Vector2.zero;
        phase = TouchPhase.None;

        // In the viewer, each eye has its own projection. Direct hand
        // input stays in 3D; screen drags must not use the centre camera.
        if (PhoneHeadsetDisplay.IsActive)
        {
            if (touchId < 0)
                return false;
            touchId = -1;
            phase = TouchPhase.Canceled;
            return true;
        }

        foreach (Touch touch in Touch.activeTouches)
        {
            if (touchId < 0)
            {
                if (touch.phase != TouchPhase.Began)
                    continue;

                touchId = touch.touchId;
            }

            if (touch.touchId != touchId)
                continue;

            point = touch.screenPosition;
            phase = touch.phase;

            if (phase == TouchPhase.Ended || phase == TouchPhase.Canceled)
                touchId = -1;

            return true;
        }

        if (touchId < 0)
            return false;

        touchId = -1;
        phase = TouchPhase.Canceled;
        return true;
    }
}
