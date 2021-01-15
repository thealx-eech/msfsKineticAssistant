using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CTrue.FsConnect;
using Microsoft.FlightSimulator.SimConnect;
using Newtonsoft.Json;
using System.Windows.Input;
using System.IO;
using System.Windows.Controls;
using System.Reflection;
using Microsoft.Win32;
//using System.Speech.Synthesis;

//TAILHOOK POSITION:1#percent/
//LIGHT RECOGNITION#  percent

//(A:LAUNCHBAR POSITION:1, percent)/
//(A:LIGHT WING,           percent)/

//(A:LAUNCHBAR POSITION, percent)/
//(A:LIGHT WING,         percent)/

//(A:TOW RELEASE HANDLE, percent)/
//(A:LIGHT WING,         percent)/

//FOLDING WING LEFT PERCENT
//FOLDING WING RIGHT PERCENT

namespace MSFS_Kinetic_Assistant
{
    public partial class MainWindow : System.Windows.Window
    {
        public FsConnect _fsConnect;
        public PlaneInfoResponse _planeInfoResponse;
        public PlaneInfoResponse _planeInfoResponseLast;
        public PlaneInfoCommit _planeCommit;
        public PlaneInfoCommit _planeCommitLast;
        public PlaneInfoRotate _planeRotate;
        public Dictionary<uint, winchPosition> _nearbyInfoResponse;
        public Dictionary<uint, winchPosition> _nearbyInfoResponseLast;
        public Dictionary<uint, winchPosition> _nearbyInfoResponsePreLast;

        // IF NOT NULL - READY TO LAUNCH
        public winchPosition _winchPosition = null;
        public winchPosition _carrierPosition = null;
        public List<winchPosition> thermalsList = new List<winchPosition>();

        // IF NOT 0 - WINCH WORKING
        public double launchTime = 0;
        public double cableLength = 0;

        // IF NOT 0 - ARRESTER CONNECTED
        public double arrestorConnectedTime = 0;

        // IF NOT 0 - CATAPULT LAUNCH IN PROCESS
        public double targedCatapultVelocity = 0;

        // IF NOT FALSE - THERMALS CALCULATION IN PROCESS
        public bool thermalsWorking = false;

        // IF NOT NULL - TOWING ACTIVE
        public uint towingTarget = 99999999;
        public double towCableLength = 0;
        bool towingScanning = false;

        bool winchAbortInitiated = false;
        bool launchpadAbortInitiated = false;
        bool arrestorsAbortInitiated = false;

        public double absoluteTime;
        public double swLast;
        public double lastFrameTiming;
        DispatcherTimer dispatcherTimer;
        public double lastPreparePressed = 0;

        public bool loaded = false;
        MathClass _mathClass;

        Dictionary<string, double> assistantSettings;
        Dictionary<string, double> controlTimestamps;

        MediaPlayer soundPlayer = null;

        public MainWindow()
        {
            DataContext = new SimvarsViewModel();
            _mathClass = new MathClass();
            assistantSettings = new Dictionary<string, double>();

            _nearbyInfoResponse = new Dictionary<uint, winchPosition>();
            _nearbyInfoResponseLast = new Dictionary<uint, winchPosition>();
            _nearbyInfoResponsePreLast = new Dictionary<uint, winchPosition>();


            // PREPARE CONTROLS DATA
            controlTimestamps = new Dictionary<string, double>();
            foreach (var field in typeof(PlaneInfoResponse).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (field.FieldType == typeof(double))
                    controlTimestamps.Add(field.Name, 0);

            InitializeComponent();

            this.Title = ((AssemblyTitleAttribute)Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyTitleAttribute), false)).Title + " " +
            Assembly.GetExecutingAssembly().GetName().Version.ToString();

            // COMMON SIM VALUES REQUEST TIMER
            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 250);
            dispatcherTimer.Tick += new EventHandler(commonInterval);
            dispatcherTimer.Start();

            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 50);
            dispatcherTimer.Tick += new EventHandler(launchInterval);
            dispatcherTimer.Start();

            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 1);
            dispatcherTimer.Tick += new EventHandler(nearbyInterval);
            dispatcherTimer.Start();

            loadSettings();
            loaded = true;
        }
        private void Window_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            var window = (Window)sender;
            window.Topmost = assistantSettings["alwaysOnTop"] == 1;
        }
        public void commonInterval(object sender, EventArgs e)
        {
            if (_fsConnect != null)
            {
                if (_winchPosition == null && _carrierPosition == null && targedCatapultVelocity == 0 && !thermalsWorking && towingTarget == 99999999)
                    _fsConnect.RequestData(Requests.PlaneInfo, Definitions.PlaneInfo);

                if (towingScanning && towingTarget == 99999999)
                    _fsConnect.RequestData(Requests.NearbyObjects, Definitions.NearbyObjects, (uint)(assistantSettings["towStringLength"] * 1.5), getTowObjectType());
            }
        }

        public void launchInterval(object sender, EventArgs e)
        {
            if (_fsConnect != null)
            {
                if (_winchPosition != null || _carrierPosition != null || targedCatapultVelocity != 0 || thermalsWorking || towingTarget != 99999999)
                {
                    _fsConnect.RequestData(Requests.PlaneInfo, Definitions.PlaneInfo);
                    _fsConnect.RequestData(Requests.PlaneCommit, Definitions.PlaneCommit);
                    _fsConnect.RequestData(Requests.PlaneRotate, Definitions.PlaneRotate);
                }

                if (towingScanning && towingTarget != 99999999)
                    _fsConnect.RequestData(Requests.NearbyObjects, Definitions.NearbyObjects, (uint)(assistantSettings["towStringLength"] * 1.5), getTowObjectType());
            }
        }

        public SIMCONNECT_SIMOBJECT_TYPE getTowObjectType()
        {
            switch ((int)assistantSettings["towScanType"])
            {
                case 1:
                    return SIMCONNECT_SIMOBJECT_TYPE.AIRCRAFT;
                case 2:
                    return SIMCONNECT_SIMOBJECT_TYPE.GROUND;
                case 3:
                    return SIMCONNECT_SIMOBJECT_TYPE.BOAT;
                case 4:
                    return SIMCONNECT_SIMOBJECT_TYPE.HELICOPTER;
                default:
                    return SIMCONNECT_SIMOBJECT_TYPE.ALL;
            }

        }

        public void toggleConnect(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("toggleConnect");

            if (!validConnection())
            {
                try
                {
                    _fsConnect = new FsConnect();
                    _fsConnect.Connect("Kinetic Assistant", "127.0.0.1", 500, SimConnectProtocol.Ipv4);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    return;
                }

                _fsConnect.FsDataReceived += HandleReceivedFsData;

                Console.WriteLine("Initializing data definitions");
                InitializeDataDefinitions(_fsConnect);

                showMessage("Kinetic Assistant connected", _fsConnect);
                Application.Current.Dispatcher.Invoke(() => playSound("true"));
                changeButtonStatus(false, connectButton, true, "DISCONNECT");
                changeButtonStatus(true, launchPrepareButton, true);
                changeButtonStatus(true, hookPrepareButton, true);
                changeButtonStatus(true, catapultlaunchButton, true);
                changeButtonStatus(true, thermalsToggleButton, true);
                changeButtonStatus(true, towToggleButton, true);

                _fsConnect.RequestData(Requests.PlaneInfo, Definitions.PlaneInfo);
                _fsConnect.RequestData(Requests.PlaneCommit, Definitions.PlaneCommit);
            }
            else
            {
                try
                {
                    showMessage("Kinetic Assistant disconnected", _fsConnect);
                    Application.Current.Dispatcher.Invoke(() => playSound("error"));
                    _fsConnect.Disconnect();
                    _fsConnect.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }

                _fsConnect = null;

                Application.Current.Dispatcher.Invoke(() => abortLaunch());

                changeButtonStatus(true, connectButton, true, "CONNECT");
                changeButtonStatus(false, launchPrepareButton, false);
                changeButtonStatus(false, hookPrepareButton, false);
                changeButtonStatus(false, catapultlaunchButton, false);
                changeButtonStatus(false, thermalsToggleButton, false);
                changeButtonStatus(false, towToggleButton, false);

                Console.WriteLine("Disconnected");
            }
        }

        public bool validConnection()
        {
            if (_fsConnect == null/* || !_fsConnect.Connected*/)
                return false;
            else
                return true;
        }


        // WINCH START
        public void toggleLaunchPrepare(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("toggleLaunchPrepare");

            if (!validConnection())
            {
                Console.WriteLine("connection lost");
                Application.Current.Dispatcher.Invoke(() => abortLaunch());
            }
            else
            {
                if (_winchPosition == null) // PREPARE TO LAUNCH
                {
                    if (_planeInfoResponse.BrakeParkingPosition == 100 && (_planeInfoResponse.SimOnGround == 100 || _planeInfoResponse.AltitudeAboveGround <= _planeInfoResponse.StaticCGtoGround * 1.25))
                    {
                        lastPreparePressed = absoluteTime;
                        Console.WriteLine("Creating winch");

                        cableLength = assistantSettings["stringLength"];
                        _winchPosition = _mathClass.getWinchPosition(_planeInfoResponse, cableLength - 10);

                        Console.WriteLine($"Current location: {_planeInfoResponse.Latitude * 180 / Math.PI} {_planeInfoResponse.Longitude * 180 / Math.PI}");
                        Console.WriteLine($"Winch location: {_winchPosition.location.Latitude * 180 / Math.PI} {_winchPosition.location.Longitude * 180 / Math.PI}");
                        Console.WriteLine($"Bearing: {_planeInfoResponse.PlaneHeading * 180 / Math.PI}deg Distance: {cableLength / 1000}km");

                        showMessage("Winch cable connected - disengage parking brakes to launch", _fsConnect);
                        Application.Current.Dispatcher.Invoke(() => playSound("true"));
                        Application.Current.Dispatcher.Invoke(() => changeButtonStatus(false, launchPrepareButton, true, "Release winch cable"));
                    }
                    else
                    {
                        showMessage("Engage parking brakes first, then connect winch cable", _fsConnect);
                        Application.Current.Dispatcher.Invoke(() => playSound("error"));
                    }
                }
                else // ABORT LAUNCH
                {
                    showMessage("Winch cable released. Gained altitude: " + (_planeInfoResponse.Altitude - _winchPosition.alt).ToString("0.0") + " meters", _fsConnect);
                    Application.Current.Dispatcher.Invoke(() => playSound("false"));
                    Application.Current.Dispatcher.Invoke(() => abortLaunch());
                }
            }
        }

        // INITIATE WINCH LAUNCH
        public void initiateLaunch(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("initiateLaunch");

            if (!validConnection())
            {
                Console.WriteLine("connection lost");
                Application.Current.Dispatcher.Invoke(() => abortLaunch());
            }
            else if (_winchPosition != null && launchTime == 0)
            {
                launchTime = absoluteTime + 5.001;
                showMessage("Launch in " + Math.Floor(launchTime - absoluteTime) + " seconds", _fsConnect);
                Application.Current.Dispatcher.Invoke(() => playSound("true"));
            }
        }

        public void abortLaunch()
        {
            Console.WriteLine("abortLaunch");

            Application.Current.Dispatcher.Invoke(() => changeButtonStatus(_fsConnect != null ? true : false, launchPrepareButton, _fsConnect != null ? true : false, "Attach winch cable"));

            if (validConnection() && launchTime != 0 && _winchPosition != null)
            {
                winchAbortInitiated = true;
            }
            else
            {
                _winchPosition = null;
                launchTime = 0;
                cableLength = 0;
            }
        }

        public void processLaunch()
        {
            bool applyForces;

            if (winchAbortInitiated)
            {
                _winchPosition = null;
                launchTime = 0;
                cableLength = 0;
                winchAbortInitiated = false;
                applyForces = true;
            }
            else
            {

                // GET ANGLE TO WINCH POSITION
                winchDirection _winchDirection = _mathClass.getForceDirection(_winchPosition, _planeInfoResponse);
                double targetVelocity = 0.514 * assistantSettings["targetSpeed"];

                // GET DRAFT CABLE TENSION
                double tensionLimit = assistantSettings["enableFailure"] == 1 ? 6 * 9.81 : 60 * 9.81; ;
                double cableTension = _mathClass.getCableTension(cableLength, Math.Max(1, assistantSettings["elasticExtension"]), _winchDirection, lastFrameTiming, tensionLimit);

                // SHORTEN THE STRING
                if (launchTime != 0 && launchTime - absoluteTime < 0 && cableLength > 10)
                {
                    double pitchCompensation = Math.Pow(Math.Cos(_winchDirection.climbAngle), 1.25);
                    double tensionMultiplier = Math.Pow(1 - Math.Min(cableTension / tensionLimit, 1), 0.75);

                    double timePassed = -launchTime + absoluteTime;
                    double StartTime = 10;

                    if (timePassed < StartTime) // SMOOTH START
                    {
                        cableLength -= tensionMultiplier * pitchCompensation * Math.Pow(timePassed / StartTime, 0.5) * targetVelocity * lastFrameTiming;
                    }
                    else
                    {
                        cableLength -= tensionMultiplier * pitchCompensation * targetVelocity * lastFrameTiming;
                    }
                }

                // LEVEL UP GLIDER BEFORE LAUNCH
                if (_planeInfoResponse.OnAnyRunway == 100 && launchTime != 0 && absoluteTime < launchTime)
                {
                    _planeRotate.RotationVelocityBodyX = 0;
                    _planeRotate.RotationVelocityBodyY = 0;
                    _planeRotate.RotationVelocityBodyZ = - Math.Sin(_planeInfoResponse.PlaneBank) / Math.Max(1, launchTime - absoluteTime);

                    Console.WriteLine($"Leveling {_planeRotate.RotationVelocityBodyZ:F5}");

                    try
                    {
                        _fsConnect.UpdateData(Definitions.PlaneRotate, _planeRotate);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }


                // GET FINAL CABLE TENSION
                cableTension = _mathClass.getCableTension(cableLength, Math.Max(1, assistantSettings["elasticExtension"]), _winchDirection, lastFrameTiming, tensionLimit);
                applyForces = applyWinchForces(cableTension, tensionLimit, _winchDirection, targetVelocity, "Winch", assistantSettings["connectionPoint"]);
            }

            if (applyForces)
            {
                try
                {
                    _fsConnect.UpdateData(Definitions.PlaneCommit, _planeCommit);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        public bool applyWinchForces(double cableTension, double tensionLimit, winchDirection _winchDirection, double targetVelocity, string type, double connectionPoint)
        {
            Console.WriteLine($"{type}: {cableTension / 9.81:F2}g {cableLength:F2}m / {_winchDirection.distance:F2}m h{(_winchDirection.heading * 180 / Math.PI):F0}deg p{(_winchDirection.pitch * 180 / Math.PI):F0}deg");

            double angleLimit = 89 * Math.PI / 180;
            if (cableTension > tensionLimit || _winchDirection.heading < -angleLimit || _winchDirection.heading > angleLimit || _winchDirection.pitch < -angleLimit || _winchDirection.pitch > angleLimit)
            {
                showMessage(assistantSettings["enableFailure"] == 1 && cableTension >= tensionLimit ?
                    type + " cable failure" :
                    type + " cable released" + (type == "Winch" ? ". Gained altitude: " + (_planeInfoResponse.Altitude - _winchPosition.alt).ToString("0.0") + " meters" : ""), _fsConnect);

                if (type == "Winch")
                {
                    Application.Current.Dispatcher.Invoke(() => playSound("false"));
                    winchAbortInitiated = true;
                    Application.Current.Dispatcher.Invoke(() => abortLaunch());
                }
                else if (type == "Tow")
                {
                    towingTarget = 99999999;
                    towCableLength = 0;
                    Application.Current.Dispatcher.Invoke(() => playSound("false"));
                }
            }
            else if (cableTension != 0)
            {
                _planeCommit.VelocityBodyX += _winchDirection.localForceDirection.X * cableTension * lastFrameTiming;
                _mathClass.restrictAirspeed(_planeCommit.VelocityBodyX, targetVelocity, lastFrameTiming);
                _planeCommit.VelocityBodyY += _winchDirection.localForceDirection.Y * cableTension * lastFrameTiming;
                _mathClass.restrictAirspeed(_planeCommit.VelocityBodyY, targetVelocity, lastFrameTiming);
                _planeCommit.VelocityBodyZ += _winchDirection.localForceDirection.Z * cableTension * lastFrameTiming;
                _mathClass.restrictAirspeed(_planeCommit.VelocityBodyZ, targetVelocity, lastFrameTiming);

                // PROCESS NOSE CONNECTION POINT
                double degree5 = 5 * Math.PI / 180;
                if (cableTension > 9.81 / 2 && connectionPoint != 0 && (Math.Abs(_winchDirection.pitch) > degree5 || Math.Abs(_winchDirection.heading) > degree5))
                {
                    double rotationForce = 10 * (cableTension - 9.81 / 2) / tensionLimit;

                    _planeRotate.RotationVelocityBodyX = (_planeRotate.RotationVelocityBodyX - rotationForce * Math.Sin(_winchDirection.pitch)) * lastFrameTiming;
                    _planeRotate.RotationVelocityBodyY = (_planeRotate.RotationVelocityBodyX + rotationForce * Math.Sin(_winchDirection.heading)) * lastFrameTiming;
                    _planeRotate.RotationVelocityBodyZ = 0;

                    Console.WriteLine($"Pitch {_planeRotate.RotationVelocityBodyX:F2} Heading {_planeRotate.RotationVelocityBodyY:F2}");

                    try
                    {
                        _fsConnect.UpdateData(Definitions.PlaneRotate, _planeRotate);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }

                return true;
            }

            return false;
        }
        // WINCH END

        // TOW START
        public void toggleScanning(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("toggleScanning");

            if (!validConnection())
            {
                Console.WriteLine("connection lost");
                Application.Current.Dispatcher.Invoke(() => abortLaunchpad());
            }
            else
            {
                if (towingScanning == false) // PREPARE TO LAUNCH
                {
                    towingScanning = true;
                    Application.Current.Dispatcher.Invoke(() => changeButtonStatus(false, towToggleButton, true, "Deactivate Aerotow"));

                    scanningResults.Visibility = Visibility.Visible;
                    Application.Current.MainWindow.Height = 800;
                }
                else
                {
                    towingScanning = false;

                    if (towingTarget != 99999999)
                    {
                        toggleTowCable(towingTarget);
                    }

                    Application.Current.Dispatcher.Invoke(() => nearbyObjects.Children.Clear());
                    Application.Current.Dispatcher.Invoke(() => farObjects.Children.Clear());
                    Application.Current.Dispatcher.Invoke(() => currentTarget.Children.Clear());
                    Application.Current.Dispatcher.Invoke(() => changeButtonStatus(true, towToggleButton, true, "Activate Aerotow"));

                    scanningResults.Visibility = Visibility.Collapsed;
                    Application.Current.MainWindow.Height = 400;
                }
            }
        }
        public void nearbyInterval(object sender, EventArgs e)
        {
            if (_fsConnect != null)
            {
                if (towingScanning && _nearbyInfoResponse.Count > 0)
                {
                    bool abort = false;
                    nearbyObjects.Children.Clear();
                    farObjects.Children.Clear();
                    currentTarget.Children.Clear();

                    try
                    {
                        foreach (KeyValuePair<uint, winchPosition> obj in new Dictionary<uint, winchPosition>(_nearbyInfoResponse))
                        {
                            if (obj.Key.ToString() == "1")
                                continue;

                            // FILTER OUT CATEGORIES
                            if (getTowObjectType() == SIMCONNECT_SIMOBJECT_TYPE.ALL)
                            {
                                switch (obj.Value.category.ToLower())
                                {
                                    case "airplane":
                                    case "groundvehicle":
                                    case "staticobject":
                                    case "human":
                                    case "boat":
                                    case "":
                                        continue;
                                }
                            }

                            winchDirection dir = _mathClass.getForceDirection(obj.Value, _planeInfoResponse);

                            Button label = new Button();
                            label.Tag = obj.Key.ToString();
                            label.FontSize = 10;
                            label.Background = new SolidColorBrush(Colors.Transparent);

                            string title = obj.Value.category + " " + obj.Value.title.Replace("_", " ");
                            label.Content = (obj.Key + " " + title).Substring(0, Math.Min(title.Length, 40)) + " (" + dir.distance.ToString(".0m") + ")";

                            if (dir.distance > 5.0 && dir.distance <= assistantSettings["towStringLength"] || obj.Key == towingTarget)
                            {
                                label.Foreground = new SolidColorBrush(obj.Key == towingTarget ? Colors.DarkRed : Colors.DarkGreen);
                                label.BorderBrush = new SolidColorBrush(obj.Key == towingTarget ? Colors.DarkRed : Colors.DarkGreen);
                                label.Cursor = Cursors.Arrow;
                                label.Margin = new Thickness(5, 5, 5, 5);
                                label.Height = 26;
                                label.Click += attachTowCable;
                                label.Cursor = Cursors.Hand;

                                if (obj.Key == towingTarget)
                                    currentTarget.Children.Add(label);
                                else
                                    nearbyObjects.Children.Add(label);
                            }
                            else if (dir.distance <= 1.5 * assistantSettings["towStringLength"])
                            {
                                label.Margin = new Thickness(5, 1, 5, 1);
                                label.IsEnabled = false;
                                farObjects.Children.Add(label);
                            }

                            if (_nearbyInfoResponseLast.ContainsKey(obj.Key)) { _nearbyInfoResponsePreLast[obj.Key] = _nearbyInfoResponseLast[obj.Key]; }
                            if (_nearbyInfoResponse.ContainsKey(obj.Key)) { _nearbyInfoResponseLast[obj.Key] = _nearbyInfoResponse[obj.Key]; }

                            // CHECK CURRENT TARGET EXISTANCE
                            if (towingTarget != 99999999 && 
                                !_nearbyInfoResponse.ContainsKey(towingTarget) && !_nearbyInfoResponseLast.ContainsKey(towingTarget) && !_nearbyInfoResponsePreLast.ContainsKey(towingTarget))
                                abort = true;

                            if (abort)
                            {
                                Console.WriteLine("Tow plane lost");
                                toggleTowCable(towingTarget);
                            }

                            _nearbyInfoResponse = new Dictionary<uint, winchPosition>();
                        }
                    } catch(Exception ex) { Console.WriteLine(ex.Message); }

                }
            }
        }

        public void attachTowCable(object sender, EventArgs e)
        {
            if (uint.TryParse(((Button)sender).Tag.ToString(), out uint id)) {
                toggleTowCable(id);
            }
        }

        public void toggleTowCable(uint id)
        {
            if (towingTarget == id)
            {
                towingTarget = 99999999;
                towCableLength = 0;

                showMessage("Tow string released", _fsConnect);
                Application.Current.Dispatcher.Invoke(() => playSound("false"));
            }
            else if (_planeInfoResponse.SimOnGround == 100 || _planeInfoResponse.AltitudeAboveGround <= _planeInfoResponse.StaticCGtoGround * 1.25 && 
                _nearbyInfoResponse.ContainsKey(id))
            {
                towingTarget = id;
                winchPosition position = _nearbyInfoResponse[id];
                winchDirection direction = _mathClass.getForceDirection(position, _planeInfoResponse);
                towCableLength = direction.distance + 5;

                showMessage("Tow string connected", _fsConnect);
                Application.Current.Dispatcher.Invoke(() => playSound("false"));
            }

            nearbyInterval(new object(), new EventArgs());
        }

        public void processTowing()
        {
            Console.WriteLine("processTowing");

            if (_nearbyInfoResponse.ContainsKey(towingTarget))
            {
                // GET ANGLE TO TUG POSITION
                winchPosition winchPosition = _nearbyInfoResponse[towingTarget];
                winchDirection winchDirection = _mathClass.getForceDirection(winchPosition, _planeInfoResponse);

                // GET FINAL CABLE TENSION
                double tensionLimit = assistantSettings["enableFailure"] == 1 ? 8 * 9.81 : 20 * 9.81;
                double cableTension = _mathClass.getCableTension(towCableLength, Math.Max(1, assistantSettings["towElasticExtension"]), winchDirection, lastFrameTiming, tensionLimit);
                double targetVelocity = winchPosition.airspeed > 25 ? winchPosition.airspeed * 2 : 50;

                if (applyWinchForces(cableTension, tensionLimit, winchDirection, targetVelocity, "Tow", assistantSettings["towConnectionPoint"]))
                {
                    try
                    {
                        _fsConnect.UpdateData(Definitions.PlaneCommit, _planeCommit);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
            }
        }

        // TOW END


        // LAUNCHPAD START
        public void toggleLaunchpadPrepare(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("toggleLaunchpadPrepare");

            if (!validConnection())
            {
                Console.WriteLine("connection lost");
                Application.Current.Dispatcher.Invoke(() => abortLaunchpad());
            }
            else
            {
                if (targedCatapultVelocity == 0) // PREPARE TO LAUNCH
                {
                    if (_planeInfoResponse.BrakeParkingPosition == 100 && (_planeInfoResponse.SimOnGround == 100 || _planeInfoResponse.AltitudeAboveGround <= _planeInfoResponse.StaticCGtoGround * 1.25))
                    {
                        targedCatapultVelocity = 0.9 * 0.514 * assistantSettings["catapultTargetSpeed"];
                        showMessage("Launchpad connected - disengage parking brakes to launch", _fsConnect);
                        Application.Current.Dispatcher.Invoke(() => playSound("true"));
                        Application.Current.Dispatcher.Invoke(() => changeButtonStatus(false, catapultlaunchButton, true, "Abort launch"));
                    }
                    else
                    {
                        showMessage("Engage parking brakes first, then connect launchpad", _fsConnect);
                        Application.Current.Dispatcher.Invoke(() => playSound("error"));
                    }
                }
                else // ABORT LAUNCH
                {
                    showMessage("Launch aborted", _fsConnect);
                    Application.Current.Dispatcher.Invoke(() => playSound("false"));
                    Application.Current.Dispatcher.Invoke(() => abortLaunchpad());
                }
            }
        }
        public void abortLaunchpad()
        {
            Console.WriteLine("abortLaunchpad");
            Application.Current.Dispatcher.Invoke(() => changeButtonStatus(_fsConnect != null ? true : false, catapultlaunchButton, true, "Ready to launch"));

            if (validConnection() && targedCatapultVelocity != 0)
            {
                launchpadAbortInitiated = true;
            }
            else
            {
                _planeCommit.LaunchbarPosition = 0;
                targedCatapultVelocity = 0;

            }
        }
        public void processLaunchpad()
        {
            if (launchpadAbortInitiated)
            {
                _planeCommit.LaunchbarPosition = 0;
                targedCatapultVelocity = 0;
                launchpadAbortInitiated = false;
            }
            else if (_planeInfoResponse.BrakeParkingPosition == 0)
            {
                // ANIMATE LAUNCHPAD
                if (_planeCommit.LaunchbarPosition != 100) { _planeCommit.LaunchbarPosition = 100; }

                // ENCREASE SPEED
                if (_planeCommit.VelocityBodyZ < 0.514 * assistantSettings["catapultTargetSpeed"])
                {
                    double diff = lastFrameTiming * (0.514 * assistantSettings["catapultTargetSpeed"] - targedCatapultVelocity);
                    targedCatapultVelocity -= diff;
                    _planeCommit.VelocityBodyZ += diff;

                    if (_planeCommit.VelocityBodyZ >= 0.9 * 0.514 * assistantSettings["catapultTargetSpeed"] || _planeInfoResponse.SimOnGround != 100)
                        abortLaunchpad();

                    Console.WriteLine("Launchpad acceleration: " + diff / lastFrameTiming);

                    // LIMIT ROTATION VELOCITY
                    _planeRotate.RotationVelocityBodyX *= lastFrameTiming;
                    _planeRotate.RotationVelocityBodyY *= lastFrameTiming;
                    _planeRotate.RotationVelocityBodyZ *= lastFrameTiming;

                    try
                    {
                        _fsConnect.UpdateData(Definitions.PlaneRotate, _planeRotate);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
            }

            try
            {
                _fsConnect.UpdateData(Definitions.PlaneCommit, _planeCommit);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        // LAUNCHPAD END

        // LANDING START
        public void toggleLandingPrepare(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("toggleLandingPrepare");

            if (!validConnection())
            {
                Console.WriteLine("connection lost");
            }
            else if (_carrierPosition == null)
            {
                _carrierPosition = new winchPosition();
                arrestorConnectedTime = 0;
                showMessage("Tailhook extracted", _fsConnect);
                Application.Current.Dispatcher.Invoke(() => playSound("true"));
                Application.Current.Dispatcher.Invoke(() => changeButtonStatus(false, hookPrepareButton, true, "Retract tailhook"));
            }
            else
            {
                arrestorsAbortInitiated = true;
            }
        }
        public void processLanding()
        {
            if (arrestorsAbortInitiated)
            {
                // RESET HOOK
                _planeCommit.TailhookPosition = 0;

                arrestorsAbortInitiated = false;
                _carrierPosition = null;
                arrestorConnectedTime = 0;
                //showMessage("Tailhook retracted", _fsConnect);
                Application.Current.Dispatcher.Invoke(() => changeButtonStatus(_fsConnect != null ? true : false, hookPrepareButton, true, "Extract tailhook"));
            }
            else
            {
                // ANIMATE HOOK
                if (_planeCommit.TailhookPosition < 100) { _planeCommit.TailhookPosition = Math.Min(_planeCommit.TailhookPosition + 50 * Math.Min(0.1, lastFrameTiming), 100); }

                // SET CONTACT POINT
                if (_planeCommit.VelocityBodyZ > 20 && _carrierPosition.alt == 0 && _planeCommit.TailhookPosition == 100 && 
                    (_planeInfoResponse.SimOnGround == 100 || _planeInfoResponse.AltitudeAboveGround <= _planeInfoResponse.StaticCGtoGround * 1.25))
                {
                    _carrierPosition.alt = _planeInfoResponse.Altitude - _planeInfoResponse.StaticCGtoGround;
                    _carrierPosition.location = new GeoLocation(_planeInfoResponseLast.Latitude, _planeInfoResponseLast.Longitude);
                    arrestorConnectedTime = absoluteTime;

                    Console.WriteLine($"Current location: {_planeInfoResponse.Latitude * 180 / Math.PI} {_planeInfoResponse.Longitude * 180 / Math.PI}");
                    Console.WriteLine($"String location: {_carrierPosition.location.Latitude * 180 / Math.PI} {_carrierPosition.location.Longitude * 180 / Math.PI}");
                }

                if (_carrierPosition.alt != 0)
                {
                    double timeLeft = 1.33 * assistantSettings["arrestorFullStopTime"] - (absoluteTime - arrestorConnectedTime);

                    // GET ANGLE TO STRING POSITION
                    winchDirection _carrierDirection = _mathClass.getForceDirection(_carrierPosition, _planeInfoResponse);

                    double accel = _planeCommit.VelocityBodyZ - _planeCommitLast.VelocityBodyZ;

                    double cableTension = 0;
                    double tensionLimit = 5 * 9.81;
                    // ARREST IN PROCESS - FIND OUT TENSION
                    cableTension = 1.2 * (_planeCommit.VelocityBodyZ * lastFrameTiming / (timeLeft / 1.5) + (accel > 0 ? accel : 0));
                    cableTension = Math.Min(tensionLimit * lastFrameTiming, cableTension);

                    //Console.WriteLine("TAIL HOOK " + (_planeInfoResponse.AltitudeAboveGround - _planeInfoResponse.StaticCGtoGround));
                    Console.WriteLine($"String: cableTension{cableTension / lastFrameTiming:F2} timeLeft{timeLeft:F2} {_carrierDirection.distance:F2}m h{(_carrierDirection.heading * 180 / Math.PI):F0}deg p{(_carrierDirection.pitch * 180 / Math.PI):F0}deg");

                    if (timeLeft <= 0 || assistantSettings["enableFailure"] == 1 && (cableTension / lastFrameTiming > 100 || _carrierDirection.distance > 105))
                    {
                        showMessage(
                            timeLeft <= 0 ? "Arresting cable released. Distance: " + _carrierDirection.distance.ToString("0.0") + " meters" :
                            "Arresting cable failure. Distance " + _carrierDirection.distance.ToString("0.0") + " meters", _fsConnect);
                        Application.Current.Dispatcher.Invoke(() => playSound("false"));
                        arrestorsAbortInitiated = true;
                        _planeCommit.TailhookPosition = 50;
                    }
                    else if (cableTension != 0 && _carrierDirection.localForceDirection.Norm > 0)
                    {
                        // LIMIT ROTATION VELOCITY
                        _planeRotate.RotationVelocityBodyX *= lastFrameTiming;
                        _planeRotate.RotationVelocityBodyY *= lastFrameTiming;
                        _planeRotate.RotationVelocityBodyZ *= lastFrameTiming;

                        try
                        {
                            _fsConnect.UpdateData(Definitions.PlaneRotate, _planeRotate);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }

                        _planeCommit.VelocityBodyX += _carrierDirection.localForceDirection.X * cableTension;
                        _planeCommit.VelocityBodyY += _carrierDirection.localForceDirection.Y * cableTension;
                        _planeCommit.VelocityBodyZ += _carrierDirection.localForceDirection.Z * cableTension;

                        // REMOVE VERTICAL VELOCITY
                        if (_planeCommit.VelocityBodyY > 0.1)
                            _planeCommit.VelocityBodyY *= -1.1;
                    }
                }
            }


            try
            {
                _fsConnect.UpdateData(Definitions.PlaneCommit, _planeCommit);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        // LANDING END

        // THERMALS START
        public void thermalsLoadMap(object sender, RoutedEventArgs e)
        {
            thermalsList = new List<winchPosition>();

            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Iittle Navmap Userpoints (*.csv)|*.csv";
            openFileDialog.FilterIndex = 2;
            openFileDialog.RestoreDirectory = true;
            if (openFileDialog.ShowDialog() == true)
            {
                string content = File.ReadAllText(openFileDialog.FileName);
                foreach(string line in content.Split( new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None) )
                { // Location,,thermal,40.17048,-111.96616,5000,11.95587,10,,,250,2021-01-04T23:32:47.770,
                    if (!String.IsNullOrWhiteSpace(line))
                    {
                        string[] data = line.Split(',');
                        if (data.Length > 7 && data[2].ToLower() == "thermal")
                        {
                            double radius;
                            double strength = 15;

                            if (double.TryParse(data[3], out double lat) &&
                                double.TryParse(data[4], out double lng) &&
                                double.TryParse(data[5], out double alt) &&
                                (data[7].Contains(" ") && double.TryParse(data[7].Split(' ')[0], out radius) && double.TryParse(data[7].Split(' ')[1], out strength) ||
                                double.TryParse(data[7], out radius)))
                            {
                                winchPosition _thermalPosition = new winchPosition(new GeoLocation(lat / 180 * Math.PI, lng / 180 * Math.PI), 0.305 * alt, 1852 * radius, strength / 1.9);
                                thermalsList.Add(_thermalPosition);
                            }
                            else
                            {
                                MessageBox.Show("Userpoint record numbers format is incorrect");
                            }
                        }
                        else
                        {
                            MessageBox.Show("Userpoint record does not have enough parameters");
                        }
                    }
                }

                thermalsMapPath.Text = thermalsList.Count > 0 ? thermalsList.Count.ToString() + " thermals loaded" : "No thermals records found";
                //;
            }
        }
        public void toggleThermals(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("toggleThermals");

            if (!validConnection())
            {
                Console.WriteLine("connection lost");
            }
            else if (thermalsList.Count == 0)
            {
                Application.Current.Dispatcher.Invoke(() => playSound("error"));
                Application.Current.Dispatcher.Invoke(() => thermalsMapPath.Text = "Load thermals map!");
            }
            else if (!thermalsWorking)
            {
                thermalsWorking = true;
                showMessage("Thermals enabled", _fsConnect);
                Application.Current.Dispatcher.Invoke(() => playSound("true"));
                Application.Current.Dispatcher.Invoke(() => changeButtonStatus(false, thermalsToggleButton, true, "Disable thermals"));
            }
            else
            {
                thermalsWorking = false;
                showMessage("Thermals disabled", _fsConnect);
                Application.Current.Dispatcher.Invoke(() => playSound("false"));
                Application.Current.Dispatcher.Invoke(() => changeButtonStatus(true, thermalsToggleButton, true, "Enable thermals"));
            }
        }
        public void processThermals()
        {
            int id = 0;
            Application.Current.Dispatcher.Invoke(() => thermalsLog.Children.Clear());

            if (_planeCommit.VelocityBodyZ < 100) {
                foreach (winchPosition thermal in thermalsList)
                {
                    double inversionLayer = 500 * 0.305;
                    double topEdge = thermal.alt + inversionLayer;

                    //Console.WriteLine(_planeInfoResponse.AltitudeAboveGround + " " + thermal.alt);
                    if (_planeInfoResponse.AltitudeAboveGround < topEdge && thermal.radius > 0)
                    {
                        winchPosition thermalTemp = new winchPosition(thermal.location, topEdge, thermal.radius);
                        thermalTemp.alt = _planeInfoResponse.Altitude - 10 * thermalTemp.radius;
                        winchDirection _thermalDirection = _mathClass.getForceDirection(thermalTemp, _planeInfoResponse);

                        if (_thermalDirection.groundDistance < thermalTemp.radius)
                        {
                            double horizontalModifier = Math.Pow(1 - _thermalDirection.groundDistance / thermalTemp.radius, 0.25); // DISTANCE TO THE CENTER
                            double verticalModifier = _planeInfoResponse.AltitudeAboveGround < topEdge - inversionLayer ?
                                Math.Pow(_planeInfoResponse.AltitudeAboveGround / (topEdge - inversionLayer), 0.25) : // DISTANCE TO THE TOP + INVERSION
                                (topEdge - _planeInfoResponse.AltitudeAboveGround - inversionLayer / 2 ) / (inversionLayer / 2); // ABOVE INVERSION
                            double pitchBankModifier = Math.Abs(Math.Cos(_planeInfoResponse.PlaneBank)) * Math.Abs(Math.Cos(_planeInfoResponse.PlanePitch)); // ROTATION
                            double airspeedModifier = Math.Pow(1 - _planeCommit.VelocityBodyZ / 100, 0.25); // ATTEMPT TO PREVENT OVERSPEED

                            double finalModifier = horizontalModifier * verticalModifier * pitchBankModifier * airspeedModifier;

                            double liftAmount = - thermal.airspeed * finalModifier;
                            _planeCommit.WaterRudderHandlePosition = horizontalModifier * verticalModifier * 100;

                            _planeCommit.VelocityBodyX += _thermalDirection.localForceDirection.X * liftAmount * lastFrameTiming;
                            _planeCommit.VelocityBodyY += _thermalDirection.localForceDirection.Y * liftAmount * lastFrameTiming;
                            _planeCommit.VelocityBodyZ += _thermalDirection.localForceDirection.Z * liftAmount * lastFrameTiming;

                            // FORWARD SPEED COMPENSATION
                            _planeCommit.VelocityBodyZ += _planeCommit.VelocityBodyZ * Math.Pow(lastFrameTiming, 2.5) * horizontalModifier * verticalModifier * airspeedModifier;

                            try
                            {
                                _fsConnect.UpdateData(Definitions.PlaneCommit, _planeCommit);

                                Application.Current.Dispatcher.Invoke(() => thermalsLog.Children.Add(makeTextBlock("Thermal #" + id + Environment.NewLine, Colors.Black)));
                                Application.Current.Dispatcher.Invoke(() => thermalsLog.Children.Add(makeTextBlock($"To center:  {_thermalDirection.groundDistance:F0}m mod: {horizontalModifier:F2}", Colors.Black)));
                                Application.Current.Dispatcher.Invoke(() => thermalsLog.Children.Add(makeTextBlock($"To top:     {(thermal.alt - _planeInfoResponse.AltitudeAboveGround):F0}m mod: {verticalModifier:F2}", (thermal.alt - _planeInfoResponse.AltitudeAboveGround) > 0 ? Colors.Black : Colors.DarkRed)));
                                Application.Current.Dispatcher.Invoke(() => thermalsLog.Children.Add(makeTextBlock($"Pitch/bank mod: {pitchBankModifier:F2}", Colors.Black)));
                                Application.Current.Dispatcher.Invoke(() => thermalsLog.Children.Add(makeTextBlock($"Velocity:   {_planeCommit.VelocityBodyZ:F0}m/s mod: {airspeedModifier:F2}", Colors.Black)));
                                Application.Current.Dispatcher.Invoke(() => thermalsLog.Children.Add(makeTextBlock($"Total lift: {-liftAmount:F2}m/s mod: {finalModifier:F2}" + Environment.NewLine, finalModifier > 0 ? Colors.Black : Colors.DarkRed)));

                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.Message);
                            }
                        }

                        id++;
                    }
                }
            }
        }
        // THERMALS END

        // DATA EXCHANGE
        public void HandleReceivedFsData(object sender, FsDataReceivedEventArgs e)
        {
            try
            {
                // COMMON DATA
                if (e.RequestId == (uint)Requests.PlaneInfo)
                {
                    //Console.WriteLine(JsonConvert.SerializeObject(e.Data, Formatting.Indented));
                    _planeInfoResponse = (PlaneInfoResponse)(e.Data);

                    absoluteTime = _planeInfoResponse.AbsoluteTime;

                    trackControlChanges();

                    _planeInfoResponseLast = _planeInfoResponse;
                }
                // LAUNCH DATA
                else if (e.RequestId == (uint)Requests.PlaneCommit)
                {
                    //Console.WriteLine(JsonConvert.SerializeObject(e.Data, Formatting.Indented));
                    _planeCommit = (PlaneInfoCommit)e.Data;

                    absoluteTime = _planeCommit.AbsoluteTime;
                    lastFrameTiming = absoluteTime - swLast;

                    if (lastFrameTiming > 0.0 && lastFrameTiming <= 1.0)
                    {
                        // CHECK WINCH LAUNCH STANDBY
                        if (_planeInfoResponse.BrakeParkingPosition == 0 && _winchPosition != null && launchTime == 0)
                        {
                            initiateLaunch(new object(), new RoutedEventArgs());
                        }
                        // APPLY PHYSICS IS LAUNCH ACTIVE
                        if (lastFrameTiming != 0 && _winchPosition != null)
                        {
                            processLaunch();
                        }
                        // ARRESTING PHYSICS
                        if (_carrierPosition != null)
                        {
                            processLanding();
                        }
                        // LAUNCHPAD PHYSICS
                        if (targedCatapultVelocity != 0)
                        {
                            processLaunchpad();
                        }
                        // THERMALS PHYSICS
                        if (thermalsWorking)
                        {
                            processThermals();
                        }
                    }

                    swLast = absoluteTime;

                    _planeCommitLast = _planeCommit;
                }
                // ROTATION DATA
                else if (e.RequestId == (uint)Requests.PlaneRotate)
                {
                    //Console.WriteLine(JsonConvert.SerializeObject(e.Data, Formatting.Indented));
                    _planeRotate = (PlaneInfoRotate)e.Data;
                }
                // NEARBY DATA
                else if (e.RequestId == (uint)Requests.NearbyObjects)
                {
                    //Console.WriteLine(e.ObjectID);
                    //Console.WriteLine(JsonConvert.SerializeObject(e.Data, Formatting.Indented));
                    NearbyInfoResponse response = (NearbyInfoResponse)e.Data;
                    
                    if (_nearbyInfoResponseLast.ContainsKey(e.ObjectID)) { _nearbyInfoResponsePreLast[e.ObjectID] = _nearbyInfoResponseLast[e.ObjectID]; }
                    if (_nearbyInfoResponse.ContainsKey(e.ObjectID)) { _nearbyInfoResponseLast[e.ObjectID] = _nearbyInfoResponse[e.ObjectID]; }
                    _nearbyInfoResponse[e.ObjectID] = new winchPosition(new GeoLocation(response.Latitude, response.Longitude), response.Altitude, 0, response.Airspeed, response.Title, response.Category);

                    // TOWING IN PROCESS
                    if (towingTarget == e.ObjectID)
                    {
                        processTowing();
                    }
                }
                else
                {
                    Console.WriteLine("Unknown request ID " + (uint)Requests.PlaneInfo + " received (type " + e.Data.GetType() + ")");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not handle received FS data: " + ex.Message);
            }
        }

        public void trackControlChanges()
        {
            foreach (var field in typeof(PlaneInfoResponse).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (field.FieldType == typeof(double) && field.Name.StartsWith("LIGHT") &&
                    (double)field.GetValue(_planeInfoResponse) != (double)field.GetValue(_planeInfoResponseLast))
                {
                    if (absoluteTime - controlTimestamps[field.Name] < 1.0)
                    {
                        Console.WriteLine(field.Name + " toggle detected");

                        // FIND FUNTION TO TOGGLE
                        Application.Current.Dispatcher.Invoke(() => runControlFunction(field.Name));

                        controlTimestamps[field.Name] = 0;
                    }
                    else
                    {
                        controlTimestamps[field.Name] = absoluteTime;
                    }
                }

        }

        public void runControlFunction(string fieldName)
        {
            foreach (ComboBox tb in FindLogicalChildren<ComboBox>(window))
            {
                if (tb.Tag != null)
                {
                    if (tb.SelectedItem.ToString().Replace(" ", "") == fieldName)
                    {
                        Console.WriteLine("Triggering function " + tb.Tag.ToString());
                        Type type = Application.Current.MainWindow.GetType();
                        MethodInfo method = type.GetMethod(tb.Tag.ToString());
                        method.Invoke(Application.Current.MainWindow, new object[] {null, null});
                    }
                }
            }
        }

        public void InitializeDataDefinitions(FsConnect fsConnect)
        {
            Console.WriteLine("InitializeDataDefinitions");

            List<SimProperty> definition = new List<SimProperty>();

            definition.Add(new SimProperty(FsSimVar.Title, FsUnit.None, SIMCONNECT_DATATYPE.STRING256));
            definition.Add(new SimProperty(FsSimVar.PlaneLatitude, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.PlaneLongitude, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.PlaneAltitudeAboveGround, FsUnit.Meter, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.StaticCGtoGround, FsUnit.Meter, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.PlaneAltitude, FsUnit.Meter, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.AbsoluteTime, FsUnit.Second, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.PlaneHeading, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.PlanePitch, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.PlaneBank, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.SimOnGround, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.BrakeParkingPosition, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.OnAnyRunway, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            // LIGHT STUFF
            definition.Add(new SimProperty(FsSimVar.LIGHTPANEL, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.LIGHTSTROBE, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.LIGHTLANDING, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.LIGHTTAXI, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.LIGHTBEACON, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.LIGHTNAV, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.LIGHTLOGO, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.LIGHTWING, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.LIGHTRECOGNITION, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.LIGHTCABIN, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.LIGHTGLARESHIELD, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.LIGHTPEDESTRAL, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.LIGHTPOTENTIOMETER, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));

            fsConnect.RegisterDataDefinition<PlaneInfoResponse>(Definitions.PlaneInfo, definition);

            List<SimProperty> cDefinition = new List<SimProperty>();
            cDefinition.Add(new SimProperty(FsSimVar.VelocityBodyX, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.VelocityBodyY, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.VelocityBodyZ, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.AbsoluteTime, FsUnit.Second, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.TailhookPosition, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.LaunchbarPosition, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.WaterRudderHandlePosition, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));

            fsConnect.RegisterDataDefinition<PlaneInfoCommit>(Definitions.PlaneCommit, cDefinition);

            List<SimProperty> rDefinition = new List<SimProperty>();
            rDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyX, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            rDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyY, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            rDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyZ, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));

            fsConnect.RegisterDataDefinition<PlaneInfoRotate>(Definitions.PlaneRotate, rDefinition);

            List<SimProperty> nDefinition = new List<SimProperty>();
            nDefinition.Add(new SimProperty(FsSimVar.Title, FsUnit.None, SIMCONNECT_DATATYPE.STRING256));
            nDefinition.Add(new SimProperty(FsSimVar.Category, FsUnit.None, SIMCONNECT_DATATYPE.STRING256));
            nDefinition.Add(new SimProperty(FsSimVar.PlaneLatitude, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            nDefinition.Add(new SimProperty(FsSimVar.PlaneLongitude, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            nDefinition.Add(new SimProperty(FsSimVar.PlaneAltitude, FsUnit.Meter, SIMCONNECT_DATATYPE.FLOAT64));
            nDefinition.Add(new SimProperty(FsSimVar.AirspeedTrue, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            

            fsConnect.RegisterDataDefinition<NearbyInfoResponse>(Definitions.NearbyObjects, nDefinition);
        }

        public void changeButtonStatus(bool active, Button btn, bool? enabled = null, string text = "")
        {
            if (active)
            {
                btn.BorderBrush = new SolidColorBrush(Colors.DarkGreen);
                btn.Foreground = new SolidColorBrush(Colors.DarkGreen);
            }
            else
            {
                btn.BorderBrush = new SolidColorBrush(Colors.DarkRed);
                btn.Foreground = new SolidColorBrush(Colors.DarkRed);
            }

            if (!string.IsNullOrEmpty(text))
                btn.Content = text;

            if (enabled != null)
                btn.IsEnabled = enabled == true;
        }

        public static IEnumerable<T> FindLogicalChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                foreach (object rawChild in LogicalTreeHelper.GetChildren(depObj))
                {
                    if (rawChild is DependencyObject)
                    {
                        DependencyObject child = (DependencyObject)rawChild;
                        if (child is T)
                        {
                            yield return (T)child;
                        }

                        foreach (T childOfChild in FindLogicalChildren<T>(child))
                        {
                            yield return childOfChild;
                        }
                    }
                }
            }
        }

        // SETTINGS STUFF
        public void processSettings(bool write = true)
        {
            foreach (ComboBox tb in FindLogicalChildren<ComboBox>(window))
            {
                if (tb.Name != null)
                {
                    if (write && assistantSettings.ContainsKey(tb.Name))
                    {
                        Console.WriteLine(tb.Name + " " + assistantSettings[tb.Name].ToString());
                        if (((ComboBox)window.FindName(tb.Name)).IsEditable == true)
                            ((ComboBox)window.FindName(tb.Name)).Text = assistantSettings[tb.Name].ToString();
                        else
                            ((ComboBox)window.FindName(tb.Name)).SelectedIndex = (int)assistantSettings[tb.Name];
                    }
                    else if (!write)
                    {
                        double value;
                        if (tb.IsEditable == true)
                            double.TryParse(tb.Text, out value);
                        else
                            value = tb.SelectedIndex;

                        if (!assistantSettings.ContainsKey(tb.Name))
                            assistantSettings.Add(tb.Name, value);
                        else
                            assistantSettings[tb.Name] = value;
                    }
                }
            }

            foreach (CheckBox tb in FindLogicalChildren<CheckBox>(window))
                if (tb.Name != null)
                {
                    if (write && assistantSettings.ContainsKey(tb.Name))
                    {
                        ((CheckBox)window.FindName(tb.Name)).IsChecked = assistantSettings[tb.Name] != 0;
                    }
                    else if (!write)
                    {
                        if (!assistantSettings.ContainsKey(tb.Name))
                            assistantSettings.Add(tb.Name, tb.IsChecked == true ? 1 : 0);
                        else
                            assistantSettings[tb.Name] = tb.IsChecked == true ? 1 : 0;
                    }
                }

            foreach (Slider tb in FindLogicalChildren<Slider>(window))
                if (tb.Name != null)
                {
                    if (write && assistantSettings.ContainsKey(tb.Name))
                    {
                        ((Slider)window.FindName(tb.Name)).Value = assistantSettings[tb.Name];
                    }
                    else if (!write)
                    {
                        if (!assistantSettings.ContainsKey(tb.Name))
                            assistantSettings.Add(tb.Name, tb.Value);
                        else
                            assistantSettings[tb.Name] = tb.Value;
                    }
                }
        }

        public void loadSettings()
        {
            if (File.Exists("assistantSettings.json"))
            {
                try
                {
                    JsonConvert.PopulateObject(File.ReadAllText("assistantSettings.json"), assistantSettings);
                    processSettings(true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }

        }

        public void saveSettings(object sender, RoutedEventArgs e)
        {
            try
            {
                if (loaded)
                {
                    processSettings(false);
                    File.WriteAllText("assistantSettings.json", JsonConvert.SerializeObject(assistantSettings));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void Hyperlink_RequestNavigate(object sender,
                                               System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(e.Uri.ToString().Contains("//") ? e.Uri.AbsoluteUri : e.Uri.ToString());
        }

        public void toggleControlOptions(object sender, RoutedEventArgs e)
        {
            foreach( var element in ((StackPanel)((Button)sender).Parent).Children)
            {
                if (element.GetType() == typeof(ComboBox))
                {
                    ((ComboBox)element).Visibility = ((ComboBox)element).Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
                    break;
                }
                else if (element.GetType() == typeof(Button))
                    ((Button)element).Visibility = ((Button)element).Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        public void showMessage(string text, FsConnect _fsConnect)
        {
            if (assistantSettings["displayTips"] == 1)
            {
                try
                {
                    _fsConnect.SetText(text, 1);
                }
                catch { }
            }

            Console.WriteLine(text);
        }

        public void playSound(string soundName)
        {
            if (assistantSettings["playSounds"] == 1)
            {
                if (soundPlayer != null)
                {
                    soundPlayer.Stop();
                }

                soundPlayer = new MediaPlayer();
                soundPlayer.Volume = assistantSettings["soundsVolume"] / 100;
                soundPlayer.Open(new Uri("pack://siteoforigin:,,,/" + soundName + ".wav"));
                soundPlayer.Play();
            }
        }

        public TextBlock makeTextBlock(string text, Color color)
        {
            TextBlock textblock = new TextBlock();
            textblock.Text = text;
            textblock.Foreground = new SolidColorBrush(color);
            textblock.FontFamily = new FontFamily("Consolas");

            return textblock;
        }
    }
}