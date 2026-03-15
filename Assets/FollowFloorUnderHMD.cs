using UnityEngine;

public class FollowFloorUnderHMD : MonoBehaviour
{
    public Transform hmd; // CenterEyeAnchor (Child von TrackingSpace)

    void LateUpdate()
    {
        if (!hmd) return;

        var pos = transform.position;
        pos.x = hmd.position.x;
        pos.z = hmd.position.z;
        transform.position = pos; // Y bleibt fix
    }
}