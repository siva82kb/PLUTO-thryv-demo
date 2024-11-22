using System;
using System.Linq;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using UnityEditor.PackageManager;
using UnityEngine;
using System.Globalization;
using System.Data;
using System.Linq;
using UnityEditor.VersionControl;
using UnityEngine.SceneManagement;
using Newtonsoft.Json.Bson;


/*
 * Summary Data Class
 */
public struct DaySummary
{
    public string Day { get; set; }
    public string Date { get; set; }
    public float MoveTime { get; set; }
}


public static class DataManager
{
    public static readonly string directoryPath = Application.dataPath + "/data";
    static string directoryPathConfig;
    public static string directoryPathSession;
    static string directoryPathRawData;
    public static string directoryMechData;
    public static string filePathConfigData { get; private set; }
    public static string filePathSessionData { get; private set; }

    public static void createFileStructure()
    {
        directoryPathConfig = directoryPath + "/configuration";
        directoryPathSession = directoryPath + "/sessions";
        directoryPathRawData = directoryPath + "/rawdata";
        directoryMechData = directoryPath + "/mech";
        filePathConfigData = directoryPath + "/configdata.csv";
        filePathSessionData = directoryPathSession + "/sessions.csv";
        // Check if the directory exists
        if (!Directory.Exists(directoryPath))
        {
            // If not, create the directory
            Directory.CreateDirectory(directoryPath);
            Directory.CreateDirectory(directoryPathConfig);
            Directory.CreateDirectory(directoryPathSession);
            Directory.CreateDirectory(directoryPathRawData);
            File.Create(filePathConfigData).Dispose();
            Debug.Log("Directory created at: " + directoryPath);
        };
    }
    public static DataTable loadCSV(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }
        DataTable dTable = new DataTable();
        var lines = File.ReadAllLines(filePath);
        if (lines.Length == 0) return null;

        // Read the header line to create columns
        var headers = lines[0].Split(',');
        foreach (var header in headers)
        {
            dTable.Columns.Add(header);
        }

        // Read the rest of the data lines
        for (int i = 1; i < lines.Length; i++)
        {
            var row = dTable.NewRow();
            var fields = lines[i].Split(',');
            for (int j = 0; j < headers.Length; j++)
            {
                row[j] = fields[j];
            }
            dTable.Rows.Add(row);
        }
        return dTable;
    }
}
/* Application Level Logger Class */
public enum LogMessageType
{
    INFO,
    WARNING,
    ERROR
}

public static class AppLogger
{
    private static string logFilePath;
    private static StreamWriter logWriter = null;
    private static readonly object logLock = new object();
    public static string currentScene { get; private set; } = "";
    public static string currentMechanism { get; private set; } = "";
    public static string currentGame { get; private set; } = "";

    public static bool isLogging
    {
        get
        {
            return logFilePath != null;
        }
    }

    public static void StartLogging(string scene)
    {
        // Start Log file only if we are not already logging.
        if (isLogging)
        {
            return;
        }
        string logDirectory = Path.Combine(DataManager.directoryPath, "applog");
        if (!Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }
        logFilePath = Path.Combine(logDirectory, $"log-{DateTime.Now:dd-MM-yyyy-HH-mm-ss}.log");
        if (!File.Exists(logFilePath))
        {
            using (File.Create(logFilePath))
            {
                Debug.Log("created");
            }
        }
        logWriter = new StreamWriter(logFilePath, true);
        currentScene = scene;
        LogInfo("Created PLUTO log file.");
    }

    public static void SetCurrentScene(string scene)
    {
        if (isLogging)
        {
            currentScene = scene;
        }
    }

    public static void SetCurrentMechanism(string mechanism)
    {
        if (isLogging)
        {
            currentMechanism = mechanism;
        }
    }

    public static void SetCurrentGame(string game)
    {
        if (isLogging)
        {
            currentGame = game;
        }
    }

    public static void StopLogging()
    {
        if (logWriter != null)
        {
            LogInfo("Closing log file.");
            logWriter.Close();
            logWriter = null;
            logFilePath = null;
            currentScene = "";
        }
    }

    public static void LogMessage(string message, LogMessageType logMsgType)
    {
        lock (logLock)
        {
            if (logWriter != null)
            {
                logWriter.WriteLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} {logMsgType,-7} [{currentScene}] [{currentMechanism}] [{currentGame}] {message}");
                logWriter.Flush();
            }
        }
    }

    public static void LogInfo(string message)
    {
        LogMessage(message, LogMessageType.INFO);
    }

    public static void LogWarning(string message)
    {
        LogMessage(message, LogMessageType.WARNING);
    }

    public static void LogError(string message)
    {
        LogMessage(message, LogMessageType.ERROR);
    }
}



