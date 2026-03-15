using UnityEngine;

public class TShapeRightController : MonoBehaviour
{
    public OVRInput.Controller controller = OVRInput.Controller.RTouch;
    public OVRInput.Button finishButton = OVRInput.Button.Two; // B

    public SelectionTaskMeasure selectionTaskMeasure;

    void Update()
    {
        if (selectionTaskMeasure == null) return;

        if (OVRInput.GetDown(finishButton, controller))
        {
            selectionTaskMeasure.EndOneTask();
        }
    }
}