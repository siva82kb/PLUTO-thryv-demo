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

public class Pluto_SceneHandler : MonoBehaviour
{
    public TextMeshProUGUI textDataDisplay;
    
    // Calibration
    public UnityEngine.UI.Toggle tglCalibSelect;
    public Dropdown ddCalibMech;
    public TextMeshProUGUI textCalibMessage;
    
    // Control
    public UnityEngine.UI.Toggle tglControlSelect;
    public Dropdown ddControlSelect;
    public TextMeshProUGUI textTarget;
    public UnityEngine.UI.Slider sldrTarget;
    public TextMeshProUGUI textCtrlBound;
    public UnityEngine.UI.Slider sldrCtrlBound;
    public TextMeshProUGUI textReachDur;
    public UnityEngine.UI.Slider sldrReachDur;
    public UnityEngine.UI.Button btnSetTarget;
    public UnityEngine.UI.Button btnResetTarget;

    // AAN Scene Button
    public UnityEngine.UI.Button btnAANDemo;
    
    // Data logging
    public UnityEngine.UI.Toggle tglDataLog;

    // Calibration variables
    private bool isCalibrating = false;
    private enum CalibrationState { WAIT_FOR_ZERO_SET, ZERO_SET, ROM_SET, ERROR, ALL_DONE };
    private CalibrationState calibState = CalibrationState.WAIT_FOR_ZERO_SET;

    // Control variables
    private bool isControl = false;
    private bool _changeSliderLimits = false;
    private float controlBound = 0.0f;

    // Logging related variables
    private bool isLogging = false;
    private string logFileName = null;
    private StreamWriter logFile = null;
    private string _dataLogDir = "Assets\\data\\diagnostics\\";

    // Start is called before the first frame update
    void Start()
    {
        // Ensure the application continues running even when in the background
        Application.runInBackground = true;

        // Initialize UI
        InitializeUI();
        // Attach callbacks
        AttachControlCallbacks();
        // Connect to the robot.
        ConnectToRobot.Connect(AppData.COMPort);
        // Set to diagnostics mode.
        PlutoComm.setDiagnosticMode();
        // Get device version.
        PlutoComm.getVersion();
        // Update the UI when starting
        UpdateUI();
        PlutoComm.setDiagnosticMode();
    }

    // Update is called once per frame
    void Update()
    {
        // Pluto heartbeat
        PlutoComm.sendHeartbeat();

        // Udpate UI
        UpdateUI();

        // Load demo scene?
        // Check for left arrow key
        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            SceneManager.LoadScene("aan_demo");
        }
    }

    public void AttachControlCallbacks()
    {
        // Toggle button
        tglCalibSelect.onValueChanged.AddListener(delegate { OnCalibrationChange(); });
        tglControlSelect.onValueChanged.AddListener (delegate { OnControlChange(); });
        tglDataLog.onValueChanged.AddListener(delegate { OnDataLogChange(); });

        // Dropdown value change.
        ddControlSelect.onValueChanged.AddListener(delegate { OnControlModeChange(); });

        // Slider value change.
        sldrCtrlBound.onValueChanged.AddListener(delegate { OnControlBoundChange(); });

        // Button click.
        btnSetTarget.onClick.AddListener(delegate { PlutoComm.setControlTarget(sldrTarget.value, sldrReachDur.value); });
        btnResetTarget.onClick.AddListener(delegate { PlutoComm.setControlTarget(999, 0); });

        // AAN Demo Button click.
        btnAANDemo.onClick.AddListener(() => SceneManager.LoadScene(1));

        // Listen to PLUTO's event
        PlutoComm.OnButtonReleased += onPlutoButtonReleased;
        PlutoComm.OnControlModeChange += onPlutoControlModeChange;
        PlutoComm.OnNewPlutoData += onNewPlutoData;
    }

    private void onPlutoControlModeChange()
    {
        _changeSliderLimits = true;
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
            $"{PlutoComm.control}",
            $"{PlutoComm.target}",
            $"{PlutoComm.controlBound}",
            $"{PlutoComm.controlDir}",
            $"{PlutoComm.desired}",
            $"{PlutoComm.err}",
            $"{PlutoComm.errDiff}",
            $"{PlutoComm.errSum}"
        };
        logFile.WriteLine(String.Join(", ", rowcomps));
    }

    private void OnControlBoundChange()
    {
        string _mech = PlutoComm.MECHANISMS[PlutoComm.mechanism];
        string _ctrlType = PlutoComm.CONTROLTYPE[PlutoComm.controlType];
        if ((_ctrlType == "POSITION") || (_ctrlType == "POSITIONAAN"))
        {
            controlBound = sldrCtrlBound.value;
            PlutoComm.setControlBound(controlBound);
        }
    }

    private void OnControlModeChange()
    {
        // Send control mode to PLUTO
        PlutoComm.setControlType(PlutoComm.CONTROLTYPE[ddControlSelect.value]);
    }

    private void OnControlChange()
    {
        isControl = tglControlSelect.isOn;
        PlutoComm.setControlType("NONE");
    }

    private void OnCalibrationChange()
    {
        isCalibrating = tglCalibSelect.isOn;
        if (isCalibrating)
        {
            PlutoComm.calibrate("NOMECH");
            calibState = CalibrationState.WAIT_FOR_ZERO_SET;
        }
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
            logFileName = _dataLogDir + $"logfile_{DateTime.Today:yyyy-MM-dd-HH-mm-ss}.csv";
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
        _sw.WriteLine("time, packetno, status, datatype, errorstatus, controltype, calibration, mechanism, button, angle, control, target, controlbound, controldir, desired, error, errordiff, errorsum");
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
        // Check if we are in Calibration Mode.
        if (isCalibrating == false)
        {
            return;
        }
        // Run the calibration state machine
        calibStateMachineOnButtonRelease();
    }

    private void InitializeUI()
    {
        // Fill dropdown list
        ddCalibMech.ClearOptions();
        ddCalibMech.AddOptions(PlutoComm.MECHANISMSTEXT.ToList());
        ddControlSelect.AddOptions(PlutoComm.CONTROLTYPETEXT.ToList());
        // Clear panel selections.
        tglCalibSelect.enabled = true;
        tglCalibSelect.isOn = false;
        tglControlSelect.enabled = true;
        tglControlSelect.isOn = false;
    }

    private void UpdateUI()
    {
        // Update UI Controls.
        ddCalibMech.enabled = tglCalibSelect.enabled && tglCalibSelect.isOn;
        ddControlSelect.enabled = tglControlSelect.enabled && tglControlSelect.isOn;

        // Update data dispaly
        UpdateDataDispay();

        // Check if calibration is in progress, and update UI accordingly.
        tglCalibSelect.enabled = !isControl;
        if (isCalibrating)
        {
            calibStateMachineOnUpdate();
        }
        else
        {
            textCalibMessage.SetText("");
        }

        // Check if control is in progress, and update UI accordingly.
        tglControlSelect.enabled = PlutoComm.MECHANISMS[PlutoComm.mechanism] != "NOMECH" && !isCalibrating;
        textTarget.SetText("Target: ");
        textCtrlBound.SetText("Control Bound: ");
        textReachDur.SetText("Reach Duration: ");
        // Enable/Disable control panel.
        string _mech = PlutoComm.MECHANISMS[PlutoComm.mechanism];
        string _ctrlType = PlutoComm.CONTROLTYPE[PlutoComm.controlType];
        sldrTarget.enabled = (isControl && ((_ctrlType == "TORQUE") || (_ctrlType == "POSITION") || (_ctrlType == "POSITIONAAN")));
        sldrCtrlBound.enabled = isControl && ((_ctrlType == "POSITION") || (_ctrlType == "POSITIONAAN"));
        sldrReachDur.enabled = isControl && ((_ctrlType == "POSITION") || (_ctrlType == "POSITIONAAN"));
        btnSetTarget.enabled = isControl && ((_ctrlType == "POSITION") || (_ctrlType == "POSITIONAAN"));
        if (isControl)
        {
            // Change slider limits if needed.
            if (_changeSliderLimits)
            {
                // Torque controller
                if (_ctrlType == "TORQUE")
                {
                    //sldrTarget.enabled = true;
                    sldrTarget.minValue = (float)-PlutoComm.MAXTORQUE;
                    sldrTarget.maxValue = (float)PlutoComm.MAXTORQUE;
                    sldrTarget.value = 0f;
                    sldrCtrlBound.enabled = false;
                }
                else if ((_ctrlType == "POSITION") || (_ctrlType == "POSITIONAAN"))
                {
                    // Set the appropriate range for the slider.
                    if (_mech == "WFE" || _mech == "WURD" || _mech == "FPS")
                    {
                        sldrTarget.minValue = 0;
                        sldrTarget.maxValue = PlutoComm.CALIBANGLE[PlutoComm.mechanism];
                        sldrTarget.value = PlutoComm.angle;
                    }
                    else
                    {
                        sldrTarget.minValue = PlutoComm.getHOCDisplay(0);
                        sldrTarget.maxValue = PlutoComm.getHOCDisplay(PlutoComm.CALIBANGLE[PlutoComm.mechanism]);
                        sldrTarget.value = PlutoComm.getHOCDisplay(PlutoComm.angle);
                    }
                    // Control Bound slider.
                    sldrCtrlBound.minValue = 0;
                    sldrCtrlBound.maxValue = 1;
                    sldrCtrlBound.value = 0.0f;
                    // Reach Duration.
                    sldrReachDur.value = 0;
                }
                _changeSliderLimits = false;
            }
            // Udpate target value.
            string _unit = (_ctrlType == "TORQUE") ? "Nm" : "deg";
            textTarget.SetText($"Target: {sldrTarget.value,7:F2} {_unit}");
            textCtrlBound.SetText($"Control Bound: {controlBound,7:F2}");
            textReachDur.SetText($"Reach Duration: {sldrReachDur.value,7:F2}");
        }
    }

    private void UpdateDataDispay()
    {
        // Update the data display.
        string _dispstr = $"Time          : {PlutoComm.currentTime.ToString()}";
        _dispstr += $"\nDev ID        : {PlutoComm.deviceId}";
        _dispstr += $"\nF/W Version   : {PlutoComm.version}";
        _dispstr += $"\nCompile Date  : {PlutoComm.compileDate}";
        _dispstr += $"\n";
        _dispstr += $"\nPacket Number : {PlutoComm.packetNumber}";
        _dispstr += $"\nDev Run Time  : {PlutoComm.runTime:F2}";
        _dispstr += $"\nFrame Rate    : {PlutoComm.frameRate:F2}";
        _dispstr += $"\nStatus        : {PlutoComm.OUTDATATYPE[PlutoComm.dataType]}";
        _dispstr += $"\nMechanism     : {PlutoComm.MECHANISMS[PlutoComm.mechanism]}";
        _dispstr += $"\nCalibration   : {PlutoComm.CALIBRATION[PlutoComm.calibration]}";
        _dispstr += $"\nError         : {PlutoComm.errorString}";
        _dispstr += $"\nControl Type  : {PlutoComm.CONTROLTYPE[PlutoComm.controlType]}";
        _dispstr += $"\nActuated      : {PlutoComm.actuated}";
        _dispstr += $"\nButton State  : {PlutoComm.button}";
        _dispstr += "\n";
        _dispstr += $"\nAngle         : {PlutoComm.angle,6:F2} deg";
        if (PlutoComm.MECHANISMS[PlutoComm.mechanism] == "HOC")
        {
            _dispstr += $" [{PlutoComm.getHOCDisplay(PlutoComm.angle),6:F2} cm]";
        }
        _dispstr += $"\nControl       : {PlutoComm.control,6:F2}";
        _dispstr += $"\nCtrl Bnd (Dir): {PlutoComm.controlBound,6:F2} ({PlutoComm.controlDir})";
        _dispstr += $"\nROM midpoint  : {1.0f * PlutoComm.romMidPoint,6:F2}";
        _dispstr += $"\nTarget        : {PlutoComm.target,6:F2}";
        _dispstr += $"\nDesired       : {PlutoComm.desired,6:F2}";
        if (PlutoComm.OUTDATATYPE[PlutoComm.dataType] == "DIAGNOSTICS")
        {
            _dispstr += $"\nError         : {PlutoComm.err,6:F2}";
            _dispstr += $"\nError Diff    : {PlutoComm.errDiff,6:F2}";
            _dispstr += $"\nError Sum     : {PlutoComm.errSum,6:F2}";
        }
        textDataDisplay.SetText(_dispstr);
    }


    /*
     * Calibration State Machine Functions
     */
    private void calibStateMachineOnButtonRelease()
    {
        int _mechInx = ddCalibMech.value + 1;
        // Run the calibration state machine.
        Debug.Log($"{calibState} " + $"{PlutoComm.CALIBRATION[PlutoComm.calibration]}");
        switch (calibState)
        {
            case CalibrationState.WAIT_FOR_ZERO_SET:
                if (PlutoComm.CALIBRATION[PlutoComm.calibration] == "NOCALIB")
                {
                    // Get the current mechanism for calibration.
                    PlutoComm.calibrate(PlutoComm.MECHANISMS[_mechInx]);
                }
                else
                {
                    calibState = CalibrationState.ZERO_SET;
                }
                break;
            case CalibrationState.ZERO_SET:
                if (Math.Abs(PlutoComm.angle) >= 0.9 * PlutoComm.CALIBANGLE[_mechInx] 
                    && Math.Abs(PlutoComm.angle) <= 1.1 * PlutoComm.CALIBANGLE[_mechInx])
                {
                    calibState = CalibrationState.ROM_SET;
                }
                else
                {
                    calibState = CalibrationState.ERROR;
                    PlutoComm.calibrate("NOMECH");
                }
                break;
            case CalibrationState.ROM_SET:
            case CalibrationState.ERROR:
                calibState = CalibrationState.ALL_DONE;
                PlutoComm.setRomMidPointForCurrentMechanism();
                break;
        }
    }

    private void calibStateMachineOnUpdate()
    {
        string _mech = PlutoComm.MECHANISMSTEXT[ddCalibMech.value];
        // Run the calibration state machine.
        switch (calibState)
        {
            case CalibrationState.WAIT_FOR_ZERO_SET:
                textCalibMessage.SetText($"Bring '{_mech}' to zero position, and press PLUTO button TWICE to set zero.");
                break;
            case CalibrationState.ZERO_SET:
                textCalibMessage.SetText($"[{PlutoComm.angle,7:F2}] Zero set. Move to the other extreme position and press PLUTO button to set zero.");
                break;
            case CalibrationState.ROM_SET:
                textCalibMessage.SetText($"'{_mech}' calibrated. Press PLUTO button to exit calibration mode.");
                break;
            case CalibrationState.ERROR:
                textCalibMessage.SetText($"Error in calibration '{_mech}'. Press PLUTO button to exit calibration mode, and try again.");
                break;
            case CalibrationState.ALL_DONE:
                tglCalibSelect.isOn = false;
                break;
        }
    }

    void OnSceneUnloaded(Scene scene)
    {
        Debug.Log("Unloading Diagnostics scene.");
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