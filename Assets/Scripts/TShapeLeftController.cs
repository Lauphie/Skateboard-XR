using System.Collections;
using UnityEngine;

public class TShapeLeftController : MonoBehaviour
{
    public Transform leftAttachPoint;

    public Vector3 localPosOffset = Vector3.zero;
    public Vector3 localRotOffsetEuler = Vector3.zero;

    public bool setKinematicWhileAttached = true;
    public bool disableCollidersWhileAttached = true;
    public bool forceFollowInLateUpdate = true;

    private GameObject attachedObj;
    private Rigidbody attachedRb;
    private Collider[] attachedColliders;

    private Quaternion localRotOffset;

    void Awake()
    {
        localRotOffset = Quaternion.Euler(localRotOffsetEuler);
    }

    void OnValidate()
    {
        localRotOffset = Quaternion.Euler(localRotOffsetEuler);
    }

    public void Attach(GameObject obj)
    {
        if (obj == null) return;
        if (leftAttachPoint == null)
        {
            Debug.LogError("TShapeLeftController: leftAttachPoint ist NICHT gesetzt!");
            return;
        }

        attachedObj = obj;
        attachedRb = obj.GetComponent<Rigidbody>();
        attachedColliders = obj.GetComponentsInChildren<Collider>(true);

        if (attachedRb != null && setKinematicWhileAttached)
        {
            attachedRb.isKinematic = true;
            attachedRb.detectCollisions = false;
            attachedRb.linearVelocity = Vector3.zero;
            attachedRb.angularVelocity = Vector3.zero;
        }

        if (disableCollidersWhileAttached && attachedColliders != null)
            foreach (var c in attachedColliders) c.enabled = false;
        
        obj.transform.SetParent(leftAttachPoint, false);

        ApplyLocalSnap();
    }

    public void Detach(GameObject obj)
    {
        if (attachedObj == null) return;
        if (obj != attachedObj) return;

        if (disableCollidersWhileAttached && attachedColliders != null)
            foreach (var c in attachedColliders) c.enabled = true;

        if (attachedRb != null && setKinematicWhileAttached)
        {
            attachedRb.isKinematic = false;
            attachedRb.detectCollisions = true;
        }

        attachedObj.transform.SetParent(null, true);

        attachedObj = null;
        attachedRb = null;
        attachedColliders = null;
    }

    void LateUpdate()
    {
        if (!forceFollowInLateUpdate) return;
        if (attachedObj == null || leftAttachPoint == null) return;

        ApplyLocalSnap();
    }

    private void ApplyLocalSnap()
    {
        attachedObj.transform.localPosition = localPosOffset;
        attachedObj.transform.localRotation = localRotOffset;
    }
    
    void OnApplicationPause(bool paused)
    {
        if (!paused && attachedObj != null)
            StartCoroutine(ResnapAfterResume());
    }

    IEnumerator ResnapAfterResume()
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        if (attachedObj != null) ApplyLocalSnap();
    }
}
