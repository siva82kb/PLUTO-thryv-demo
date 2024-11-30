using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System;
using System.Linq;
using UnityEngine.UIElements;
using UnityEditorInternal;
using static UnityEditor.LightingExplorerTableColumn;
using static UnityEngine.GraphicsBuffer;
using Unity.VisualScripting;
using UnityEditor.PackageManager;
using System.Runtime.CompilerServices;
using XCharts;
using XCharts.Runtime;

public class Pluto_AAN_SceneHandler : MonoBehaviour
{
    public TextMeshProUGUI textDataDisplay;
    public TextMeshProUGUI textTrialDetails;
    public TextMeshProUGUI textCBAdaptDetailsDisplay;

    // Target and actual circles
    public GameObject targetCircle;
    public GameObject actualCircle;

    // Start/Stop Demo button
    public UnityEngine.UI.Button btnStartStop;

    // Pluto Diagnostics Button
    public UnityEngine.UI.Button btnDiagnsotics;

    // Data logging
    public UnityEngine.UI.Toggle tglDataLog;

    // Control variables
    private bool isRunning = false;
    private float controlTarget = 0.0f;
    private float controlBound = 0.0f;
    private const float tgtDuration = 3.0f;
    private float _currentTime = 0;
    private float _initialTarget = 0;
    private float _finalTarget = 0;
    private bool _changingTarget = false;

    // Discrete movements related variables
    private uint trialNo = 0;
    // Define variables for a discrete movement state machine
    // Enumerated variable for states
    private enum DiscreteMovementTrialState
    {
        Rest,           // Resting state
        SetTarget,      // Set the target
        MovingControl,  // Start Movement control.
        Moving,         // All controls set, just moving.
    }
    private DiscreteMovementTrialState _trialState;
    private static readonly IReadOnlyList<float> stateDurations = Array.AsReadOnly(new float[] {
        2.50f,               // Rest duration
        0.25f,               // Target set duration
        tgtDuration,         // Maximum movement duration
        5.00f - tgtDuration, // Maximum movement duration
    });
    private const float tgtHoldDuration = 1f;
    private float _trialTarget = 0f;
    private float _currTgtForDisplay;
    private float trialDuration = 0f;
    private float stateStartTime = 0f;
    private float _tempIntraStateTimer = 0f;

    // Control bound adaptation variables
    private float prevControlBound = 0.16f;
    // Magical minimum value where the mechanisms mostly move without too much instability.
    private float currControlBound = 0.16f;
    private const float cbChangeDuration = 2.0f;
    private float _currCBforDisplay;
    private int successRate;

    // Target Display Scaling
    private const float xmax = 12f;

    // Logging related variables
    private bool isLogging = false;
    private string logFileName = null;
    private StreamWriter logFile = null;
    private string _dataLogDir = "Assets\\data\\diagnostics\\";

    // Start is called before the first frame update
    void Start()
    {
        // Initialize UI
        InitializeUI();
        // Attach callbacks
        AttachControlCallbacks();
        // Connect to the robot.
        ConnectToRobot.Connect(AppData.COMPort);
        // Get device version.
        PlutoComm.getVersion();
        // First make sure the robot is not in any control mode
        // and set it in the diagnostics mode.
        PlutoComm.setControlType("NONE");
        PlutoComm.setDiagnosticMode();
        // Update the UI when starting
        UpdateUI();
    }

    // Update is called once per frame
    void Update()
    {
        UpdateUI();

        // Update trial detials
        UpdateTrialDetailsDisplay();

        // Update CB adapt details
        UpdateCBAdaptDetailsDisplay();

        // Check if the demo is running.
        if (isRunning == false) return;

        // Update trial time
        trialDuration += Time.deltaTime;

        // Run trial state machine
        RunTrialStateMachine();

        // Load diagnostics scene?
        // Check for left arrow key
        //Debug.Log(Input.GetKeyDown(KeyCode.LeftArrow));
        //if (Input.GetKeyDown(KeyCode.LeftArrow))
        //{
        //    SceneManager.LoadScene("plutoDiagnostiocs");
        //}
        if (Input.anyKeyDown)
        {
            Debug.Log("A key was pressed!");
        }
        if (Input.GetKeyDown(KeyCode.Space))
            Debug.Log("Space key detected!");
        if (Input.GetKeyDown(KeyCode.LeftArrow))
            Debug.Log("Left Arrow Pressed");
        if (Input.GetKeyDown(KeyCode.RightArrow))
            Debug.Log("Right Arrow Pressed");
    }

    void FixedUpdate()
    {
        try
        {
            // Update actual position
            actualCircle.transform.position = new Vector3(
                (2 * PlutoComm.angle / PlutoComm.CALIBANGLE[PlutoComm.mechanism] - 1) * xmax,
                actualCircle.transform.position.y,
                actualCircle.transform.position.z
            );
        }
        catch (Exception ex) { }
    }

    private void RunTrialStateMachine()
    {
        float _deltime = trialDuration - stateStartTime;
        bool _statetimeout = _deltime >= stateDurations[(int)_trialState];
        switch (_trialState)
        {
            case DiscreteMovementTrialState.Rest:
                // Check if the rest time has run out.
                if (_statetimeout)
                {
                    SetTrialState(DiscreteMovementTrialState.SetTarget);
                }
                break;
            case DiscreteMovementTrialState.SetTarget:
                if (_statetimeout)
                {
                    SetTrialState(DiscreteMovementTrialState.MovingControl);
                }
                break;
            case DiscreteMovementTrialState.MovingControl:
                // Update control bound smoothly.
                UpdateControlBoundSmoothly();
                // Update the position control target smoothly.
                UpdatePositionTargetSmoothly();
                // Check if time has run out.
                if (_statetimeout)
                {
                    SetTrialState(DiscreteMovementTrialState.Moving);
                }
                break;
            case DiscreteMovementTrialState.Moving:
                if (_statetimeout)
                {
                    if (successRate >= 0)
                    {
                        successRate = -1;
                    } else
                    {
                        successRate -= 1;
                    }
                    SetTrialState(DiscreteMovementTrialState.Rest);
                }
                else
                {
                    // Check if the target has been reached.
                    if (Math.Abs(_trialTarget - PlutoComm.angle) <= 5.0f)
                    {
                        // Increment the intrastate timer.
                        _tempIntraStateTimer += Time.deltaTime;
                        // Check if target has been held for the required amount of time.
                        if (_tempIntraStateTimer >= tgtHoldDuration)
                        {
                            // Change state to Done.
                            if (successRate < 0)
                            {
                                successRate = 1;
                            }
                            else
                            {
                                successRate += 1;
                            }
                            SetTrialState(DiscreteMovementTrialState.Rest);
                        }

                    } else
                    {
                        // Reset the intrastate timer.
                        _tempIntraStateTimer = 0;
                    }
                }
                break;
        }
    }

    private void SetTrialState(DiscreteMovementTrialState newState)
    {
        _trialState = newState;
        switch (newState)
        {
            case DiscreteMovementTrialState.Rest:
                trialDuration = 0f;
                // Compute the new control bound.
                if (successRate >= 3)
                {
                    prevControlBound = PlutoComm.controlBound;
                    currControlBound = 0.9f * currControlBound;
                }
                else if (successRate < 0)
                {
                    prevControlBound = PlutoComm.controlBound;
                    currControlBound = Math.Min(1.0f, 1.1f * currControlBound);
                }
                trialNo += 1; 
                UpdateCBAdaptDetailsDisplay();
                break;
            case DiscreteMovementTrialState.SetTarget:
                // Random select target from the appropriate range.
                float _tgtscale = UnityEngine.Random.Range(0.0f, 1.0f);
                _trialTarget = _tgtscale * PlutoComm.CALIBANGLE[PlutoComm.mechanism];
                // Change target location.
                targetCircle.transform.position = new Vector3(
                    (2 * _tgtscale - 1) * xmax,
                    targetCircle.transform.position.y,
                    targetCircle.transform.position.z
                );
                break;
            case DiscreteMovementTrialState.MovingControl:
                // Start the position control to the tatget location.
                _initialTarget = PlutoComm.angle;
                _finalTarget = _trialTarget;
                break;
            case DiscreteMovementTrialState.Moving:
                break;
        }
        stateStartTime = trialDuration;
        _tempIntraStateTimer = 0f;
    }

    public void AttachControlCallbacks()
    {
        // Toggle button
        //tglCalibSelect.onValueChanged.AddListener(delegate { OnCalibrationChange(); });
        //tglControlSelect.onValueChanged.AddListener (delegate { OnControlChange(); });
        tglDataLog.onValueChanged.AddListener(delegate { OnDataLogChange(); });

        // Button click.
        btnStartStop.onClick.AddListener(delegate { OnStartStopDemo(); });

        // PLUTO Diagnostics Button click.
        btnDiagnsotics.onClick.AddListener(() => SceneManager.LoadScene(0));

        // Listen to PLUTO's event
        PlutoComm.OnButtonReleased += onPlutoButtonReleased;
        //PlutoComm.OnControlModeChange += onPlutoControlModeChange;
        PlutoComm.OnNewPlutoData += onNewPlutoData;
    }

    private void UpdateControlBoundSmoothly()
    {
        if ((prevControlBound == currControlBound) ||
            ((trialDuration - stateStartTime) >= cbChangeDuration))
        {
            return;
        }
        // Implement the minimum jerk trajectory.
        float _t = (trialDuration - stateStartTime) / cbChangeDuration;
        // Limit _t between 0 and 1.
        _t = Mathf.Clamp(_t, 0, 1);
        // Compute the CB value using the minimum jerk trajectory.
        Debug.Log($"{prevControlBound}, {currControlBound}, {_t}, {_currCBforDisplay}");
        _currCBforDisplay = prevControlBound + (currControlBound - prevControlBound) * (10 * Mathf.Pow(_t, 3) - 15 * Mathf.Pow(_t, 4) + 6 * Mathf.Pow(_t, 5));
        // Update control bound.
        PlutoComm.setControlBound(_currCBforDisplay);
    }

    private void UpdatePositionTargetSmoothly()
    {
        float _t = (trialDuration- stateStartTime) / tgtDuration;
        // Limit _t between 0 and 1.
        _t = Mathf.Clamp(_t, 0, 1);
        // Compute the current target value using the minimum jerk trajectory.
        _currTgtForDisplay = _initialTarget + (_finalTarget - _initialTarget) * (10 * Mathf.Pow(_t, 3) - 15 * Mathf.Pow(_t, 4) + 6 * Mathf.Pow(_t, 5));
        // Update position target
        PlutoComm.setControlTarget(_currTgtForDisplay);
    }

    private void onNewPlutoData()
    {
        // Log data if needed. Else move on.
        if (logFile == null) return;

        // Log data
        String[] rowcomps = new string[]
        {
            $"{PlutoComm.runTime}",
            $"{PlutoComm.packetNumber}",
            $"{PlutoComm.status}",
            $"{PlutoComm.dataType}",
            $"{PlutoComm.errorStatus}",
            $"{PlutoComm.controlType}",
            $"{PlutoComm.calibration}",
            $"{PlutoComm.mechanism}",
            $"{PlutoComm.button}",
            $"{PlutoComm.angle}",
            $"{PlutoComm.torque}",
            $"{PlutoComm.control}",
            $"{PlutoComm.controlBound}",
            $"{PlutoComm.target}",
            $"{PlutoComm.err}",
            $"{PlutoComm.errDiff}",
            $"{PlutoComm.errSum}"
        };
        logFile.WriteLine(String.Join(", ", rowcomps));
    }

    private void OnStartStopDemo()
    {
        if (isRunning)
        {
            btnStartStop.GetComponentInChildren<TMP_Text>().text = "Start Demo";
            isRunning = false;
            // Stop control.
            PlutoComm.setControlType("NONE");
        }
        else
        {
            btnStartStop.GetComponentInChildren<TMP_Text>().text = "Stop Demo";
            isRunning = true;
            // Start the state machine.
            SetTrialState(DiscreteMovementTrialState.Rest);
            // Set Control mode.
            PlutoComm.setControlType("POSITION");
            PlutoComm.setControlBound(currControlBound);
            trialNo = 0;
        }
    }

    private (float currTgtValue, bool isTgtChanging) computeCurrentTarget()
    {
        float _t = (Time.time - _currentTime) / tgtDuration;
        // Limit _t between 0 and 1.
        _t = Mathf.Clamp(_t, 0, 1);
        // Compute the current target value using the minimum jerk trajectory.
        float _currtgt = _initialTarget + (_finalTarget - _initialTarget) * (10 * Mathf.Pow(_t, 3) - 15 * Mathf.Pow(_t, 4) + 6 * Mathf.Pow(_t, 5));
        // return if the final target has been reached.
        return (_currtgt, _t < 1);
    }

    private void OnDataLogChange()
    {
        // Close file.
        closeLogFile(logFile);
        logFile = null;
        // Check what needs to done.
        if (logFileName == null)
        {
            // We are not logging to a file. Start logging.
            logFileName = _dataLogDir + $"logfile_{DateTime.Today:yyyy-MM-dd}.csv";
            logFile = createLogFile(logFileName);
        }
        else
        {
            logFileName = null;
        }
    }

    private StreamWriter createLogFile(string logFileName)
    {
        StreamWriter _sw = new StreamWriter(logFileName, false);
        // Write the header row.
        _sw.WriteLine($"DeviceID = {PlutoComm.deviceId}");
        _sw.WriteLine($"FirmwareVersion = {PlutoComm.version}");
        _sw.WriteLine($"CompileDate = {PlutoComm.compileDate}");
        _sw.WriteLine($"Actuated = {PlutoComm.actuated}");
        _sw.WriteLine($"Start Datetime = {DateTime.Now:yyyy/MM/dd HH-mm-ss.ffffff}");
        _sw.WriteLine("time, packetno, status, datatype, errorstatus, controltype, calibration, mechanism, button, angle, torque, control, controlbound, target, error, errordiff, errorsum");
        return _sw;
    }

    private void closeLogFile(StreamWriter logFile)
    {
        if (logFile != null)
        {
            // Close the file properly and create a new handle.
            logFile.Close();
            logFile.Dispose();
        }
    }

    private void onPlutoButtonReleased()
    {
    }

    private void InitializeUI()
    {
    }

    private void UpdateUI()
    {
        // Update data dispaly
        UpdateDataDispay();

        // Enable/Disable control panel.
        string _mech = PlutoComm.MECHANISMS[PlutoComm.mechanism];
        string _ctrlType = PlutoComm.CONTROLTYPE[PlutoComm.controlType];
    }

    private void UpdateDataDispay()
    {
        // Update the data display.
        string _dispstr = "";
        _dispstr += $"\nTime          : {PlutoComm.currentTime.ToString()}";
        _dispstr += $"\nDev ID        : {PlutoComm.deviceId}";
        _dispstr += $"\nF/W Version   : {PlutoComm.version}";
        _dispstr += $"\nCompile Date  : {PlutoComm.compileDate}";
        _dispstr += $"\n";
        _dispstr += $"\nPacket Number : {PlutoComm.packetNumber}";
        _dispstr += $"\nDev Run Time  : {PlutoComm.runTime:F2}";
        _dispstr += $"\nFrame Rate    : {PlutoComm.frameRate:F2}";
        _dispstr += $"\nStatus        : {PlutoComm.OUTDATATYPE[PlutoComm.dataType]}";
        _dispstr += $"\nControl Type  : {PlutoComm.CONTROLTYPE[PlutoComm.controlType]}";
        _dispstr += $"\nCalibration   : {PlutoComm.CALIBRATION[PlutoComm.calibration]}";
        _dispstr += $"\nError         : {PlutoComm.errorStatus}";
        _dispstr += $"\nMechanism     : {PlutoComm.MECHANISMS[PlutoComm.mechanism]}";
        _dispstr += $"\nActuated      : {PlutoComm.actuated}";
        _dispstr += $"\nButton State  : {PlutoComm.button}";
        _dispstr += "\n";
        _dispstr += $"\nAngle         : {PlutoComm.angle,6:F2} deg";
        if (PlutoComm.MECHANISMS[PlutoComm.mechanism] == "HOC")
        {
            _dispstr += $" [{PlutoComm.getHOCDisplay(PlutoComm.angle),6:F2} cm]";
        }
        _dispstr += $"\nTorque        : {0f,6:F2} Nm";
        _dispstr += $"\nControl       : {PlutoComm.control,6:F2}";
        _dispstr += $"\nControl Bound : {PlutoComm.controlBound,6:F2}"; 
        _dispstr += $"\nTarget        : {PlutoComm.target,6:F2}";
        if (PlutoComm.OUTDATATYPE[PlutoComm.dataType] == "DIAGNOSTICS")
        {
            _dispstr += $"\nError         : {PlutoComm.err,6:F2}";
            _dispstr += $"\nError Diff    : {PlutoComm.errDiff,6:F2}";
            _dispstr += $"\nError Sum     : {PlutoComm.errSum,6:F2}";
        }
        textDataDisplay.SetText(_dispstr);
    }

    private void UpdateTrialDetailsDisplay()
    {
        // Update the trial related data.
        if (isRunning == false)
        {
            textTrialDetails.SetText("No trial running.");
            return;
        }
        string _dispstr = "Trial Details\n";
        _dispstr += "-------------\n";
        _dispstr += $"Duration         : {trialDuration:F2}s";
        _dispstr += $"\nState            : {_trialState}";
        _dispstr += $"\nState Durtation  : {trialDuration - stateStartTime:F2}s";
        if (_trialState == DiscreteMovementTrialState.Rest)
        {
            _dispstr += $"\nTarget           : -";
        } else
        {
            _dispstr += $"\nTarget           : {_trialTarget:F2} [{_currTgtForDisplay:F2}]";
        }
        _dispstr += $"\nControl Bound    : {_currCBforDisplay:F2}";
        
        textTrialDetails.SetText(_dispstr);
    }

    private void UpdateCBAdaptDetailsDisplay()
    {
        // Update the trial related data.
        if (isRunning == false)
        {
            textCBAdaptDetailsDisplay.SetText("No trial running.");
            return;
        }
        string _dispstr = "Control Bound Adpatation Details\n";
        _dispstr += "--------------------------------\n";
        _dispstr += $"Trial No.          : {trialNo}\n";
        _dispstr += $"Success Rate       : {successRate}\n";
        _dispstr += $"Current Ctrl Bound : {currControlBound:F2}\n";
        _dispstr += $"Prev Ctrl Bound    : {prevControlBound:F2}\n";
        textCBAdaptDetailsDisplay.SetText(_dispstr);
    }

    void OnSceneUnloaded(Scene scene)
    {
        Debug.Log("Unloading AAN scene.");
        ConnectToRobot.disconnect();
    }

    private void OnApplicationQuit()
    {
        ConnectToRobot.disconnect();
    }

    public void quitApplication()
    {
        Application.Quit();
    }
}