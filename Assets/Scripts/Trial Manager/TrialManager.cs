﻿using System;
using System.Collections;
using DotStimulus;
using EyeTracker;
using ScriptableObjects;
using UnityEngine;
using UXF;
using Valve.VR;
using Random = UnityEngine.Random;

namespace Trial_Manager
{
    public class TrialManager : MonoBehaviour
    {
        [SerializeField] private GameObject outerStimulus;
        [SerializeField] private GameObject innerStimulus;
        [SerializeField] private GameObject attentionCue;
        [SerializeField] private GameObject fixationDot;
        [SerializeField] private SessionSettings sessionSettings;
        [SerializeField] private SteamVR_Action_Boolean confirmInputAction;
        [SerializeField] private SteamVR_Action_Vector2 angleSelectAction;
        [SerializeField] private SteamVR_Input_Sources inputSource;
        [SerializeField] private ActiveLaserManager laserManager;
        [SerializeField] private SoundPlayer soundPlayer;
        [SerializeField] private FeedbackModule feedbackModule;
        [SerializeField] private SelectEyeTracker eyeTrackerSelector;
        [SerializeField] private Transform cameraTransform;

        private int _trialCount = 1;

        private AperturePartition _partition;
        private StimulusSettings _innerStimulusSettings;
        private StimulusSettings _outerStimulusSettings;
        private IEnumerator _trialRoutine;

        private float _innerStimMagnitude;
        private float _innerStimAngle;
    
        private bool _inputReceived;
        private bool _waitingForInput;
        private bool _isTrialSuccessful;

        private InputData _userInput;
        private DotManager _innerStimulusManager;
        private IEyeTracker _eyeTracker;
        private StaircaseManager _staircaseManager;

        public void OnEnable()
        {
            InitializeStimuli();
            InitializeFixationDot();
            _eyeTracker = eyeTrackerSelector.ChosenTracker;
            _innerStimulusManager = innerStimulus.GetComponent<DotManager>();
            _partition = new AperturePartition(sessionSettings, _outerStimulusSettings, _innerStimulusSettings);
            _staircaseManager = new StaircaseManager(sessionSettings);

            confirmInputAction[inputSource].onStateUp += GetUserSelection;
        }

        private void InitializeFixationDot()
        {
            var fixationDotRadius = sessionSettings.fixationDotRadius * Mathf.PI / 180 * sessionSettings.stimulusDepth;
            fixationDot.transform.localScale = new Vector3(2.0f * fixationDotRadius, 0.0f, 2.0f * fixationDotRadius);
            fixationDot.transform.localPosition = new Vector3(0.0f, 0.0f, sessionSettings.stimulusDepth);
        }

        public void OnDisable()
        {
            confirmInputAction[inputSource].onStateUp -= GetUserSelection;
        }
        
        // Called via UXF event
        public void BeginTrial(Trial trial)
        {
            _staircaseManager.RandomizeStaircase();
            Debug.Log("CURRENT STAIRCASE: " + _staircaseManager.CurrentStaircaseName());
            Debug.Log("CURRENT STAIRCASE LEVEL: " + _staircaseManager.CurrentStaircase.CurrentStaircaseLevel());
            _isTrialSuccessful = false;
            soundPlayer.PlayStartSound();
            RandomizeInnerStimulus();
            _inputReceived = false;
            
            _trialRoutine = TrialRoutine(trial);
            StartCoroutine(_trialRoutine);
        }

        // Called via UXF event
        public void EndTrial(Trial trial)
        {
            laserManager.DeactivateBothLasers();
            
            if (_inputReceived)
            {
                CalculateOutputs(out var chosenAngle, out var chosenPosition, out var positionError);
                if (positionError < sessionSettings.positionErrorTolerance)
                {
                    if (_staircaseManager.CheckForLocationWin())
                        _isTrialSuccessful = true;
                }
                else
                    _staircaseManager.CheckForLocationLoss();

                if (Math.Abs(chosenAngle - _innerStimulusSettings.correctAngle) < sessionSettings.angleErrorTolerance)
                {
                    if (_staircaseManager.CheckForDirectionWin())
                        _isTrialSuccessful = true;
                }
                else
                    _staircaseManager.CheckForDirectionLoss();
                
                RecordTrialData(trial, chosenAngle, chosenPosition, positionError);
            }

            if (_inputReceived && _trialCount <= sessionSettings.numTrials)
            {
                Session.instance.CurrentBlock.CreateTrial();
                _trialCount++;
            }
        
            StartCoroutine(FeedBackRoutine());
        }

        private void CalculateOutputs(out float chosenAngle, out Vector3 chosenPosition, out float positionError)
        {
            chosenAngle = Mathf.Acos(Vector2.Dot(Vector2.up,
                _userInput.chosenDirection.normalized)) * 180f / Mathf.PI;
            if (_userInput.chosenDirection.x > 0)
                chosenAngle = 360.0f - chosenAngle;

            var innerStimulusPosition = innerStimulus.transform.localPosition;
            chosenPosition = new Vector3(_userInput.selectionLocation.x, _userInput.selectionLocation.y,
                _outerStimulusSettings.stimDepthMeters);

            positionError = Mathf.Acos(Vector3.Dot(innerStimulusPosition.normalized, chosenPosition.normalized)) * 180f /
                            Mathf.PI;
        }

        private void RecordTrialData(Trial trial, float chosenAngle, Vector3 chosenPosition, float positionError)
        {
            trial.result["correct_angle"] = _innerStimulusSettings.correctAngle;
            trial.result["chosen_angle"] = chosenAngle;
            trial.result["correct_position"] = $"({_innerStimMagnitude}, {_innerStimAngle})";
            trial.result["chosen_position"] = CalculateChosenPositionPolar(chosenPosition);
            trial.result["position_error"] = positionError;
            trial.result["coherence_range"] = _innerStimulusSettings.coherenceRange;
            trial.result["position_within_threshold"] = positionError < sessionSettings.positionErrorTolerance;
            trial.result["angle_within_threshold"] = _isTrialSuccessful;
            trial.result["staircase"] = _staircaseManager.CurrentStaircaseName();
        }

        private string CalculateChosenPositionPolar(Vector3 chosenPosition)
        {
            var chosenPosition2d = new Vector2(chosenPosition.x, chosenPosition.y);
            var magnitude = Mathf.Atan(chosenPosition2d.magnitude / sessionSettings.stimulusDepth) * 180f / Mathf.PI;
            var angle = Mathf.Acos(Vector2.Dot(Vector2.up, _userInput.selectionLocation.normalized)) * 180f / Mathf.PI;
            if (chosenPosition.x > 0)
                angle = 360 - angle;
            return $"({magnitude}, {angle})";
        }

        private void InitializeStimuli()
        {
            _innerStimulusSettings = innerStimulus.GetComponent<DotManager>().GetSettings();
            _outerStimulusSettings = outerStimulus.GetComponent<DotManager>().GetSettings();
            _innerStimulusSettings.stimDepthMeters = sessionSettings.stimulusDepth - sessionSettings.stimulusSpacing;
            _outerStimulusSettings.stimDepthMeters = sessionSettings.stimulusDepth;
            _innerStimulusSettings.apertureRadiusDegrees = sessionSettings.innerStimulusRadius;
            _outerStimulusSettings.apertureRadiusDegrees = sessionSettings.outerStimulusRadius;
            _innerStimulusSettings.density = sessionSettings.stimulusDensity;
            _outerStimulusSettings.density = sessionSettings.stimulusDensity;
            _innerStimulusSettings.dotLifetime = sessionSettings.dotLifetime;
            _outerStimulusSettings.dotLifetime = sessionSettings.dotLifetime;
            _innerStimulusSettings.dotSizeArcMinutes = sessionSettings.dotSize;
            _outerStimulusSettings.dotSizeArcMinutes = sessionSettings.dotSize;

            innerStimulus.GetComponent<DotManager>().InitializeWithSettings(_innerStimulusSettings);
            outerStimulus.GetComponent<DotManager>().InitializeWithSettings(_outerStimulusSettings);
        }

        private void GetUserSelection(SteamVR_Action_Boolean action, SteamVR_Input_Sources source)
        {
            if (_waitingForInput)
            {
                StopCoroutine(_trialRoutine);
                innerStimulus.SetActive(false);
                outerStimulus.SetActive(false);
                fixationDot.SetActive(false);

                var selectionLocation =
                    outerStimulus.transform.InverseTransformPoint(laserManager.GetActiveSelectionTransform().position);

                var chosenDirection = angleSelectAction.axis.normalized;
                if (sessionSettings.coarseAdjustEnabled)
                    chosenDirection = DiscretizeInput(chosenDirection);
            
                _userInput = new InputData
                {
                    chosenDirection = chosenDirection,
                    selectionLocation = new Vector2(selectionLocation.x, selectionLocation.z)
                };
                _waitingForInput = false;
                _inputReceived = true;
                Session.instance.CurrentTrial.End();
            }
        }

        private Vector2 DiscretizeInput(Vector2 chosenDirection)
        {
            var angleChoiceList = sessionSettings.choosableAngles;

            var minimumDifference = float.MaxValue;
            var bestChoice = new Vector2();

            foreach (var angleChoice in angleChoiceList)
            {
                var directionChoice = Utility.Rotate2D(Vector2.up, angleChoice);
                var difference = Mathf.Acos(Vector2.Dot(directionChoice, chosenDirection));

                if (difference < minimumDifference)
                {
                    bestChoice = directionChoice;
                    minimumDifference = difference;
                }
            }
            return bestChoice;
        }

        private IEnumerator FeedBackRoutine()
        {
            feedbackModule.GiveFeedback(_inputReceived, _isTrialSuccessful, _userInput);
            yield return new WaitForSeconds(sessionSettings.interTrialDelay);
            feedbackModule.HideFeedback();

            // Redo trial if timed-out
            if(!_inputReceived)
                BeginTrial(Session.instance.CurrentTrial);
            else if (_trialCount <= sessionSettings.numTrials)
                Session.instance.BeginNextTrial();
            else
                Session.instance.End();
        }

        private IEnumerator TrialRoutine(Trial trial)
        {
            fixationDot.SetActive(true);
            yield return WaitForFixation(sessionSettings.fixationTime, 
                Mathf.Tan(sessionSettings.fixationErrorTolerance * Mathf.PI / 180 * sessionSettings.stimulusDepth));

            if (sessionSettings.sessionType == SessionSettings.SessionType.Training)
            {
                attentionCue.SetActive(true);
                yield return new WaitForSeconds(sessionSettings.attentionCueDuration);
                attentionCue.SetActive(false);
            }

            outerStimulus.SetActive(true);
            innerStimulus.SetActive(true);
            yield return new WaitForSeconds(sessionSettings.innerStimulusDuration / 1000);
        
            laserManager.ActivateLaser();
            innerStimulus.SetActive(false);
            fixationDot.SetActive(false);
            _waitingForInput = true;
            yield return new WaitForSeconds((sessionSettings.outerStimulusDuration - sessionSettings.innerStimulusDuration) / 1000);
            
            _waitingForInput = false;
            outerStimulus.SetActive(false);
            trial.End();
        }

        private IEnumerator WaitForFixation(float fixationTime, float maxFixationError)
        {
            var timeFixated = 0.0f;
            while (timeFixated < fixationTime)
            {
                timeFixated += Time.deltaTime;
                if (Physics.Raycast(cameraTransform.position, cameraTransform.TransformDirection(_eyeTracker.GetLocalGazeDirection()), out var hit))
                {
                    Debug.DrawRay(cameraTransform.position, hit.distance * cameraTransform.TransformDirection(_eyeTracker.GetLocalGazeDirection()), Color.yellow);
                    if ((hit.point - fixationDot.transform.position).magnitude > maxFixationError)
                        timeFixated = 0.0f;
                }
                else
                {
                    timeFixated = 0.0f;
                }
                yield return null;
            }
        }
        
        private void RandomizeInnerStimulus()
        {
            var randomPosition = _partition.RandomInnerStimulusPosition(out _innerStimMagnitude, out _innerStimAngle);
            innerStimulus.transform.localPosition =
                new Vector3(randomPosition.x, randomPosition.y, sessionSettings.stimulusDepth - sessionSettings.stimulusSpacing);

            _innerStimulusSettings.correctAngle = sessionSettings.coarseAdjustEnabled
                ? sessionSettings.choosableAngles[Random.Range(0, sessionSettings.choosableAngles.Count)]
                : Random.Range(0.0f, 360.0f);

            _innerStimulusSettings.coherenceRange = _staircaseManager.CurrentStaircase.CurrentStaircaseLevel();
            _innerStimulusManager.InitializeWithSettings(_innerStimulusSettings);
        }
    }
}
