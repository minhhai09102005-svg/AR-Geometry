using UnityEngine;

public class HandPoseSmoother
{
    private bool initialized;
    private bool pendingJump;
    private float lastTime;
    private Vector3 lastRaw;
    private Vector3 pendingPoint;
    private Vector3 velocity;
    private Vector3 point;
    private Vector3 indexTip;

    public bool TrySmooth(ref Vector3 value, ref Vector3 tip, float now)
    {
        float elapsed = now - lastTime;
        if (!initialized || elapsed > 0.4f || elapsed <= 0f)
        {
            initialized = true;
            pendingJump = false;
            lastTime = now;
            lastRaw = point = value;
            indexTip = tip;
            velocity = Vector3.zero;
            return true;
        }

        // Require another sample before accepting a large one-frame PnP
        // jump. A dropped sample never releases or places a held object.
        if (elapsed < 0.25f && Vector3.Distance(value, lastRaw) > 0.16f)
        {
            if (!pendingJump || Vector3.Distance(value, pendingPoint) > 0.10f)
            {
                pendingJump = true;
                pendingPoint = value;
                return false;
            }
        }

        pendingJump = false;
        velocity = Vector3.Lerp(velocity, (value - lastRaw) / elapsed, Alpha(elapsed, 1f));
        float amount = Alpha(elapsed, 1.8f + 4f * velocity.magnitude);
        point = Vector3.Lerp(point, value, amount);
        indexTip = Vector3.Lerp(indexTip, tip, amount);
        lastRaw = value;
        lastTime = now;
        value = point;
        tip = indexTip;
        return true;
    }

    private static float Alpha(float elapsed, float cutoff)
    {
        return 1f / (1f + 1f / (2f * Mathf.PI * cutoff * elapsed));
    }
}
