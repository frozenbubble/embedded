// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using Windows.UI.Xaml.Controls;

namespace SDKTemplate
{
    public partial class MainPage : Page
    {
        public const string FEATURE_NAME = "Background Sensors";

        List<Scenario> scenarios = new List<Scenario>
        {
            new Scenario() { Title="Accelerometer DeviceUse", ClassType=typeof(Scenario1)},
        };
    }

    public class Scenario
    {
        public string Title { get; set; }
        public Type ClassType { get; set; }
    }

    public struct SampleConstants
    {
        public const string Scenario1_TaskName = "Pat reading BackgroundTask";
        public const string Scenario1_TaskEntryPoint = "PatMusic.BackgroundTasks.PatReadingBackgroundTask";
    }
}