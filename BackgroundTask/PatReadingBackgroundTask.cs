// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

using Windows.ApplicationModel.Background;
using Windows.Devices.Background;
using Windows.Devices.Sensors;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.Media.Playback;
using Windows.Foundation.Collections;
using PlayListManagement;

namespace PatMusic.BackgroundTasks
{
    public sealed class PatReadingBackgroundTask : IBackgroundTask, IDisposable
    {
        private static readonly int MAX_BUFFER_SIZE = 5;
        private static readonly int Y_DATA_COUNT = 4;
        private static readonly double MIN_GRAVITY = 2;
        private static readonly double MAX_GRAVITY = 1200;

        private List<float[]> mAccelDataBuffer = new List<float[]>();
        private List<long> mMagneticFireData = new List<long>();
        private long mLastStepTime = 0;
        private List<Pair> mAccelFireData = new List<Pair>();


        private readonly double w = 0.5;
        private readonly double threshold = 0.6;
        private readonly double limit = 0.15;
        private readonly PlayList playlist = DummyPlayList.Instance;

        private enum PatState { NoPat, SinglePat, DoublePat, Else };
        private enum Mode { Vertical, Horizontal };

        private Accelerometer accelerometer;
        private BackgroundTaskDeferral deferral;
        private ulong sampleCount;
        private List<AccelerometerReading> samples;
        private PatState patState = PatState.NoPat;
        private DispatcherTimer timer;
        private double Mprev = 0.0;
        private int lastDoublePatIdx = 0;

        /// <summary> 
        /// Background task entry point.
        /// </summary> 
        /// <param name="taskInstance"></param>
        public void Run(IBackgroundTaskInstance taskInstance)
        {
            accelerometer = Accelerometer.GetDefault();

            if (null != accelerometer)
            {
                sampleCount = 0;
                samples = new List<AccelerometerReading>();

                // Select a report interval that is both suitable for the purposes of the app and supported by the sensor.
                uint minReportIntervalMsecs = accelerometer.MinimumReportInterval;
                accelerometer.ReportInterval = minReportIntervalMsecs > 16 ? minReportIntervalMsecs : 16;

                // Subscribe to accelerometer ReadingChanged events.
                accelerometer.ReadingChanged += new TypedEventHandler<Accelerometer, AccelerometerReadingChangedEventArgs>(ReadingChanged);

                // Take a deferral that is released when the task is completed.
                deferral = taskInstance.GetDeferral();

                // Get notified when the task is canceled.
                taskInstance.Canceled += new BackgroundTaskCanceledEventHandler(OnCanceled);

                // Store a setting so that the app knows that the task is running.
                ApplicationData.Current.LocalSettings.Values["IsBackgroundTaskActive"] = true;
            }

            

            BackgroundMediaPlayer.MessageReceivedFromBackground += BackgroundMediaPlayer_MessageReceivedFromBackground;
            BackgroundMediaPlayer.MessageReceivedFromForeground += BackgroundMediaPlayer_MessageReceivedFromForeground;
        }

        void BackgroundMediaPlayer_MessageReceivedFromForeground(object sender, MediaPlayerDataReceivedEventArgs e)
        {
            BackgroundMediaPlayer.Current.SetUriSource(new Uri("ms-appx:///Audio/Two Steps From Hell - El Dorado (SkyWorld).mp3"));
            BackgroundMediaPlayer.Current.Play();
        }

        void BackgroundMediaPlayer_MessageReceivedFromBackground(object sender, MediaPlayerDataReceivedEventArgs e)
        {
            throw new NotImplementedException();
        }

        /// <summary> 
        /// Called when the background task is canceled by the app or by the system.
        /// </summary> 
        /// <param name="sender"></param>
        /// <param name="reason"></param>
        private void OnCanceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
        {
            ApplicationData.Current.LocalSettings.Values["TaskCancelationReason"] = reason.ToString();
            ApplicationData.Current.LocalSettings.Values["SampleCount"] = sampleCount;
            ApplicationData.Current.LocalSettings.Values["IsBackgroundTaskActive"] = false;

            // Complete the background task (this raises the OnCompleted event on the corresponding BackgroundTaskRegistration).
            deferral.Complete();
        }

        /// <summary>
        /// Frees resources held by this background task.
        /// </summary>
        public void Dispose()
        {
            if (null != accelerometer)
            {
                accelerometer.ReadingChanged -= new TypedEventHandler<Accelerometer, AccelerometerReadingChangedEventArgs>(ReadingChanged);
                accelerometer.ReportInterval = 0;
            }
        }

        /// <summary>
        /// This is the event handler for acceleroemter ReadingChanged events.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ReadingChanged(object sender, AccelerometerReadingChangedEventArgs e)
        {
            samples.Add(e.Reading);

            //clear old data
            if (samples.Count >= 300) samples = samples.GetRange(1, samples.Count - 1);

            if (patState == PatState.SinglePat || Math.Abs(e.Reading.AccelerationZ) > threshold)
            {
                PatState gesture = ProcessData();

                if (patState == PatState.NoPat && gesture == PatState.SinglePat)
                {
                    patState = gesture;
                    samples.Clear();
                }
                else if (patState == PatState.SinglePat && gesture == PatState.DoublePat)
                {
                    SwitchSong();
                    patState = PatState.NoPat;
                    samples.Clear();
                    sampleCount = 0;
                }
                else if (patState == PatState.SinglePat && sampleCount > 100)
                {
                    PauseSong();
                    patState = PatState.NoPat;
                    samples.Clear();
                    sampleCount = 0;
                }
            }

            if (patState == PatState.SinglePat) sampleCount++;

            // Save the sample count if the foreground app is visible.
            bool appVisible = (bool)ApplicationData.Current.LocalSettings.Values["IsAppVisible"];
            if (appVisible)
            {
                ApplicationData.Current.LocalSettings.Values["SampleCount"] = sampleCount;
            }
        }

        private void PauseSong()
        {
            //int i = 5;
            //BackgroundMediaPlayer.SendMessageToBackground(new ValueSet{ { "play", 0 } });
            bool pause = ApplicationData.Current.LocalSettings.Values["PlayButtonText"].Equals("Pause");
            ApplicationData.Current.LocalSettings.Values["PlayButtonText"] = (pause) ? "Pause" : "Play";
            ApplicationData.Current.LocalSettings.Values["Message"] = (pause) ? (int)MediaControlMessage.Pause : (int)MediaControlMessage.Play;
        }

        private void SwitchSong()
        {
            //int i = 5;
            //BackgroundMediaPlayer.Current.Ne
            ApplicationData.Current.LocalSettings.Values["Message"] = (int)MediaControlMessage.Next;
        }

        private PatState ProcessData()
        {
            int patCount = 0;

            // Map to y
            var y = samples.Select(x => { return x.AccelerationZ; });

            // Map to floor and invert
            var floor = y.Select(x => (Math.Abs(x) > threshold)? Math.Abs(x) : 0).ToList();

            // Moving average
            //double Mprev = 0;
            //int lastSpike = 0;

            foreach (var value in floor)
            {
                double Mk = (1 - w) * Mprev + w * value;

                if ((value - Mk) / Mk > limit)
                {
                    patCount++;
                }
                
                Mprev = Mk;
            }

            //if (patCount > 2) return PatState.Else;

            if (patState == PatState.SinglePat && patCount == 1) return PatState.DoublePat;

            else if (patState == PatState.NoPat && patCount == 1) return PatState.SinglePat;

            else return PatState.Else;
        }

        private void accelDetector(float[] detectedValues, long timeStamp)
        {
            float[] currentValues = new float[3];
            for (int i = 0; i < currentValues.Length; ++i)
            {
                currentValues[i] = detectedValues[i];
            }
            mAccelDataBuffer.Add(currentValues);
            if (mAccelDataBuffer.Count > MAX_BUFFER_SIZE)
            {
                double avgGravity = 0;
                foreach (float[] values in mAccelDataBuffer)
                {
                    avgGravity += Math.Abs(Math.Sqrt(
                            values[0] * values[0] + values[1] * values[1] + values[2] * values[2]) - 0.98);
                }
                avgGravity /= mAccelDataBuffer.Count;

                if (avgGravity >= MIN_GRAVITY && avgGravity < MAX_GRAVITY)
                {
                    mAccelFireData.Add(new Pair(timeStamp, true));
                }
                else
                {
                    mAccelFireData.Add(new Pair(timeStamp, false));
                }

                if (mAccelFireData.Count >= Y_DATA_COUNT)
                {
                    //checkData(mAccelFireData, timeStamp);

                    mAccelFireData.RemoveAt(0);
                }

                mAccelDataBuffer.Clear();
            }
        }

        //private void checkData(List<Pair> accelFireData, long timeStamp)
        //{
        //    bool stepAlreadyDetected = false;
        //    //Iterator<Pair> iterator = accelFireData.iterator();
        //    //while (iterator.hasNext() && !stepAlreadyDetected)
        //    //{
        //    //    stepAlreadyDetected = iterator.next().first.equals(mLastStepTime);
        //    //}
        //    foreach (var item in mAccelFireData)
        //    {
        //        stepAlreadyDetected = iterator.next().first.equals(mLastStepTime);

        //        if (stepAlreadyDetected) break;
        //    }

        //    if (!stepAlreadyDetected)
        //    {
        //        int firstPosition = Collections.binarySearch(mMagneticFireData, accelFireData.get(0).first);
        //        int secondPosition = Collections
        //            .binarySearch(mMagneticFireData, accelFireData.get(accelFireData.size() - 1).first - 1);

        //        if (firstPosition > 0 || secondPosition > 0 || firstPosition != secondPosition)
        //        {
        //            if (firstPosition < 0)
        //            {
        //                firstPosition = -firstPosition - 1;
        //            }
        //            if (firstPosition < mMagneticFireData.Count && firstPosition > 0)
        //            {
        //                mMagneticFireData = new List<long>(
        //                       mMagneticFireData.subList(firstPosition - 1, mMagneticFireData.size()));
        //            }

        //            iterator = accelFireData.iterator();
        //            while (iterator.hasNext())
        //            {
        //                if (iterator.next().second)
        //                {
        //                    mLastStepTime = timeStamp;
        //                    accelFireData.remove(accelFireData.size() - 1);
        //                    accelFireData.add(new Pair(timeStamp, false));
        //                    onStep();
        //                    break;
        //                }
        //            }
        //        }
        //    }
        //}
    }

    public sealed class Pair
    {
        long first;
        bool second;

        public Pair(long first, bool second)
        {
            this.first = first;
            this.second = second;
        }

        public override bool Equals(Object o)
        {
            if (o is Pair) return first.Equals(((Pair) o).first);

            else return false;
        }
    }

    /*
    public class Pedometer
    {
        private int mStepValue = 0;
        private int mPaceValue = 0;
        private float mDistanceValue;
        private float mSpeedValue;
        private int mCaloriesValue;
        private float mDesiredPaceOrSpeed;
        private int mMaintain;
        private bool mIsMetric;
        private float mMaintainInc;

        private bool mIsRunning;
        private StepService mService;

        public Pedometer()
        {
            mStepValue = 0;
            mPaceValue = 0;

            startStepService();
        }

        private void setDesiredPaceOrSpeed(float desiredPaceOrSpeed)
        {
            if (mService != null)
            {
                if (mMaintain == PedometerSettings.M_PACE)
                {
                    mService.setDesiredPace((int)desiredPaceOrSpeed);
                }
                else
                    if (mMaintain == PedometerSettings.M_SPEED)
                    {
                        mService.setDesiredSpeed(desiredPaceOrSpeed);
                    }
            }
        }

        private void startStepService()
        {
            throw new NotImplementedException();
        }
    }
    */
}
