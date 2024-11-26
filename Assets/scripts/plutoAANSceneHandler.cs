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

public class Pluto_AAN_SceneHandler : MonoBehaviour
{
    public TextMeshProUGUI textDataDisplay;
    
    // Data logging
    public UnityEngine.UI.Toggle tglDataLog;

    // Control variables
    private bool isControl = false;
    private bool _changeSliderLimits = false;
    private float controlTarget = 0.0f;
    private float controlBound = 0.0f;
    private float tgtDuration = 2.0f;
    private float _currentTime = 0;
    private float _initialTarget = 0;
    private float _finalTarget = 0;
    private bool _changingTarget = false;

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
        // Check if there is an changing target being sent to the robot.
        if (_changingTarget)
        {
            // Compute the current target value.
            var _tgt = computeCurrentTarget();
            // Set the current target value.
            controlTarget = _tgt.currTgtValue;
            // Set the changing target flag.
            _changingTarget = _tgt.isTgtChanging;
            // Set the control target.
            PlutoComm.setControlTarget(controlTarget);
        }
        // Udpate UI
        UpdateUI();
    }

    public void AttachControlCallbacks()
    {
        // Toggle button
        //tglCalibSelect.onValueChanged.AddListener(delegate { OnCalibrationChange(); });
        //tglControlSelect.onValueChanged.AddListener (delegate { OnControlChange(); });
        tglDataLog.onValueChanged.AddListener(delegate { OnDataLogChange(); });

        // Button click.
        //btnNextRandomTarget.onClick.AddListener(delegate { OnNextRandomTarget(); });

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

    private void OnNextRandomTarget()
    {
        //// Set initial and final target values.
        //// Initial angle is the current robot angle.
        //_initialTarget = PlutoComm.angle;
        //// Final angle is a random value chosen between 0 and the maximum angle for the current mechanism.
        //_finalTarget = UnityEngine.Random.Range(0, PlutoComm.CALIBANGLE[PlutoComm.mechanism]);
        //// Set the target duration.
        //tgtDuration = float.Parse(inputDuration.text);
        //// Set the current time.
        //_currentTime = Time.time;
        //// Compute current target value.
        //var _tgt = computeCurrentTarget();
        //// Set the current target value.
        //controlTarget = _tgt.currTgtValue;
        //// Set the changing target flag.
        //_changingTarget = _tgt.isTgtChanging;
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

    private void OnApplicationQuit()
    {
        ConnectToRobot.disconnect();
    }

    public void quitApplication()
    {
        Application.Quit();
    }
}