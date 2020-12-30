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

//TAILHOOK POSITION:1#percent/
//LIGHT RECOGNITION#  percent

//(A:LAUNCHBAR POSITION:1, percent)/
//(A:LIGHT WING,           percent)/

//(A:LAUNCHBAR POSITION, percent)/
//(A:LIGHT WING,         percent)/

//(A:TOW RELEASE HANDLE, percent)/
//(A:LIGHT WING,         percent)/

namespace MSFS_Cloud_Assistant
{
    public partial class MainWindow : System.Windows.Window
    {
        public FsConnect _fsConnect;
        public Dictionary<ConsoleKey, Action> _keyHandlers = new Dictionary<ConsoleKey, Action>();
        public PlaneInfoResponse _planeInfoResponse;
        public PlaneInfoResponse _planeInfoResponseLast;
        public PlaneInfoCommit _planeCommit;
        public PlaneInfoCommit _planeCommitLast;

        // IF NOT NULL - READY TO LAUNCH
        public winchPosition _winchPosition = null;
        public winchPosition _carrierPosition = null;

        // IF NOT 0 - WINCH WORKING
        public double launchTime = 0;

        // IF NOT 0 - ARRESTER CONNECTED
        public double arrestorConnectedTime = 0;

        // IF NOT 0 - CATAPULT LAUNCH IN PROCESS
        public double targedCatapultVelocity = 0;

        bool abortInitiated = false;

        public double absoluteTime;
        public double swLast;
        public double lastFrameTiming;
        DispatcherTimer dispatcherTimer;
        public double lastPreparePressed = 0;

        public bool loaded = false;
        MathClass _mathClass;

        Dictionary<string, double> assistantSettings;

        public MainWindow()
        {
            _mathClass = new MathClass();
            assistantSettings = new Dictionary<string, double>();

            InitializeComponent();

            // COMMON SIM VALUES REQUEST TIMER
            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 1000);
            dispatcherTimer.Tick += new EventHandler(commonInterval);
            dispatcherTimer.Start();

            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 100);
            dispatcherTimer.Tick += new EventHandler(launchInterval);
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
                if (_winchPosition == null && _carrierPosition == null && targedCatapultVelocity == 0)
                    _fsConnect.RequestData(Requests.PlaneInfo, Definitions.PlaneInfo);
            }
        }

        public void launchInterval(object sender, EventArgs e)
        {
            if (_fsConnect != null)
            {
                if (_winchPosition != null || _carrierPosition != null || targedCatapultVelocity != 0)
                {
                    _fsConnect.RequestData(Requests.PlaneInfo, Definitions.PlaneInfo);
                    _fsConnect.RequestData(Requests.PlaneCommit, Definitions.PlaneCommit);
                }
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
                    _fsConnect.Connect("Cloud Assistant", "127.0.0.1", 500, SimConnectProtocol.Ipv4);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    return;
                }

                _fsConnect.FsDataReceived += HandleReceivedFsData;

                Console.WriteLine("Initializing data definitions");
                InitializeDataDefinitions(_fsConnect);

                showMessage("Cloud Assistant connected", _fsConnect);
                changeButtonStatus(true, connectButton, true, "DISCONNECT");
                changeButtonStatus(false, launchButton, false);
                changeButtonStatus(false, launchPrepareButton, true);
                changeButtonStatus(false, hookPrepareButton, true);
                changeButtonStatus(true, catapultlaunchButton, true);
                

                _fsConnect.RequestData(Requests.PlaneInfo, Definitions.PlaneInfo);
                _fsConnect.RequestData(Requests.PlaneCommit, Definitions.PlaneCommit);
            }
            else
            {
                try
                {
                    showMessage("Cloud Assistant disconnected", _fsConnect);
                    _fsConnect.Disconnect();
                    _fsConnect.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }

                _fsConnect = null;

                Application.Current.Dispatcher.Invoke(() => abortLaunch());

                changeButtonStatus(false, connectButton, true, "CONNECT");
                changeButtonStatus(false, launchButton, false);
                changeButtonStatus(false, launchPrepareButton, false);
                changeButtonStatus(false, hookPrepareButton, false);
                changeButtonStatus(false, catapultlaunchButton, false);

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
                    if (_planeInfoResponse.SimOnGround == 100 || _planeInfoResponse.AltitudeAboveGround <= _planeInfoResponse.StaticCGtoGround * 1.25)
                    {
                        lastPreparePressed = absoluteTime;
                        Console.WriteLine("Creating winch");

                        _winchPosition = _mathClass.getWinchPosition(_planeInfoResponse, assistantSettings["stringLength"]);

                        Console.WriteLine($"Current location: {_planeInfoResponse.Latitude * 180 / Math.PI} {_planeInfoResponse.Longitude * 180 / Math.PI}");
                        Console.WriteLine($"Winch location: {_winchPosition.location.Latitude * 180 / Math.PI} {_winchPosition.location.Longitude * 180 / Math.PI}");
                        Console.WriteLine($"Bearing: {_planeInfoResponse.PlaneHeading * 180 / Math.PI}deg Distance: {assistantSettings["stringLength"] / 1000}km");

                        showMessage("Winch cable connected", _fsConnect);
                        Application.Current.Dispatcher.Invoke(() => changeButtonStatus(true, launchPrepareButton, true, "Release winch cable"));
                        Application.Current.Dispatcher.Invoke(() => changeButtonStatus(true, launchButton, true));
                    }
                    else
                    {
                        showMessage("Land to connect winch cable", _fsConnect);
                    }
                }
                else // ABORT LAUNCH
                {
                    showMessage("Winch cable released. Gained altitude: " + (_planeInfoResponse.Altitude - _winchPosition.alt).ToString("0.0") + " meters", _fsConnect);
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
                _planeCommit.LightLogo = 100;
                showMessage("Launch in " + Math.Floor(launchTime - absoluteTime) + " seconds", _fsConnect);
                Application.Current.Dispatcher.Invoke(() => changeButtonStatus(true, launchButton, false));
            }
        }

        public void abortLaunch()
        {
            Console.WriteLine("abortLaunch");

            Application.Current.Dispatcher.Invoke(() => changeButtonStatus(false, launchPrepareButton, _fsConnect != null ? true : false, "Attach winch cable"));
            Application.Current.Dispatcher.Invoke(() => changeButtonStatus(false, launchButton, false, "Ready to launch"));

            if (validConnection() && launchTime != 0 && _winchPosition != null)
            {
                abortInitiated = true;
            }
            else
            {
                _planeCommit.LightWing = 100;
                _planeCommit.LightLogo = 100;
                _winchPosition = null;
                launchTime = 0;
            }
        }

        public void processLaunch()
        {
            if (abortInitiated)
            {
                _planeCommit.LightWing = 100;
                _planeCommit.LightLogo = 100;
                _winchPosition = null;
                launchTime = 0;
                abortInitiated = false;
            }
            else
            {
                // ANIMATE HANDLE
                if (_planeCommit.LightWing != 0) { _planeCommit.LightWing = 0; }
                // RESET LAUNCH HOTKEY
                if (_planeCommit.LightLogo != 100 && launchTime != 0) { _planeCommit.LightLogo = 100; }

                // GET ANGLE TO WINCH POSITION
                winchDirection _winchDirection = _mathClass.getForceDirection(_winchPosition, _planeInfoResponse);

                double diff = 0;
                // LAUNCH IN PROCESS - FIND OUT TENSION
                if (launchTime != 0 && launchTime - absoluteTime < 0)
                {
                    if (assistantSettings["stringLength"] < _winchDirection.distance) { // PULL AIRPLANE
                        double dumpingLength = assistantSettings["stringLength"] * assistantSettings["elasticExtension"] / 100;
                        diff = Math.Min(2 * 0.514 * assistantSettings["targetSpeed"], 0.514 * assistantSettings["targetSpeed"] * Math.Pow((_winchDirection.distance - assistantSettings["stringLength"]) / dumpingLength, 2)) * lastFrameTiming;
                    }

                    // SHORTEN STRING
                    if (assistantSettings["stringLength"] > 10)
                    {
                        if (-launchTime + absoluteTime < 10) // SMOOTH START
                            assistantSettings["stringLength"] -= 0.514 * assistantSettings["targetSpeed"] * lastFrameTiming * Math.Pow((-launchTime + absoluteTime) / 10, 0.5);
                        else if (_planeCommit.VelocityBodyZ > assistantSettings["targetSpeed"]) // SLOW DOWN
                            assistantSettings["stringLength"] -= 0.514 * assistantSettings["targetSpeed"] * Math.Pow(assistantSettings["targetSpeed"] / _planeCommit.VelocityBodyZ, 4)  * lastFrameTiming;
                        else
                            assistantSettings["stringLength"] -= 0.514 * assistantSettings["targetSpeed"] * lastFrameTiming;
                    }
                }

                Console.WriteLine($"Winch: {diff:F2}m/s {assistantSettings["stringLength"]:F2}m / {_winchDirection.distance:F2}m h{(_winchDirection.heading * 180 / Math.PI):F0}deg p{(_winchDirection.pitch * 180 / Math.PI):F0}deg");

                double angleLimit = 91 * Math.PI / 180;
                if (assistantSettings["enableFailure"] == 1 && diff > 10 || _winchDirection.heading < -angleLimit || _winchDirection.heading > angleLimit || _winchDirection.pitch < -angleLimit || _winchDirection.pitch > angleLimit)
                {
                    showMessage(assistantSettings["enableFailure"] == 1 && diff > 10 ?
                        "Whinch cable failure" :
                        "Winch cable released. Gained altitude: " + (_planeInfoResponse.Altitude - _winchPosition.alt).ToString("0.0") + " meters", _fsConnect);
                    abortInitiated = true;

                    Application.Current.Dispatcher.Invoke(() => abortLaunch());
                    _planeCommit.LightWing = 100;
                }
                else if (diff != 0)
                {
                    _planeCommit.VelocityBodyX += _winchDirection.localForceDirection.X * diff;
                    _mathClass.restrictAirspeed(_planeCommit.VelocityBodyX, 0.514 * assistantSettings["targetSpeed"], lastFrameTiming);
                    _planeCommit.VelocityBodyY += _winchDirection.localForceDirection.Y * diff;
                    _mathClass.restrictAirspeed(_planeCommit.VelocityBodyY, 0.514 * assistantSettings["targetSpeed"], lastFrameTiming);
                    _planeCommit.VelocityBodyZ += _winchDirection.localForceDirection.Z * diff;
                    _mathClass.restrictAirspeed(_planeCommit.VelocityBodyZ, 0.514 * assistantSettings["targetSpeed"], lastFrameTiming);
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
        // WINCH END

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
                        showMessage("Disengage parking brake to launch", _fsConnect);
                        Application.Current.Dispatcher.Invoke(() => changeButtonStatus(true, catapultlaunchButton, true, "Abort launch"));
                    }
                    else
                    {
                        showMessage("Engage parking brakes", _fsConnect);
                    }

                }
                else // ABORT LAUNCH
                {
                    showMessage("Launch aborted", _fsConnect);
                    Application.Current.Dispatcher.Invoke(() => abortLaunchpad());
                }
            }
        }
        public void abortLaunchpad()
        {
            Console.WriteLine("abortLaunchpad");
            Application.Current.Dispatcher.Invoke(() => changeButtonStatus(false, catapultlaunchButton, true, "Ready to launch"));

            if (validConnection() && targedCatapultVelocity != 0)
            {
                abortInitiated = true;
            }
            else
            {
                _planeCommit.LaunchbarPosition = 0;
                targedCatapultVelocity = 0;

            }
        }
        public void processLaunchpad()
        {
            if (abortInitiated)
            {
                _planeCommit.LaunchbarPosition = 0;
                targedCatapultVelocity = 0;
                abortInitiated = false;
            }
            else if (_planeInfoResponse.BrakeParkingPosition == 0)
            {
                // ANIMATE LAUNCHPAD
                if (_planeCommit.LaunchbarPosition != 100) { _planeCommit.LaunchbarPosition = 100; }

                // DECREASE SPEED
                if (_planeCommit.VelocityBodyZ < 0.514 * assistantSettings["catapultTargetSpeed"])
                {
                    double diff = 0.25 * (0.514 * assistantSettings["catapultTargetSpeed"] - targedCatapultVelocity);
                    targedCatapultVelocity -= diff;
                    _planeCommit.VelocityBodyZ += diff;

                    if (_planeCommit.VelocityBodyZ >= 0.9 * 0.514 * assistantSettings["catapultTargetSpeed"] || _planeInfoResponse.SimOnGround != 100)
                        abortLaunchpad();

                    Console.WriteLine("Launchpad acceleration: " + diff);
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
                Application.Current.Dispatcher.Invoke(() => changeButtonStatus(true, hookPrepareButton, true, "Retract tailhook"));
            }
            else
            {
                abortInitiated = true;
            }
        }
        public void processLanding()
        {
            if (abortInitiated)
            {
                // ANIMATE HOOK
                if (_planeCommit.TailhookPosition != 0) { _planeCommit.TailhookPosition = 0; }

                abortInitiated = false;
                _carrierPosition = null;
                arrestorConnectedTime = 0;
                //showMessage("Tailhook retracted", _fsConnect);
                Application.Current.Dispatcher.Invoke(() => changeButtonStatus(false, hookPrepareButton, true, "Extract tailhook"));
            }
            else
            {
                // ANIMATE HOOK
                if (_planeCommit.TailhookPosition != 100) { _planeCommit.TailhookPosition += 25; }

                // SET CONTACT POINT
                if (_carrierPosition.alt == 0 && _planeCommit.TailhookPosition == 100 && 
                    (_planeInfoResponse.SimOnGround == 100 || _planeInfoResponse.AltitudeAboveGround <= _planeInfoResponse.StaticCGtoGround * 1.25))
                {
                    _carrierPosition.alt = _planeInfoResponse.Altitude;
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

                    double diff = 0;
                    // ARREST IN PROCESS - FIND OUT TENSION
                    diff = 1.1 * (_planeCommit.VelocityBodyZ * lastFrameTiming / (timeLeft / 1.5) + (accel > 0 ? accel : 0));

                    //Console.WriteLine("TAIL HOOK " + (_planeInfoResponse.AltitudeAboveGround - _planeInfoResponse.StaticCGtoGround));
                    Console.WriteLine($"String: diff{diff / lastFrameTiming:F2} timeLeft{timeLeft:F2} {_carrierDirection.distance:F2}m h{(_carrierDirection.heading * 180 / Math.PI):F0}deg p{(_carrierDirection.pitch * 180 / Math.PI):F0}deg");

                    if (timeLeft <= 0 || assistantSettings["enableFailure"] == 1 && diff / lastFrameTiming > 100)
                    {
                        showMessage(
                            timeLeft <= 0 ? "Arresting cable released. Distance: " + _carrierDirection.distance.ToString("0.0") + " meters" :
                            "Arresting cable failure", _fsConnect);
                        abortInitiated = true;
                        _planeCommit.TailhookPosition = 50;
                    }
                    else if (diff != 0 && _carrierDirection.localForceDirection.Norm > 0)
                    {
                        _planeCommit.VelocityBodyX += _carrierDirection.localForceDirection.X * diff;
                        _planeCommit.VelocityBodyY += _carrierDirection.localForceDirection.Y * diff;
                        _planeCommit.VelocityBodyZ += _carrierDirection.localForceDirection.Z * diff;

                        // REMOVE VERTICAL VELOCITY
                        if (_planeCommit.VelocityBodyY > 0.1)
                            _planeCommit.VelocityBodyY = -0.33 * _planeCommit.VelocityBodyY;
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

        public void HandleReceivedFsData(object sender, FsDataReceivedEventArgs e)
        {
            try
            {
                // COMMON DATA
                if (e.RequestId == (uint)Requests.PlaneInfo)
                {
                    //Console.WriteLine(JsonConvert.SerializeObject(e.Data, Formatting.Indented));
                    _planeInfoResponse = (PlaneInfoResponse)e.Data;

                    absoluteTime = _planeInfoResponse.AbsoluteTime;

                    checkTowHandleState(_planeInfoResponse.LightWing);
                }
                // LAUNCH DATA
                else if (e.RequestId == (uint)Requests.PlaneCommit)
                {
                    //Console.WriteLine(JsonConvert.SerializeObject(e.Data, Formatting.Indented));
                    _planeCommit = (PlaneInfoCommit)e.Data;

                    absoluteTime = _planeCommit.AbsoluteTime;
                    lastFrameTiming = absoluteTime - swLast;

                    // APPLY PHYSICS IS LAUNCH ACTIVE
                    if (lastFrameTiming != 0 && _winchPosition != null)
                    {
                        // CALCULATE PHYSICS
                        processLaunch();

                        if (launchTime > absoluteTime) // COUNT
                        {
                            /*if (Math.Floor(launchTime - swCurrent) < Math.Floor(launchTime - swLast)) // RENDER MESSAGE
                            {
                                if (launchTime - swCurrent > 1)
                                    showMessage("Launch in " + Math.Floor(launchTime - swCurrent), _fsConnect);
                                else
                                    showMessage("Launch!", _fsConnect);
                            }*/
                        }
                    }
                    else if (_carrierPosition != null)
                    {
                        processLanding();
                    }
                    else if (targedCatapultVelocity != 0)
                    {
                        processLaunchpad();
                    }

                    swLast = absoluteTime;

                    checkTowHandleState(_planeCommit.LightWing);
                    checkLaunchState(_planeCommit.LightLogo);

                    _planeCommitLast = _planeCommit;
                    _planeInfoResponseLast = _planeInfoResponse;
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

        public void checkTowHandleState(double state)
        {
            /*if (state == 0 && _winchPosition == null ||
                state != 0 && _winchPosition != null && absoluteTime - 1 > lastPreparePressed)
            {
                Console.WriteLine("Toggle string connection");
                Application.Current.Dispatcher.Invoke(() => toggleLaunchPrepare(new object(), new RoutedEventArgs()));
            }*/
        }

        public void checkLaunchState(double state)
        {
            /*if (state == 0 && launchTime == 0 && _winchPosition != null)
            {
                Console.WriteLine("Toggle launch");
                Application.Current.Dispatcher.Invoke(() => initiateLaunch(new object(), new RoutedEventArgs()));
            }*/
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
            definition.Add(new SimProperty(FsSimVar.LightWing, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.LightLogo, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.LightRecognition, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.SimOnGround, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.BrakeParkingPosition, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));

            fsConnect.RegisterDataDefinition<PlaneInfoResponse>(Definitions.PlaneInfo, definition);

            List<SimProperty> cDefinition = new List<SimProperty>();
            cDefinition.Add(new SimProperty(FsSimVar.Title, FsUnit.None, SIMCONNECT_DATATYPE.STRING256));
            cDefinition.Add(new SimProperty(FsSimVar.VelocityBodyX, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.VelocityBodyY, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.VelocityBodyZ, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            //cDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyX, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            //cDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyY, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            //cDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyZ, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.AbsoluteTime, FsUnit.Second, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.LightWing, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.LightLogo, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.LightRecognition, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.TailhookPosition, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.LaunchbarPosition, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            fsConnect.RegisterDataDefinition<PlaneInfoCommit>(Definitions.PlaneCommit, cDefinition);
        }

        public void showMessage(string text, FsConnect _fsConnect)
        {
            if (assistantSettings["displayTips"] == 1)
            {
                _fsConnect.SetText(text, 1);
            }

            Console.WriteLine(text);
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
                        ((ComboBox)window.FindName(tb.Name)).Text = assistantSettings[tb.Name].ToString();
                    }
                    else
                    {
                        double.TryParse(tb.Text, out double value);
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
                    else
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
                    else
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
    }
}