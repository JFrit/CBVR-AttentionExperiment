﻿using System;
using System.Windows.Forms.VisualStyles;
using ScriptableObjects;
using UnityEngine;
using UXF;
using Valve.VR;

public class SessionManager : MonoBehaviour
{
    [SerializeField] private SessionSettings settings;
    
    // Variable: trialManager
    // A GameObject containing a TrialManager monobehaviour. Should start inactive.
    [SerializeField] private GameObject trialManager;
    [SerializeField] private SteamVR_Action_Boolean confirmInputAction;
    
    private Block _primaryBlock;
    private bool _sessionStarted;
    private bool _sessionOver;

    public void OnEnable()
    {
        confirmInputAction.onStateDown += StartFirstTrial;
        confirmInputAction.onStateDown += CloseProgram;
    }

    public void OnDisable()
    {
        confirmInputAction.onStateDown -= StartFirstTrial;
        confirmInputAction.onStateDown -= CloseProgram;
    }
    
    // Called via UXF Event
    public void StartSession(Session session)
    {
        settings.LoadFromUxfJson();
        SetSky(settings.skyColor);
        _primaryBlock = session.CreateBlock();
        _primaryBlock.CreateTrial();
        trialManager.SetActive(true);
        _sessionStarted = true;
    }
    
    private static void SetSky(Color skyColor)
    {
        RenderSettings.skybox.color = skyColor;
    }

    private void StartFirstTrial(SteamVR_Action_Boolean action, SteamVR_Input_Sources source)
    {
        if (_sessionStarted)
        {
            Session.instance.BeginNextTrial();
            _sessionStarted = false;
        }
    }
    
    public void StartFirstTrial()
    {
        if (_sessionStarted)
        {
            Session.instance.BeginNextTrial();
            _sessionStarted = false;
        }
    }

    // Called via UXF Event
    public void EndSession()
    {
        _sessionOver = true;
    }

    public void CloseProgram(SteamVR_Action_Boolean action, SteamVR_Input_Sources source)
    {
        if(_sessionOver)
            Application.Quit();
    }
    
    public void CloseProgram()
    {
        if(_sessionOver)
            Application.Quit();
    }

    public void ForceQuit()
    {
        Application.Quit();
    }
}