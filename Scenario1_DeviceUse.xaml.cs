// Copyright (c) Microsoft. All rights reserved.

using PlayListManagement;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Devices.Sensors;
using Windows.Foundation;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;



namespace SDKTemplate
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class Scenario1 : Page
    {        
        private MainPage rootPage;

        private Accelerometer _accelerometer;
        private DeviceUseTrigger _deviceUseTrigger;

        // Used to register the device use background task
        private BackgroundTaskRegistration _deviceUseBackgroundTaskRegistration;

        // Used for refreshing the number of samples received when the app is visible
        private DispatcherTimer _refreshTimer;
        private bool playState = false;

        public Scenario1()
        {
            this.InitializeComponent();

            _accelerometer = Accelerometer.GetDefault();
            if (null != _accelerometer)
            {
                // Save trigger so that we may start the background task later.
                // Only one instance of the trigger can exist at a time. Since the trigger does not implement
                // IDisposable, it may still be in memory when a new trigger is created.
                _deviceUseTrigger = new DeviceUseTrigger();

                // Setup a timer to periodically refresh results when the app is visible.
                _refreshTimer = new DispatcherTimer()
                {
                    Interval = new TimeSpan(0, 0, 1) // Refresh once every second
                };
                _refreshTimer.Tick += RefreshTimer_Tick;
            }
            else
            {
                rootPage.NotifyUser("No accelerometer found", NotifyType.StatusMessage);
            }

            ApplicationData.Current.LocalSettings.Values["PlayButtonText"] = "Play";
            BackgroundMediaPlayer.Current.SetUriSource(new Uri("ms-appx:///Audio/Two Steps From Hell - El Dorado (SkyWorld).mp3"));
            BackgroundMediaPlayer.Current.Pause();
        }

        /// <summary>
        /// Invoked when this page is about to be displayed in a Frame.
        /// </summary>
        /// <param name="e">Event data that describes how this page was reached. The Parameter
        /// property is typically used to configure the page.</param>
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            rootPage = MainPage.Current;

            // If the background task is active, start the refresh timer and activate the "Disable" button.
            // The "IsBackgroundTaskActive" state is set by the background task.
            bool isBackgroundTaskActive =
                ApplicationData.Current.LocalSettings.Values.ContainsKey("IsBackgroundTaskActive") &&
                (bool)ApplicationData.Current.LocalSettings.Values["IsBackgroundTaskActive"];

            ScenarioEnableButton.IsEnabled = !isBackgroundTaskActive;
            ScenarioDisableButton.IsEnabled = isBackgroundTaskActive;
            if (isBackgroundTaskActive)
            {
                _refreshTimer.Start();
            }

            // Store a setting for the background task to read
            ApplicationData.Current.LocalSettings.Values["IsAppVisible"] = true;
        }

        /// <summary>
        /// Invoked immediately before the Page is unloaded and is no longer the current source of a parent Frame.
        /// </summary>
        /// <param name="e">
        /// Event data that can be examined by overriding code. The event data is representative
        /// of the navigation that will unload the current Page unless canceled. The
        /// navigation can potentially be canceled by setting Cancel.
        /// </param>
        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            Window.Current.VisibilityChanged -= new WindowVisibilityChangedEventHandler(VisibilityChanged);
            ApplicationData.Current.LocalSettings.Values["IsAppVisible"] = false;

            // The default behavior here is to let the background task continue to run when
            // this scenario exits. The background task can be canceled by clicking on the "Disable"
            // button the next time the app is navigated to.
            // To cancel the background task on scenario exit, uncomment this code.
            // if (null != _deviceUseBackgroundTaskRegistration)
            // {
            //    _deviceUseBackgroundTaskRegistration.Unregister(true);
            //    _deviceUseBackgroundTaskRegistration = null;
            // }

            base.OnNavigatingFrom(e);
        }


        /// <summary>
        /// This is the event handler for VisibilityChanged events.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e">
        /// Event data that can be examined for the current visibility state.
        /// </param>
        private void VisibilityChanged(object sender, VisibilityChangedEventArgs e)
        {
            if (ScenarioDisableButton.IsEnabled)
            {
                ApplicationData.Current.LocalSettings.Values["IsAppVisible"] = e.Visible;

                if (e.Visible)
                {
                    _refreshTimer.Start();
                }
                else
                {
                    _refreshTimer.Stop();
                }
            }
        }

        /// <summary>
        /// This is the click handler for the 'Enable' button.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void ScenarioEnable(object sender, RoutedEventArgs e)
        {
            Window.Current.VisibilityChanged += new WindowVisibilityChangedEventHandler(VisibilityChanged);

            if (null != _accelerometer)
            {
                // Make sure this app is allowed to run background tasks.
                // RequestAccessAsync must be called on the UI thread.
                BackgroundAccessStatus accessStatus = await BackgroundExecutionManager.RequestAccessAsync();

                if ((BackgroundAccessStatus.AllowedWithAlwaysOnRealTimeConnectivity == accessStatus) ||
                    (BackgroundAccessStatus.AllowedMayUseActiveRealTimeConnectivity == accessStatus))
                {
                    if (await StartSensorBackgroundTaskAsync(_accelerometer.DeviceId))
                    {
                        _refreshTimer.Start();

                        ScenarioEnableButton.IsEnabled = false;
                        ScenarioDisableButton.IsEnabled = true;
                    }
                }
                else
                {
                    rootPage.NotifyUser("Background tasks may be disabled for this app", NotifyType.ErrorMessage);
                }
            }
            else
            {
                rootPage.NotifyUser("No accelerometer found", NotifyType.StatusMessage);
            }

            
        }

        /// <summary>
        /// This is the click handler for the 'Disable' button.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ScenarioDisable(object sender, RoutedEventArgs e)
        {
            Window.Current.VisibilityChanged -= new WindowVisibilityChangedEventHandler(VisibilityChanged);

            ScenarioEnableButton.IsEnabled = true;
            ScenarioDisableButton.IsEnabled = false;

            _refreshTimer.Stop();

            if (null != _deviceUseBackgroundTaskRegistration)
            {
                // Cancel and unregister the background task from the current app session.
                _deviceUseBackgroundTaskRegistration.Unregister(true);
                _deviceUseBackgroundTaskRegistration = null;
            }
            else
            {
                // Cancel and unregister the background task from the previous app session.
                FindAndCancelExistingBackgroundTask();
            }

            rootPage.NotifyUser("Background task was canceled", NotifyType.StatusMessage);
        }

        /// <summary>
        /// Starts the sensor background task.
        /// </summary>
        /// <param name="deviceId">Device Id for the sensor to be used by the task.</param>
        /// <param name="e"></param>
        /// <returns>True if the task is started successfully.</returns>
        private async Task<bool> StartSensorBackgroundTaskAsync(String deviceId)
        {
            bool started = false;

            // Make sure only 1 task is running.
            FindAndCancelExistingBackgroundTask();

            // Register the background task.
            var backgroundTaskBuilder = new BackgroundTaskBuilder()
            {
                Name = SampleConstants.Scenario1_TaskName,
                TaskEntryPoint = SampleConstants.Scenario1_TaskEntryPoint
            };

            backgroundTaskBuilder.SetTrigger(_deviceUseTrigger);
            _deviceUseBackgroundTaskRegistration = backgroundTaskBuilder.Register();

            // Make sure we're notified when the task completes or if there is an update.
            _deviceUseBackgroundTaskRegistration.Completed += new BackgroundTaskCompletedEventHandler(OnBackgroundTaskCompleted);

            try
            {
                // Request a DeviceUse task to use the accelerometer.
                DeviceTriggerResult deviceTriggerResult = await _deviceUseTrigger.RequestAsync(deviceId);

                switch (deviceTriggerResult)
                {
                    case DeviceTriggerResult.Allowed:
                        rootPage.NotifyUser("Background task started", NotifyType.StatusMessage);
                        started = true;
                        break;

                    case DeviceTriggerResult.LowBattery:
                        rootPage.NotifyUser("Insufficient battery to run the background task", NotifyType.ErrorMessage);
                        break;

                    case DeviceTriggerResult.DeniedBySystem:
                        // The system can deny a task request if the system-wide DeviceUse task limit is reached.
                        rootPage.NotifyUser("The system has denied the background task request", NotifyType.ErrorMessage);
                        break;

                    default:
                        rootPage.NotifyUser("Could not start the background task: " + deviceTriggerResult, NotifyType.ErrorMessage);
                        break;
                }
            }
            catch (InvalidOperationException)
            {
                // If toggling quickly between 'Disable' and 'Enable', the previous task
                // could still be in the process of cleaning up.
                rootPage.NotifyUser("A previous background task is still running, please wait for it to exit", NotifyType.ErrorMessage);
                FindAndCancelExistingBackgroundTask();
            }

            return started;
        }

        /// <summary>
        /// Finds a previously registered background task for this scenario and cancels it (if present)
        /// </summary>
        private void FindAndCancelExistingBackgroundTask()
        {
            foreach (var backgroundTask in BackgroundTaskRegistration.AllTasks.Values)
            {
                if (SampleConstants.Scenario1_TaskName == backgroundTask.Name)
                {
                    ((BackgroundTaskRegistration)backgroundTask).Unregister(true);
                    break;
                }
            }
        }

        /// <summary>
        /// This is the tick handler for the Refresh timer.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RefreshTimer_Tick(object sender, object e)
        {
            if (ApplicationData.Current.LocalSettings.Values.ContainsKey("SampleCount"))
            {
                ulong sampleCount = (ulong)ApplicationData.Current.LocalSettings.Values["SampleCount"];
                ScenarioOutput_SampleCount.Text = sampleCount.ToString(System.Globalization.CultureInfo.CurrentCulture);

                PlayButton.Content = ApplicationData.Current.LocalSettings.Values["PlayButtonText"];
            }
            else
            {
                ScenarioOutput_SampleCount.Text = "No data";
            }

            if (ApplicationData.Current.LocalSettings.Values.ContainsKey("Message"))
            {
                MediaControlMessage msg = (MediaControlMessage)ApplicationData.Current.LocalSettings.Values["Message"];
                switch (msg)
                {
                    case MediaControlMessage.Play:
                        Play();
                        break;
                    case MediaControlMessage.Pause:
                        Pause();
                        break;
                    case MediaControlMessage.Next:
                        Next_Click(this, null);
                        break;
                    default:
                        break;
                }

                ApplicationData.Current.LocalSettings.Values.Remove("Message");
            }
        }

        /// <summary>
        /// This is the background task completion handler.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void OnBackgroundTaskCompleted(BackgroundTaskRegistration sender, BackgroundTaskCompletedEventArgs e)
        {
            // Dispatch to the UI thread to display the output.
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                // An exception may be thrown if an error occurs in the background task.
                try
                {
                    e.CheckResult();
                    if (ApplicationData.Current.LocalSettings.Values.ContainsKey("TaskCancelationReason"))
                    {
                        string cancelationReason = (string)ApplicationData.Current.LocalSettings.Values["TaskCancelationReason"];
                        rootPage.NotifyUser("Background task was stopped, reason: " + cancelationReason, NotifyType.StatusMessage);
                    }
                }
                catch (Exception ex)
                {
                    rootPage.NotifyUser("Exception in background task: " + ex.Message, NotifyType.ErrorMessage);
                }

                _refreshTimer.Stop();
            });

            // Unregister the background task and let the remaining task finish until completion.
            if (null != _deviceUseBackgroundTaskRegistration)
            {
                _deviceUseBackgroundTaskRegistration.Unregister(false);
                _deviceUseBackgroundTaskRegistration = null;
            }
        }

        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            BackgroundMediaPlayer.Current.SetUriSource(DummyPlayList.Instance.Prev());
            //ForcePlay();
            Play();
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            playState = !((PlayButton.Content = (PlayButton.Content.Equals("Play")) ? "Pause" : "Play").Equals("Play"));
            ApplicationData.Current.LocalSettings.Values["PlayButtonText"] = (playState) ? "Play": "Pause";

            if (playState)
            {
                //BackgroundMediaPlayer.Current.Play();
                Play();
            }
            else
	        {
                //BackgroundMediaPlayer.Current.Pause();
                Pause();
	        }
        }

        private void Next_Click (object sender, RoutedEventArgs e)
        {
            BackgroundMediaPlayer.Current.SetUriSource(DummyPlayList.Instance.Next());
            //ForcePlay();
            Play();
        }

        private void ForcePlay()
        {
            if (!playState)
            {
                playState = true;
                PlayButton.Content = "Pause";
            }
        }

        private void Play()
        {
            playState = true;
            PlayButton.Content = "Pause";
            ApplicationData.Current.LocalSettings.Values["PlayButtonText"] = "Pause";
            BackgroundMediaPlayer.Current.Play();
        }

        private void Pause()
        {
            playState = false;
            PlayButton.Content = "Play";
            ApplicationData.Current.LocalSettings.Values["PlayButtonText"] = "Play";
            BackgroundMediaPlayer.Current.Pause();
        }
    }
}
