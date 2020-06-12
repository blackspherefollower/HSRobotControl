using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using System.Diagnostics;
using System.IO.Ports;
using Manager;
using IllusionUtility.GetUtility;
using System.Configuration;
using System.Reflection;
using Buttplug4Net35;
using Buttplug4Net35.Messages;

namespace HSRobotControl
{
    public class HSRobotControlPlugin : IllusionPlugin.IPlugin
    {
        public enum CharDiagLevel
        {
            NONE = 0,
            BASIC,
            DISTNACE,
            BONES,
        }

        // Gets the name of the plugin.
        public string Name { get; } = "HSRobotControl";

        /// Gets the version of the plugin.
        public string Version { get; } = "2.0";

        // Stopwatch for ms timing
        private Stopwatch sw = Stopwatch.StartNew();

        // Variables for chara indexing
        private int femaleCount = 0;
        private int femaleIndex = 0;
        private int maleCount = 0;
        private int maleIndex = 0;

        // Configuration variables below from HSRobotControl.dll.config
        private int serialPortBaudRate;
        private string serialPortName;
        private string buttplugUrl;
        private float robotUpdateFrequency;
        private bool autoRange;
        private float autoRangeTime;
        private int charaDiagnostics;
        private bool configDiagnostics;
        private bool hapticDiagnostics;

        // Variables for female chara targeting
        private string[] targetNames;
        private string[] targetBoneNames;
        // targetPriorities schema: "<Closest target found>|<Prioritize as closest target instead>|<Prioritize as closest target instead>|..."
        // So if the leftmost target name string value is the closest female chara target (bone) to the male chara penis (bone)
        // then prioritize any successive target name in the string to the right of the first '|' and chose it over the actual closest female chara target (bone)
        private string[] targetPriorities;
        private float[] targetPriorityRange;
        private float[] targetRangeMin;
        private float[] targetRangeMax;
        private float[] targetAutoRangeValues;
        private float targetPriorityAutoRangeTolerance;
        private Vector3[] targetPositions;
        private float[] targetDistances;
        private float targetDistanceRangeThreshold;

        // Haptics interfaces
        private SerialPort serialPort = null;
        private ButtplugWSClient bpClient = null;

        // Updates the positions based on the distance from the closest female chara's targets (bones) to the chara male's penis (bone)
        // If a female chara target (bone) priority exists and in the target range then it is used instead of the closest target (bone)
        private void UpdatePositions()
        {
            // Find all the female and male chara in the current scene
            var charaManager = Character.Instance;
            var females = charaManager.dictFemale.Values.Where(x => x.animBody != null).Select(x => x as CharInfo);
            var males = charaManager.dictMale.Values.Where(x => x.animBody != null).Select(x => x as CharInfo);

            // Record the female and male chara count
            femaleCount = females.Count();
            maleCount = males.Count();

            /*if (charaDiagnostics >= (int) CharDiagLevel.BASIC)
            {
                Console.WriteLine($"Females: {femaleCount}, Males: {maleCount}");
            }*/

            // If there is at least one female and male chara in the current scene then find the
            // nearest female chara's target (bone) to the male chara's penis target (bone)
            if (femaleCount <= 0 || maleCount <= 0)
            {
                return;
            }

            // Male chara's penis target (bone) position
            var penis = new Vector3
            {
                x = 0.0f,
                y = 0.0f,
                z = 0.0f,
            };

            // Used for index tracking in the foreach loops
            var index = 0;

            // Iterate through the male chara and record the penis target (bone) position of only the desired male chara by index.
            // The index value is changed by pressing the Shift+C button on the keyboard and only changes if there is more than
            // one male chara in the current scene
            foreach (var chara in males)
            {
                if (index == maleIndex)
                {
                    penis = chara.chaBody.objBone.transform.FindLoop("cm_J_dan_s").transform.position;

                    if (charaDiagnostics >= (int)CharDiagLevel.BONES)
                    {
                        Console.WriteLine($"Male chara ({index})'s penis at {penis.x}, {penis.y}, {penis.z}");
                    }
                }

                index++;
            }

            index = 0;

            var minDistance = 999999999.0f;
            var minIndex = 0;

            // Iterate through the female chara and record the target (bone) positions of only the desired female chara by index.
            // The index value is changed by pressing the C button on the keyboard and only changes if there is more than
            // one female chara in the current scene
            foreach (var chara in females)
            {
                if (index != femaleIndex)
                {
                    index++;
                    continue;
                }

                for (var i = 0; i < targetNames.Length; i++)
                {
                    var bones = targetBoneNames[i].Split('|');

                    var bonePosition = new Vector3 {
                        x = 0.0f,
                        y = 0.0f,
                        z = 0.0f,
                    };

                    bonePosition = bones.Aggregate(bonePosition, (current, t) => current + chara.chaBody.objBone.transform.FindLoop(t).transform.position);

                    targetPositions[i] = bonePosition / bones.Length;

                    if (charaDiagnostics >= (int) CharDiagLevel.BONES)
                    {
                        Console.WriteLine(
                            $"Female chara ({index})'s {targetNames[i]} is at {targetPositions[i].x}, {targetPositions[i].y}, {targetPositions[i].z}");
                    }
                    
                    targetDistances[i] = Vector3.Distance(targetPositions[i], penis);

                    if (charaDiagnostics >= (int) CharDiagLevel.DISTNACE)
                    {
                        Console.WriteLine(
                            $"Distance from Female chara ({index})'s {targetNames[i]} to Male chara ({maleIndex})'s penis is {targetDistances[i]}");
                    }

                    if (targetDistances[i] >= minDistance)
                    {
                        continue;
                    }

                    minDistance = targetDistances[i];
                    minIndex = i;
                }

                break;
            }

            var targetAutoRangeValuesMin = 0.0f;
            var targetAutoRangeValuesMax = 0.0f;

            // Find and assign min and max values in targetAutoRangeValues array
            if (autoRange)
            {
                targetAutoRangeValuesMin = targetAutoRangeValues.Min();
                targetAutoRangeValuesMax = targetAutoRangeValues.Max();
            }

            if (charaDiagnostics > (int) CharDiagLevel.DISTNACE)
            {
                Console.WriteLine(
                    $"Female chara ({femaleIndex})'s {targetNames[minIndex]} is the closest to Male chara ({maleIndex})");
            }

            // Priority used flag
            var pFlag = false;

            // If the closest female chara's target (bone) has priority targets and they are in the priority target (bone) distance range 
            // to the male chara's penis target (bone) then use the female chara's priority target (bone) instead
            // targetPriorities schema: "<Closest target found>|<Prioritize as closest target instead>|<Prioritize as closest target instead>|..."
            foreach (var t in targetPriorities)
            {
                // Split target (bone) priorities string
                var priorities = t.Split('|');

                // Check if the current closest female chara's target (bone) has target (bone) priorities
                if (minIndex != Array.IndexOf(targetNames, priorities[0]))
                {
                    continue;
                }

                var minDistancePriority = 999999999.0f;

                // Check if any of the target (bone) priorities are in their acceptable distance ranges and if so select the closest
                for (var p = 1; p < priorities.Length; p++)
                {
                    var pIndex = Array.IndexOf(targetNames, priorities[p]);

                    float priorityRangeMin;
                    float priorityRangeMax;

                    if (autoRange)
                    {
                        priorityRangeMin = targetAutoRangeValuesMin - targetPriorityAutoRangeTolerance;
                        priorityRangeMax = targetAutoRangeValuesMax + targetPriorityAutoRangeTolerance;
                    }
                    else
                    {
                        priorityRangeMin = targetPriorityRange[0];
                        priorityRangeMax = targetPriorityRange[1];
                    }

                    if (targetDistances[pIndex] < priorityRangeMin ||
                        targetDistances[pIndex] > priorityRangeMax ||
                        targetDistances[pIndex] >= minDistancePriority )
                    {
                        continue;
                    }

                    minDistancePriority = targetDistances[pIndex];
                    minIndex = pIndex;
                    pFlag = true;
                }
            }

            // If a female chara's priority target (bone) was found
            if (pFlag && charaDiagnostics >= (int) CharDiagLevel.BASIC)
            {
                Console.WriteLine($"Female chara ({femaleIndex})'s {targetNames[minIndex]} takes priority as the closest to Male chara ({maleIndex})");
            }

            // Shift targetAutoRangeValues array and append the closest new target (bone) distance
            if (autoRange)
            {
                Array.Copy(targetAutoRangeValues, 1, targetAutoRangeValues, 0, targetAutoRangeValues.Length - 1);

                targetAutoRangeValues[targetAutoRangeValues.Length - 1] = targetDistances[minIndex];

                targetAutoRangeValuesMin = targetAutoRangeValues.Min();
                targetAutoRangeValuesMax = targetAutoRangeValues.Max();
            }

            float distanceRangeMin;
            float distanceRangeMax;

            if (autoRange)
            {
                distanceRangeMin = targetAutoRangeValuesMin;
                distanceRangeMax = targetAutoRangeValuesMax;
            }
            else
            {
                distanceRangeMin = targetRangeMin[minIndex];
                distanceRangeMax = targetRangeMax[minIndex];
            }

            if (charaDiagnostics >= (int) CharDiagLevel.BASIC)
            {
                Console.WriteLine($"Distance Range: {distanceRangeMin} to {distanceRangeMax}");
            }

            // If the female chara's target (bone) is in it's distance range to the male chara's penis target (bone)
            if (targetDistances[minIndex] < distanceRangeMin ||
                targetDistances[minIndex] > distanceRangeMax ||
                distanceRangeMax - distanceRangeMin < targetDistanceRangeThreshold)
            {
                return;
            }

            if (charaDiagnostics >= (int) CharDiagLevel.BASIC)
            {
                Console.WriteLine(
                    $"Female chara ({femaleIndex}) is using her {targetNames[minIndex]} on Male chara ({maleIndex}): Distance {targetDistances[minIndex]}");
            }

            UpdateSerial(targetDistances[minIndex], distanceRangeMin, distanceRangeMax);
            UpdateButtplug(targetDistances[minIndex], distanceRangeMin, distanceRangeMax);
        }

        private void UpdateSerial(float distance, float distanceRangeMin, float distanceRangeMax)
        {
            try
            {
                // If serial port is open then send the command to the robot
                if (serialPort?.IsOpen == true)
                {
                    // Serial port robot command schema: "<distance from female target to male's penis> <female target's distance range min> <female target's distance range max>"
                    var command = $"{distance} {distanceRangeMin} {distanceRangeMax}";

                    if (hapticDiagnostics)
                    {
                        Console.WriteLine($"Command: {command}");
                    }

                    serialPort.WriteLine(command);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e}");
            }
        }

        private void UpdateButtplug(float distance, float distanceRangeMin, float distanceRangeMax)
        {
            if (bpClient?.IsConnected != true)
            {
                return;
            }

            var range = distanceRangeMax - distanceRangeMin;
            var offset = distance - distanceRangeMin;
            var pcent = offset / range;
            pcent = Math.Min(Math.Max(pcent, 0), 1);

            try
            {
                foreach (var dev in bpClient.Devices)
                {
                    if (!dev.AllowedMessages.ContainsKey("LinearCmd"))
                    {
                        continue;
                    }

                    if (hapticDiagnostics)
                    {
                        Console.WriteLine($"Sending LinearCmd to {dev.Name} ({dev.Index}): Moving to {pcent} in {robotUpdateFrequency}ms");
                    }

                    var count = dev.AllowedMessages["LinearCmd"].FeatureCount ?? 1;
                    var vectors = new List<LinearCmd.VectorSubcommand>();
                    for(uint i = 0; i < count; i++)
                        vectors.Add(new LinearCmd.VectorSubcommand(i, Convert.ToUInt16(robotUpdateFrequency), pcent));

                    bpClient.SendDeviceMessage(dev, new LinearCmd(dev.Index, vectors));
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e}");
            }
        }

        // Gets invoked when the application is started.
        public void OnApplicationStart()
        {
            try
            {
                // Import and setup configuration variables from HSRobotControl.dll.config
                var appSettings = ConfigurationManager.OpenExeConfiguration(Assembly.GetExecutingAssembly().Location).AppSettings;
                targetNames = appSettings.Settings["targetNames"].Value.Split(',');
                targetBoneNames = appSettings.Settings["targetBoneNames"].Value.Split(',');
                targetPriorities = appSettings.Settings["targetPriorities"].Value.Split(',');

                var values = appSettings.Settings["targetPriorityRange"].Value.Split(',');
                targetPriorityRange = new float[values.Length];
                for (var i = 0; i < values.Length; i++)
                {
                    targetPriorityRange[i] = Convert.ToSingle(values[i]);
                }

                values = appSettings.Settings["targetRangeMin"].Value.Split(',');
                targetRangeMin = new float[values.Length];
                for (var i = 0; i < values.Length; i++)
                {
                    targetRangeMin[i] = Convert.ToSingle(values[i]);
                }

                values = appSettings.Settings["targetRangeMax"].Value.Split(',');
                targetRangeMax = new float[values.Length];
                for (var i = 0; i < values.Length; i++)
                {
                    targetRangeMax[i] = Convert.ToSingle(values[i]);
                }

                targetPriorityAutoRangeTolerance = Convert.ToSingle(appSettings.Settings["targetPriorityAutoRangeTolerance"].Value);
                targetDistanceRangeThreshold = Convert.ToSingle(appSettings.Settings["targetDistanceRangeThreshold"].Value);

                serialPortBaudRate = Convert.ToInt32(appSettings.Settings["serialPortBaudRate"].Value);
                configDiagnostics = Convert.ToBoolean(appSettings.Settings["configDiagnostics"].Value);
                serialPortName = appSettings.Settings["serialPortName"].Value;
                autoRange = Convert.ToBoolean(appSettings.Settings["autoRange"].Value);
                autoRangeTime = Convert.ToSingle(appSettings.Settings["autoRangeTime"].Value);
                robotUpdateFrequency = Convert.ToSingle(appSettings.Settings["updateFrequency"].Value);
                charaDiagnostics = Convert.ToInt16(appSettings.Settings["charaDiagnostics"].Value);
                configDiagnostics = Convert.ToBoolean(appSettings.Settings["configDiagnostics"].Value);
                hapticDiagnostics = Convert.ToBoolean(appSettings.Settings["hapticDiagnostics"].Value);
                buttplugUrl = appSettings.Settings["buttplugUrl"].Value;

                // Setup variables based on current configuration
                var autoRangeLength = (int)(autoRangeTime * robotUpdateFrequency);
                targetAutoRangeValues = new float[autoRangeLength];

                for (var i = 0; i < autoRangeLength; i++)
                {
                    targetAutoRangeValues[i] = targetPriorityRange[1];
                }

                targetPositions = new Vector3[targetNames.Length];
                targetDistances = new float[targetNames.Length];
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e}");
            }
        }

        // Gets invoked when the application is closed.
        public void OnApplicationQuit()
        {

        }

        // Gets invoked whenever a level is loaded.
        public void OnLevelWasLoaded(int level)
        {

        }

        // Gets invoked after the first update cycle after a level was loaded.
        public void OnLevelWasInitialized(int level)
        {

        }

        // Gets invoked on every graphic update.
        public void OnUpdate()
        {
            // Reload config
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.R))
            {
                OnApplicationStart();
            }

            // Open and close the serial port connection when Control+K is pressed on the keyboard
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.K))
            {
                try
                {
                    if (serialPort?.IsOpen == true)
                    {
                        // Close the serial port connection
                        var portName = serialPort.PortName;
                        serialPort.Close();
                        serialPort = null;
                        Console.WriteLine($"Serial port {portName} is closed.");
                    }
                    else
                    {
                        // Open the serial port connection
                        serialPort = new SerialPort(serialPortName, serialPortBaudRate);
                        serialPort.Open();
                        Console.WriteLine($"Serial port {serialPort.PortName} is {(serialPort.IsOpen ? "open":"closed")}.");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error: {e}");
                }
            }

            // Open and close the serial port connection when Control+K is pressed on the keyboard
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.I))
            {
                try
                {
                    if (bpClient?.IsConnected == true)
                    {
                        // Close the serial port connection
                        bpClient.Disconnect();
                        bpClient = null;
                        Console.WriteLine("BP connection is closed.");
                    }
                    else
                    {
                        // Open the serial port connection
                        bpClient = new ButtplugWSClient($"{Name} {Version}");
                        bpClient.Connect(new Uri(buttplugUrl), true);
                        Console.WriteLine($"Connected to Buttplug ser at {buttplugUrl}");

                        if (bpClient.StartScanning().Result)
                        {
                            Console.WriteLine("Buttplug Scanning started");
                        }

                        bpClient.ErrorReceived += (sender, args) => Console.WriteLine($"Buttplug Error: {args.Message}\n{args.Exception}");
                        bpClient.Log += (sender, args) => Console.WriteLine($"Buttplug Event: {args.Message}");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Buttplug Error: {e}");
                }
            }

            // Cycle the female index value based on available female chara in the current scene when Control+C is pressed on the keyboard
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.C))
            {
                if (femaleIndex + 1 < femaleCount)
                {
                    femaleIndex++;
                }
                else
                {
                    femaleIndex = 0;
                }

                if (femaleIndex >= femaleCount)
                {
                    femaleIndex = 0;
                }

                if (charaDiagnostics >= (int) CharDiagLevel.BASIC)
                {
                    Console.WriteLine($"Female chara Index: {femaleIndex} of {femaleCount}");
                }
            }

            // Handles the case when the number of female chara's in the current scene changes
            if (femaleIndex >= femaleCount)
            {
                femaleIndex = 0;
            }

            // Cycle the male index value based on available male chara in the current scene when Shift+C is pressed on the keyboard
            if ((Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && Input.GetKeyDown(KeyCode.C))
            {
                if (maleIndex + 1 < maleCount)
                {
                    maleIndex++;
                }
                else
                {
                    maleIndex = 0;
                }

                if (maleIndex >= maleCount)
                {
                    maleIndex = 0;
                }

                if (charaDiagnostics >= (int) CharDiagLevel.BASIC)
                {
                    Console.WriteLine($"Male chara Index: {maleIndex} of {maleCount}");
                }
            }

            // Handles the case when the number of male chara's in the current scene changes
            if (maleIndex >= maleCount)
            {
                maleIndex = 0;
            }

            // Get ms elapsed since current stopwatch interval
            float msElapsed = sw.ElapsedMilliseconds;

            // If the ms elapsed is greater than the period based on the robot's update frequency then
            // stop the stopwatch, call the robot update function, and restart the stopwatch
            if (msElapsed < robotUpdateFrequency)
            {
                return;
            }

            sw.Stop();

            if (configDiagnostics)
            {
                Console.WriteLine($"Time taken: {msElapsed}ms, Frequency: {msElapsed/1000}Hz");
            }

            UpdatePositions();

            sw = Stopwatch.StartNew();
        }

        // Gets invoked on ever physics update.
        public void OnFixedUpdate()
        {
            
        }
    }
}
