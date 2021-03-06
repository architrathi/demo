﻿using System;
using ActionStreetMap.Infrastructure.IO;
using ActionStreetMap.Infrastructure.Reactive;
using Assets.Scripts.Console;
using Assets.Scripts.Console.Utils;
using Assets.Scripts.Demo;
using ActionStreetMap.Core;
using ActionStreetMap.Core.Scene;
using ActionStreetMap.Explorer;
using ActionStreetMap.Explorer.Interactions;
using ActionStreetMap.Infrastructure.Dependencies;
using ActionStreetMap.Infrastructure.Diagnostic;
using UnityEngine;

using RecordType = ActionStreetMap.Infrastructure.Diagnostic.DefaultTrace.RecordType;

namespace Assets.Scripts.Character
{
    public class ActionStreetMapBehavior : MonoBehaviour
    {
        /// <summary> This is start coordinate corresponds to World (0,0,0). </summary>
        private GeoCoordinate StartPosition = new GeoCoordinate(55.75282, 37.62259);

        private IPositionObserver<MapPoint> _positionObserver;

        private ITrace _trace;

        private bool _isInitialized = false;

        private Vector3 _position = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        private IMessageBus _messageBus;

        private DebugConsole _console;

        private Address _currentAddress;

        // Use this for initialization
        private void Start()
        {
            Initialize();
        }

        // Update is called once per frame
        private void Update()
        {
            if (_isInitialized && _position != transform.position)
            {
                _position = transform.position;
                Scheduler.ThreadPool.Schedule(() => 
                    _positionObserver.OnNext(new MapPoint(_position.x, _position.z, _position.y)));
            }
        }

        #region Initialization

        private void Initialize()
        {
            // Setup main thread scheduler
            Scheduler.MainThread = new UnityMainThreadScheduler();

            // Create and register DebugConsole inside Container
            var container = new Container();

            // Create message bus class which is way to listen for ASM events
            _messageBus = new MessageBus();

            // Console is way to debug/investigate app behavior on real devices when 
            // regular debugger is not applicable
            InitializeConsole(container);

            // Attach address locator which provides the way to get current address
            AttachAddressLocator();

            // ASM should be started from non-UI thread
            Scheduler.ThreadPool.Schedule(() =>
            {
                try
                {
                    // NOTE These services should be registered inside container before GameRunner is constructed.

                    // Trace implementation
                    container.RegisterInstance(_trace);
                    // Path resolver which knows about current platform
                    container.RegisterInstance<IPathResolver>(new WinPathResolver());
                    // Message bus
                    container.RegisterInstance(_messageBus);

                    // Create ASM entry point with settings provided and register custom plugin which adds 
                    // custom logic or replaces default one
                    var gameRunner = new GameRunner(container, @"Config/settings.json")
                        .RegisterPlugin<DemoBootstrapper>("demo", _messageBus, _trace);
                    
                    // Store position observer which will listen for character movements
                    _positionObserver = gameRunner;

                    // Run ASM logic
                    gameRunner.RunGame(StartPosition);

                    _isInitialized = true;
                }
                catch (Exception ex)
                {
                    _console.LogMessage(new ConsoleMessage("Error running game:" + ex, RecordType.Error, Color.red));
                    throw;
                }
            });
        }

        private void InitializeConsole(IContainer container)
        {
            // NOTE DebugConsole is based on some adapted solution found in Internet
            var consoleGameObject = new GameObject("_DebugConsole_");
            _console = consoleGameObject.AddComponent<DebugConsole>();
            container.RegisterInstance(_console);
            // that is not nice, but we need to use commands registered in DI with their dependencies
            _console.SetContainer(container);
           
            _trace = new DebugConsoleTrace(_console);
            _console.IsOpen = true;

            UnityMainThreadDispatcher.RegisterUnhandledExceptionCallback(ex => 
                _trace.Error("FATAL", ex, "Unhandled exception"));
        }

        private void AttachAddressLocator()
        {
            _messageBus
                .AsObservable<GameStarted>()
                .ObserveOnMainThread()
                .Subscribe(_ =>
                {
                    gameObject.AddComponent<AddressLocatorBehaviour>()
                        .SetCommandController(_console.CommandController)
                        .GetObservable()
                        .Subscribe(address => { _currentAddress = address; });
                });
        }

        #endregion

        private void OnGUI()
        {
            var address = _currentAddress;
            if (address != null)
            {
                String addressString = address.Street;

                if (!String.IsNullOrEmpty(address.Name))
                    addressString += String.Format(", {0}", address.Name);

                if (!String.IsNullOrEmpty(address.Code))
                    addressString += String.Format(", {0}", address.Code);

                GUI.Box(new Rect(0, 0, 400, 30), addressString);
            }
        }
    }
}
