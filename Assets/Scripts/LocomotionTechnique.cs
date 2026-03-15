using System;
using UnityEngine;
using UnityEngine.Serialization;

public class LocomotionTechnique : MonoBehaviour
{
    public Transform hmd;
    
    [Header("Tracking Space (optional)")]
    public Transform trackingSpace;
    public Vector3 boardForwardAxis = Vector3.forward;

    [Header("Board Axes (local to controller)")]
    public Vector3 boardRightAxis   = Vector3.right;   // Pitch-Achse
    
    
    /////////////////////////////////////////////////////////
    // These are for the game mechanism.
    public ParkourCounter parkourCounter;
    public string stage;
    public SelectionTaskMeasure selectionTaskMeasure;
    /////////////////////////////////////////////////////////
    
    [Header("Controllers")]
    public OVRInput.Controller steeringController = OVRInput.Controller.LTouch; // Skateboard

    [Header("Rigidbody")]
    public Rigidbody rb;

    [Header("Steering")]
    public float maxLeanDegrees = 12f;          // Board-Neigung für Voll-Lenkung
    public float steerDeadzone = 0.08f;         // Deadzone für kleine Bewegungen
    public float steerSmoothing = 12f;          // Glättung
    public float maxYawRateDegPerSec = 120f;     // Drehgeschwindigkeit
    

    [Header("Resistance")]
    public float rollingDamping = 0.6f;         // Ausrollwiderstand
    
    [Header("Throttle Tuning")]
    public float maxSpeed = 20f;
    public float accel = 60f;
    
    public float throttleExponent = 0.6f;      // Wie stark Trigger drücken für Beschleunigung
    public float minSpeedWhenPressed = 0f;      // Startgeschwindigkeit
    public bool invertForward = false;

    [Header("Brake (Right Grip Trigger)")]
    public float maxBrakeDecel = 25f;     // Bremskraft
    public float brakeDeadzone = 0.2f;   // Deadzone für zu leichte Betätigung
    public float brakeExponent = 1.0f;    // Wie stark Trigger drücken für das Bremsen


    [Header("Calibrate")]
    public OVRInput.Button calibrateButton = OVRInput.Button.One;

    
    [Header("Drop In / Jump")]
    public float tailLiftAngleDeg = 10f;          // Nose hoch -> armed
    public float tailReleaseAngleDeg = 6f;        // Nose runter -> auslösen
    public float tailMinDownRateDegPerSec = 5f;  // Nötige Geschwindigkeit der Runter-Bewegung
    public float tailCooldown = 0.6f;             // Cooldown

    [FormerlySerializedAs("dropMaxPlanarSpeed")] public float planarSpeedBorder = 0.35f;      // Geschwindigkeitsgrenze: Stillstand -> Drop-In; Bewegung -> Sprung
    public float dropBoostDeltaV = 20.0f;          // Speed-Boost
    public float jumpUpDeltaV = 10.0f;             // Sprung-Boost

    [Header("Steer Lock while tail moving")]
    public float steerLockAngleDeg = 5f;          // Ab Höhe von Nose oben keine Lenkung mehr
    public float steerLockHoldTime = 0.25f;       // noch kurz nach Bewegung locken
    public bool invertTailPitch = true;          // falls Nose hoch negativ ist
    
    
    
    // runtime
    private bool tailArmed = false;             
    private float prevTailPitchDeg = 0f;
    private bool hasPrevTailPitch = false;
    private float lastTailActionTime = -999f;
    private float pendingDropBoost = 0f;  
    private float pendingJumpUp = 0f;     
    private float steerLockedUntil = 0f;
    
    
    private Quaternion steerNeutralLocalRot;
    private bool hasNeutral;
    private float steerSmoothed;
    
    [SerializeField] private bool locomotionLocked = false;
    
    
    void Reset()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Start()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        CalibrateNeutral();
    }
    
    void Update()
    {
        /////////////////////////////////////////////////////////
        // These are for the game mechanism.
        //if (OVRInput.Get(OVRInput.Button.Two) || OVRInput.Get(OVRInput.Button.Four))
        if (OVRInput.Get(OVRInput.Button.Four))
        {
            if (parkourCounter.parkourStart)
            {
                transform.position = parkourCounter.currentRespawnPos;
            }
        }
        /////////////////////////////////////////////////////////

        if (locomotionLocked)
        {
            if (OVRInput.GetDown(calibrateButton))
                CalibrateNeutral();

            return;
        }
        
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        if (OVRInput.GetDown(calibrateButton))
            CalibrateNeutral();

        // ---- Steering input (roll -> steer) ----
        Quaternion currentLocalRot = OVRInput.GetLocalControllerRotation(steeringController);
        if (!hasNeutral)
        {
            steerNeutralLocalRot = currentLocalRot;
            hasNeutral = true;
        }

        
        // Tail Drop und Jump detecten
        
        if (hasNeutral  && Time.time >= ignoreTailUntil)
        {
            float pitch = GetTailPitchDeg(currentLocalRot);
            if (invertTailPitch) pitch = -pitch;

            if (!hasPrevTailPitch)
            {
                prevTailPitchDeg = pitch;
                hasPrevTailPitch = true;
            }

            float pitchRate = (pitch - prevTailPitchDeg) / dt;
            prevTailPitchDeg = pitch;

            // aktueller Fahr-Speed
            float planarSpeed = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up).magnitude;

            // Steering deaktivieren solange Nose hoch/Bewegung aktiv
            if (Mathf.Abs(pitch) >= steerLockAngleDeg)
                steerLockedUntil = Mathf.Max(steerLockedUntil, Time.time + steerLockHoldTime);

            bool cooldownOk = (Time.time - lastTailActionTime) >= tailCooldown;

            // Arm: nur wenn Nose hoch genug
            if (!tailArmed && cooldownOk && pitch >= tailLiftAngleDeg)
            {
                tailArmed = true;
            }

            // Trigger: wenn armed + Nose runter + schnell genug
            if (tailArmed)
            {
                bool released = pitch <= tailReleaseAngleDeg;
                bool droppingFast = pitchRate <= -tailMinDownRateDegPerSec;

                if (released && droppingFast)
                {
                    if (planarSpeed <= planarSpeedBorder)
                        pendingDropBoost += dropBoostDeltaV;     // Drop-In (planar)
                    else
                        pendingJumpUp += jumpUpDeltaV;          // Jump (vertical)

                    lastTailActionTime = Time.time;
                    tailArmed = false;

                    // kurz Steering locken nach dem Snap
                    steerLockedUntil = Mathf.Max(steerLockedUntil, Time.time + steerLockHoldTime);
                }

                // disarm
                if (pitch < 0.5f && Mathf.Abs(pitchRate) < 20f)
                    tailArmed = false;
            }
            
        }
        
        bool steerLocked = Time.time < steerLockedUntil;
        
        float steer = ComputeSteerFromRollZ(currentLocalRot);
        steer = -steer;

        float k = 1f - Mathf.Exp(-steerSmoothing * dt);
        steerSmoothed = Mathf.Lerp(steerSmoothed, steer, k);

        
        if (steerLocked)
            steerSmoothed = 0f;
        
    }
    
    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        float gripRawDbg = Mathf.Clamp01(OVRInput.Get(OVRInput.RawAxis1D.RHandTrigger));
        
        if (locomotionLocked)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            return;
        }

        // Yaw rotation
        float yawRate = steerSmoothed * maxYawRateDegPerSec;
        float newYaw = rb.rotation.eulerAngles.y + yawRate * dt;
        Quaternion desiredRot = Quaternion.Euler(0f, newYaw, 0f);
        rb.MoveRotation(desiredRot);

        // Forward von desiredRot
        Vector3 forward = GetBoardForwardWorld();
        if (invertForward) forward = -forward;


        // Teile velocity in planar + vertical 
        Vector3 vel = rb.linearVelocity;
        Vector3 velPlanar = Vector3.ProjectOnPlane(vel, Vector3.up);
        Vector3 velVertical = vel - velPlanar;

        float newSpeed = velPlanar.magnitude;

        // Beschleunigen mit primary trigger
        float trig = Mathf.Clamp01(OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger));
        bool throttleOn = trig > 0.001f;

        if (throttleOn)
        {
            float shaped = Mathf.Pow(trig, throttleExponent);
            float targetSpeed = Mathf.Lerp(minSpeedWhenPressed, maxSpeed, shaped);
            
            if (targetSpeed > newSpeed)
                newSpeed = Mathf.MoveTowards(newSpeed, targetSpeed, accel * dt);
        }
        else
        {
            // Trigger losgelassen -> nur Reibung / Ausrollen
            newSpeed *= Mathf.Exp(-rollingDamping * dt);
        }
        
        // Drop in boost
        if (pendingDropBoost > 0f)
        {
            newSpeed += pendingDropBoost;
            pendingDropBoost = 0f;
        }
        
        
        // Bremsen mit secondary trigger
        float gripRaw = Mathf.Clamp01(OVRInput.Get(OVRInput.RawAxis1D.RHandTrigger));
        float grip = Mathf.InverseLerp(brakeDeadzone, 1f, gripRaw);
        grip = Mathf.Clamp01(grip);

        if (grip > 0f)
        {
            float brakeFactor = Mathf.Pow(grip, brakeExponent);
            float brakeDecel = brakeFactor * maxBrakeDecel;
            newSpeed = Mathf.MoveTowards(newSpeed, 0f, brakeDecel * dt);
        }

        newSpeed = Mathf.Max(0f, newSpeed);

        
        if (pendingJumpUp > 0f)
        {
            velVertical.y = Mathf.Max(velVertical.y, pendingJumpUp);
            pendingJumpUp = 0f;
        }
        
        
        rb.linearVelocity = velVertical + forward * newSpeed;
        
        
        Vector3 e = rb.rotation.eulerAngles;
        rb.MoveRotation(Quaternion.Euler(0f, e.y, 0f));
        rb.angularVelocity = new Vector3(0f, rb.angularVelocity.y, 0f);
        
    }


    private Vector3 GetBoardForwardWorld()
    {
        Transform ts = trackingSpace != null ? trackingSpace : (hmd != null ? hmd.parent : null);
        Quaternion tsRot = ts != null ? ts.rotation : Quaternion.identity;
        
        Quaternion ctrlLocal = OVRInput.GetLocalControllerRotation(steeringController);

        Vector3 axis = (boardForwardAxis.sqrMagnitude < 1e-6f) ? Vector3.forward : boardForwardAxis;
        
        Vector3 fwd = tsRot * (ctrlLocal * axis);

        // Yaw
        fwd.y = 0f;
        
        if (fwd.sqrMagnitude < 1e-6f)
            fwd = rb != null ? (rb.rotation * Vector3.forward) : Vector3.forward;

        return fwd.normalized;
    }



    private void CalibrateNeutral()
    {
        steerNeutralLocalRot = OVRInput.GetLocalControllerRotation(steeringController);
        hasNeutral = true;
        steerSmoothed = 0f;
        
        // Tail-State reset, damit keine Sprünge durch Kalibrierung entstehen
        tailArmed = false;
        hasPrevTailPitch = false;
    }
    
    private float ignoreTailUntil = 0f;

    private void ResetTailRuntime()
    {
        tailArmed = false;
        hasPrevTailPitch = false;
        pendingDropBoost = 0f;
        pendingJumpUp = 0f;
        steerLockedUntil = 0f;
    }
    
    public void SetLocomotionLocked(bool locked)
    {
        locomotionLocked = locked;

        if (locked)
        {
            ResetTailRuntime();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
        else
        {
            // nach Task kleiner Delay um nicht beim Abstellen zu triggern
            ResetTailRuntime();
            ignoreTailUntil = Time.time + 3.0f;
        }
    }

    private float ComputeSteerFromRollZ(Quaternion currentLocalRot)
    {
        Quaternion rel = Quaternion.Inverse(steerNeutralLocalRot) * currentLocalRot;
        float roll = NormalizeAngle(rel.eulerAngles.z);

        float raw = Mathf.Clamp(roll / Mathf.Max(0.01f, maxLeanDegrees), -1f, 1f);

        float abs = Mathf.Abs(raw);
        if (abs < steerDeadzone) return 0f;

        float sign = Mathf.Sign(raw);
        float t = (abs - steerDeadzone) / Mathf.Max(0.0001f, (1f - steerDeadzone));
        return sign * Mathf.Clamp01(t);
    }

    private static float NormalizeAngle(float degrees)
    {
        degrees %= 360f;
        if (degrees > 180f) degrees -= 360f;
        if (degrees < -180f) degrees += 360f;
        return degrees;
    }
    
    private float GetTailPitchDeg(Quaternion currentLocalRot)
    {
        Quaternion rel = Quaternion.Inverse(steerNeutralLocalRot) * currentLocalRot;

        Vector3 axis = (boardRightAxis.sqrMagnitude < 1e-6f) ? Vector3.right : boardRightAxis.normalized;
        
        Quaternion twist = GetTwist(rel, axis);
        
        float angleRad = 2f * Mathf.Atan2(new Vector3(twist.x, twist.y, twist.z).magnitude, Mathf.Abs(twist.w));
        float angleDeg = angleRad * Mathf.Rad2Deg;
        
        float sign = Mathf.Sign(Vector3.Dot(new Vector3(twist.x, twist.y, twist.z), axis));
        return angleDeg * sign;
    }

    private static Quaternion GetTwist(Quaternion q, Vector3 axis)
    {
        axis.Normalize();
        Vector3 r = new Vector3(q.x, q.y, q.z);
        Vector3 proj = Vector3.Dot(r, axis) * axis;

        Quaternion twist = new Quaternion(proj.x, proj.y, proj.z, q.w);
        return NormalizeQuat(twist);
    }

    private static Quaternion NormalizeQuat(Quaternion q)
    {
        float mag = Mathf.Sqrt(q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w);
        if (mag < 1e-8f) return Quaternion.identity;
        return new Quaternion(q.x/mag, q.y/mag, q.z/mag, q.w/mag);
    }
    
    
    /////////////////////////////////////////////////////////
    void OnTriggerEnter(Collider other)
    {

        // These are for the game mechanism.
        if (other.CompareTag("banner"))
        {
            stage = other.gameObject.name;
            parkourCounter.isStageChange = true;
        }
        else if (other.CompareTag("objectInteractionTask"))
        {
            if (selectionTaskMeasure.IsTaskRunning || selectionTaskMeasure.isCountdown)
                return;
            
            GameObject zoneRoot = other.gameObject;
            selectionTaskMeasure.SetActiveTaskZone(zoneRoot);
            
            //selectionTaskMeasure.isTaskStart = true;
            selectionTaskMeasure.scoreText.text = "";
            selectionTaskMeasure.partSumErr = 0f;
            selectionTaskMeasure.partSumTime = 0f;
            // rotation: facing the user's entering direction
            //float tempValueY = other.transform.position.y > 0 ? 12 : 0;
            //Vector3 tmpTarget = new(hmd.transform.position.x, tempValueY, hmd.transform.position.z);
            //selectionTaskMeasure.taskUI.transform.LookAt(tmpTarget);
            //selectionTaskMeasure.taskUI.transform.Rotate(new Vector3(0, 180f, 0));
            //selectionTaskMeasure.taskStartPanel.SetActive(true);
            
            selectionTaskMeasure.StartOneTask();
        }
        else if (other.CompareTag("coin"))
        {
            CoinRotator coin = other.GetComponentInParent<CoinRotator>();
            if (coin == null) coin = other.GetComponent<CoinRotator>();
            
            if (coin != null)
            {
                if (coin.collected) return;
                coin.collected = true;

                parkourCounter.coinCount += 1;
                GetComponent<AudioSource>().Play();
                other.gameObject.SetActive(false);
            }
            else
            {
                // fallback: falls kein Coin-Script dran ist
                parkourCounter.coinCount += 1;
                GetComponent<AudioSource>()?.Play();
                other.transform.root.gameObject.SetActive(false);
            }
            
            
        }
        // These are for the game mechanism.
    }
    /////////////////////////////////////////////////////////
    
}