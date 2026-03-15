using System.Collections;
using UnityEngine;
using TMPro;
public class SelectionTaskMeasure : MonoBehaviour
{
    public GameObject targetT;
    public GameObject targetTPrefab;
    Vector3 targetTStartingPos;
    public GameObject objectT;
    public GameObject objectTPrefab;
    Vector3 objectTStartingPos;

    public GameObject taskStartPanel;
    public GameObject donePanel;
    public TMP_Text startPanelText;
    public TMP_Text scoreText;
    public int completeCount;
    public bool isTaskStart;
    public bool isTaskEnd;
    public bool isCountdown;
    public Vector3 manipulationError;
    public float taskTime;
    public ParkourCounter parkourCounter;
    public DataRecording dataRecording;
    private int part;
    public float partSumTime;
    public float partSumErr;
    
    
    // T-Spawn beim Spieler
    public Transform hmdTransform;          // CenterEyeAnchor 
    private GameObject activeTaskZone;
    
    [Header("Reachable target spawn")]
    public float targetDistance = 0.55f;
    public float targetHeightFromEye = -0.25f;
    public float targetSide = 0.15f;
    

    // References
    public TShapeLeftController attacher;
    
    public bool IsTaskRunning => isTaskStart; 
    
    void Start()
    {
        parkourCounter = GetComponent<ParkourCounter>();
        dataRecording = GetComponent<DataRecording>();
        part = 1;
        donePanel.SetActive(false);
        scoreText.text = "Part" + part.ToString();
        taskStartPanel.SetActive(false);
    }
    
    void Update()
    {
        if (isTaskStart)
        {
            // recording time
            taskTime += Time.deltaTime;
        }

        if (isCountdown)
        {
            taskTime += Time.deltaTime;
            startPanelText.text = (3.0 - taskTime).ToString("F1");
        }
    }

    public void SetActiveTaskZone(GameObject zoneGo)
    {
        activeTaskZone = zoneGo;
    }

    
    public void StartOneTask()
    {
        if (isTaskStart || isCountdown) return;
        
        isTaskStart = true;
        isTaskEnd = false;
        
        if (targetT != null) Destroy(targetT);
        if (objectT != null)
        {
            if (attacher != null) attacher.Detach(objectT);
            Destroy(objectT);
        }
        
        // Locomotion sperren
        if (parkourCounter != null && parkourCounter.locomotionTech != null)
            parkourCounter.locomotionTech.SetLocomotionLocked(true);
        
        taskTime = 0f;
        taskStartPanel.SetActive(false);
        donePanel.SetActive(true);
        
        // Spawn target vor dem Spieler in Reichweite
        Vector3 flatForward = Vector3.ProjectOnPlane(hmdTransform.forward, Vector3.up).normalized;
        if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;

        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;
        Vector3 origin = hmdTransform.position + Vector3.up * targetHeightFromEye;
        Vector3 targetPos = origin + flatForward * targetDistance + right * targetSide;

        targetT = Instantiate(targetTPrefab, targetPos, Random.rotation);
        
        // Attach T-Shape an Controller
        objectT = Instantiate(objectTPrefab, attacher.leftAttachPoint.position, attacher.leftAttachPoint.rotation);
        attacher.Attach(objectT);
    }
    
    
    public void EndOneTask()
    {
        if (!isTaskStart) return;
        
        // Locomotion wieder erlauben
        if (parkourCounter != null && parkourCounter.locomotionTech != null)
            parkourCounter.locomotionTech.SetLocomotionLocked(false);
        
        donePanel.SetActive(false);
        
        isTaskEnd = true;
        isTaskStart = false;
        
        // distance error
        manipulationError = Vector3.zero;
        int n = Mathf.Min(targetT.transform.childCount, objectT.transform.childCount);
        for (int i = 0; i < n; i++)
        {
            manipulationError += targetT.transform.GetChild(i).transform.position - objectT.transform.GetChild(i).transform.position;
        }
        scoreText.text = scoreText.text + "Time: " + taskTime.ToString("F1") + ", offset: " + manipulationError.magnitude.ToString("F2") + "\n";
        partSumErr += manipulationError.magnitude;
        partSumTime += taskTime;
        dataRecording.AddOneData(parkourCounter.locomotionTech.stage.ToString(), completeCount, taskTime, manipulationError);
        
        attacher.Detach(objectT);

        Destroy(objectT);
        Destroy(targetT);
        
        if (activeTaskZone != null)
        {
            activeTaskZone.SetActive(false);
            activeTaskZone = null;
        }
        
        StartCoroutine(Countdown(3f));
    }
    
    

    IEnumerator Countdown(float t)
    {
        taskTime = 0f;
        taskStartPanel.SetActive(true);
        isCountdown = true;
        completeCount += 1;

        if (completeCount > 4)
        {
            taskStartPanel.SetActive(false);
            scoreText.text = "Done Part" + part.ToString();
            part += 1;
            completeCount = 0;
        }
        else
        {
            yield return new WaitForSeconds(t);
            isCountdown = false;
            startPanelText.text = "start";
        }
        isCountdown = false;
        yield return 0;
    }
}
