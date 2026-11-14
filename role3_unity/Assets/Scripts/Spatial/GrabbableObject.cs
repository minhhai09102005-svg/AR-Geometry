using UnityEngine;

public class GrabbableObject : MonoBehaviour
{
    public bool Available = true;

    private Vector3 homePosition;
    private Quaternion homeRotation;
    private Vector3 homeScale;
    private Vector3 grabOffset;

    public void RememberHome()
    {
        homePosition = transform.localPosition;
        homeRotation = transform.localRotation;
        homeScale = transform.localScale;
    }

    public float DistanceTo(Vector3 point)
    {
        if (!Available || !gameObject.activeInHierarchy)
            return float.PositiveInfinity;

        float distance = float.PositiveInfinity;

        foreach (Collider target in GetComponentsInChildren<Collider>())
        {
            if (target.enabled)
                distance = Mathf.Min(distance,
                    Vector3.Distance(target.ClosestPoint(point), point));
        }

        return distance;
    }

    public void BeginGrab(Vector3 point)
    {
        grabOffset = transform.position - point;
    }

    public void MoveGrab(Vector3 point)
    {
        transform.position = point + grabOffset;
    }

    public void ReturnHome()
    {
        transform.localPosition = homePosition;
        transform.localRotation = homeRotation;
        transform.localScale = homeScale;
    }
}
