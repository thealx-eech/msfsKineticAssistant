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
using System.Globalization;
using System.Windows.Media.Imaging;
using System.Net;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;

//-TOW RELEASE HANDLE
//=FOLDING WING LEFT PERCENT
//=FOLDING WING RIGHT PERCENT
//+WATER RUDDER HANDLE POSITION
//?CANOPY OPEN
//+TAILHOOK POSITION
//?EXIT OPEN:index


namespace MSFS_Kinetic_Assistant
{
    public partial class MainWindow : System.Windows.Window
    {
        private FsConnect _fsConnect;
        private PlaneInfoResponse _planeInfoResponse;
        private PlaneInfoResponse _planeInfoResponseLast;
        private PlaneInfoCommit _planeCommit;
        private PlaneInfoCommit _planeCommitLast;
        private PlaneInfoRotate _planeRotate;
        private PlaneEngineData _planeEngineData;
        private Dictionary<uint, winchPosition> _nearbyInfoResponse;
        private Dictionary<uint, winchPosition> _nearbyInfoResponseLast;
        private Dictionary<uint, winchPosition> _nearbyInfoResponsePreLast;
        private WeatherReport _weatherReport;

        // IF NOT NULL - READY TO LAUNCH
        private winchPosition _winchPosition = null;
        private winchPosition _carrierPosition = null;

        // IF NOT 0 - WINCH WORKING
        private double launchTime = 0;
        private double cableLength = 0;
        private double cableLengthPrev = 0;
        private double cableLengthPrePrev = 0;

        // IF NOT 0 - ARRESTER CONNECTED
        private double arrestorConnectedTime = 0;

        // IF NOT 0 - CATAPULT LAUNCH IN PROCESS
        private double targedCatapultVelocity = 0;

        // IF NOT FALSE - THERMALS CALCULATION IN PROCESS
        private bool thermalsWorking = false;
        private byte thermalsDebugActive = 0;
        private GeoLocation apiThermalsLoadedPosition = null;
        double apiThermalsLoadedTime = 0;
        private List<winchPosition> thermalsList = new List<winchPosition>();
        private List<winchPosition> thermalsListAPI = new List<winchPosition>();
        private double windDirection;
        private double windVelocity;
        private double dayTimeModifier;
        private double overcastModifier;
        private int scaleRefresh = 0;

        // IF NOT 0 - TOWING ACTIVE
        private TowScanMode towScanMode = TowScanMode.Disabled;
        private double towToggledAltitude;
        private static uint TARGETMAX = 99999999;
        private uint towingTarget = TARGETMAX;
        private double lightToggled = 0;
        private KeyValuePair<uint, bool> insertedTowPlane = new KeyValuePair<uint, bool>(TARGETMAX, false);
        private double towCableLength = 0;
        private double towPrevDist = 0;
        private double towPrePrevDist = 0;
        private double towCableDesired = 40;
        private double insertTowPlanePressed = 0;
        private double AIholdInterval = 8;
        private enum TowScanMode
        {
            Disabled = 0,
            Scan = 1,
            TowSearch = 2,
            TowInsert = 3
        }
        /*
         * 1. Insert pressed
         * 2. scan enabled
         * 3. 2sec delay - searching for existing AI tow plane
         * 4. if not found, 4sec delay - creating new AI plane
         * 5. capture inserted AI plane
         * 6. 2sec delay - searching for inserted AI plane
         * 7. if not found and captured - move to the player position
        */



        bool winchAbortInitiated = false;
        bool launchpadAbortInitiated = false;
        bool arrestorsAbortInitiated = false;

        private double absoluteTime;
        private double swLast;
        private double lastFrameTiming;
        DispatcherTimer dispatcherTimer;
        DispatcherTimer launchTimer;
        private double lastPreparePressed = 0;

        string optPath;
        string plnPath;

        private bool loaded = false;
        MathClass _mathClass;
        RadarClass _radarClass;

        Dictionary<string, double> assistantSettings;
        Dictionary<string, double> controlTimestamps;

        MediaPlayer soundPlayer = null;

        bool taskInProcess = false;

        private Server server;
        private Thread threadServer;

        // RADAR
#if DEBUG
        double allowedRadarScale = 5;
#else
        double allowedRadarScale = 50;
#endif
        double maxRadarScale = 50;

        public MainWindow()
        {
            DataContext = new SimvarsViewModel();
            _mathClass = new MathClass();
            _radarClass = new RadarClass();
            assistantSettings = new Dictionary<string, double>();

            // PREPARE CONTROLS DATA
            controlTimestamps = new Dictionary<string, double>();
            foreach (var field in typeof(PlaneInfoResponse).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (field.FieldType == typeof(double))
                    controlTimestamps.Add(field.Name, 0);

            InitializeComponent();

            this.Title = ((AssemblyTitleAttribute)Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyTitleAttribute), false)).Title;
#if !DEBUG
            this.Title += "+";
#endif
            this.Title += " " + Assembly.GetExecutingAssembly().GetName().Version.ToString();
            AppName.Text = this.Title;

            // COMMUNITY CHECK
            if (Assembly.GetEntryAssembly().Location.Contains("\\Community\\"))
            {
                MessageBox.Show("You have installed Kinetic Assistant inside of Community folder, please move files outside of this folder to avoid MSFS issues", "..\\Community\\.. - Unsupported location");
                Application.Current.Shutdown();
            }

            optPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalState\";
            if (!Directory.Exists(optPath))
                optPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\Microsoft Flight Simulator\";
            if (!Directory.Exists(optPath))
                optPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\PackagesMicrosoft.FlightDashboard_8wekyb3d8bbwe\LocalState\";

            addLogMessage("MSFS Path: " + optPath);

            addServerIPs();

            loadSettings();
            loaded = true;

            _ = CheckUpdateAsync();

            if (assistantSettings.ContainsKey("nmeaServer") && assistantSettings["nmeaServer"] != 0)
            {
                ServerStart(((ComboBoxItem)nmeaServer.SelectedItem).Tag.ToString());
            }

            // COMMON SIM VALUES REQUEST TIMER
            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 250);
            dispatcherTimer.Tick += new EventHandler(commonInterval);
            dispatcherTimer.Start();

            launchTimer = new DispatcherTimer();
            launchTimer.Interval = new TimeSpan(0, 0, 0, 0, 1000 / Math.Max(5, (int)(assistantSettings.ContainsKey("RequestsFrequency") ? assistantSettings["RequestsFrequency"] : 5)));
            launchTimer.Tick += new EventHandler(launchInterval);
            launchTimer.Start();

            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 1);
            dispatcherTimer.Tick += new EventHandler(nearbyInterval);
            dispatcherTimer.Start();

            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 10, 33);
            dispatcherTimer.Tick += new EventHandler(weatherInterval);
            dispatcherTimer.Start();

            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Interval = new TimeSpan(1, 0, 0);
            dispatcherTimer.Tick += new EventHandler(triggerCheckUpdate);
            dispatcherTimer.Start();
        }

        private void Window_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            Application.Current.MainWindow.Topmost = assistantSettings["alwaysOnTop"] == 1;
        }
        private void commonInterval(object sender, EventArgs e)
        {
            if (validConnection())
            {
                if (_winchPosition == null && _carrierPosition == null && targedCatapultVelocity == 0 && !thermalsWorking && towingTarget == TARGETMAX && !taskInProcess)
                    _fsConnect.RequestData(Requests.PlaneInfo, Definitions.PlaneInfo);

                if (towScanMode > TowScanMode.Disabled && towingTarget == TARGETMAX)
                    _fsConnect.RequestData(Requests.NearbyObjects, Definitions.NearbyObjects, (uint)(1000 * Math.Min(allowedRadarScale, (assistantSettings.ContainsKey("RadarScale") ? assistantSettings["RadarScale"] : allowedRadarScale))), getTowObjectType());

                if (taskInProcess)
                    _fsConnect.RequestData(Requests.PlaneEngineData, Definitions.PlaneEngineData);
            }
        }

        private void launchInterval(object sender, EventArgs e)
        {
            if (validConnection())
            {
                if (_winchPosition != null || _carrierPosition != null || targedCatapultVelocity != 0 || thermalsWorking || towingTarget != TARGETMAX || taskInProcess)
                {
                    _fsConnect.RequestData(Requests.PlaneInfo, Definitions.PlaneInfo);
                    _fsConnect.RequestData(Requests.PlaneCommit, Definitions.PlaneCommit);
                    _fsConnect.RequestData(Requests.PlaneRotate, Definitions.PlaneRotate);
                }

                if (towScanMode > TowScanMode.Disabled && towingTarget != TARGETMAX)
                    _fsConnect.RequestData(Requests.NearbyObjects, Definitions.NearbyObjects, (uint)(1000 * Math.Min(allowedRadarScale, assistantSettings["RadarScale"])), getTowObjectType());
            }
        }

        private void weatherInterval(object sender, EventArgs e)
        {
            if (validConnection())
            {
                _fsConnect.RequestData(Requests.WeatherData, Definitions.WeatherData);
            }
        }

        private SIMCONNECT_SIMOBJECT_TYPE getTowObjectType()
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

        private void toggleConnect(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("toggleConnect");

            if (!validConnection())
            {
                Application.Current.Dispatcher.Invoke(() => _radarClass.InitRadar(RadarCanvas, assistantSettings["RadarScale"] / maxRadarScale));

                try
                {
                    _fsConnect = new FsConnect();
                    _fsConnect.Connect("Kinetic Assistant", "127.0.0.1", 500, SimConnectProtocol.Ipv4);
                }
                catch (Exception ex)
                {
                    addLogMessage(ex.Message);
                    return;
                }

                _fsConnect.FsDataReceived += HandleReceivedFsData;
                //_fsConnect.AirportDataReceived += HandleReceivedAirports;
                _fsConnect.ObjectAddremoveEventReceived += HandleReceivedSystemEvent;
                //_fsConnect.SystemEventReceived += HandleReceivedSystemEvent;

                Console.WriteLine("Initializing data definitions");
                InitializeDataDefinitions(_fsConnect);

                showMessage("Kinetic Assistant connected", _fsConnect);
                Application.Current.Dispatcher.Invoke(() => playSound("true"));
                changeButtonStatus(false, connectButton, true, "DISCONNECT");
                changeButtonStatus(true, launchPrepareButton, true);
                changeButtonStatus(true, hookPrepareButton, true);
                changeButtonStatus(true, catapultlaunchButton, true);
                changeButtonStatus(true, thermalsToggleButton, true, "Enable thermals");
                changeButtonStatus(true, towToggleButton, true);
                changeButtonStatus(true, towConnectButton, false);
                changeButtonStatus(true, towInsertButton, true);
                RadarScale.Maximum = maxRadarScale;

                _fsConnect.RequestData(Requests.PlaneInfo, Definitions.PlaneInfo);
                _fsConnect.RequestData(Requests.PlaneCommit, Definitions.PlaneCommit);
                //_fsConnect.RequestFacilitiesList(Requests.Airport);
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() => _radarClass.ClearRadar(RadarCanvas));

                if (towScanMode > TowScanMode.Disabled)
                {
                    toggleScanning(null, null);
                }

                try
                {
                    showMessage("Kinetic Assistant disconnected", _fsConnect);
                    //Application.Current.Dispatcher.Invoke(() => playSound("error"));
                    _fsConnect.Disconnect();
                    _fsConnect.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    addLogMessage(ex.Message, 2);
                }

                _fsConnect = null;

                Application.Current.Dispatcher.Invoke(() => abortLaunch());

                changeButtonStatus(true, connectButton, true, "CONNECT");
                changeButtonStatus(false, launchPrepareButton, false);
                changeButtonStatus(false, hookPrepareButton, false);
                changeButtonStatus(false, catapultlaunchButton, false);
                changeButtonStatus(false, thermalsToggleButton, false, "Disable thermals");
                changeButtonStatus(true, towToggleButton, false);
                changeButtonStatus(true, towInsertButton, false);

                Console.WriteLine("Disconnected");
            }
        }

        private bool validConnection()
        {
            if (_fsConnect == null/* || !_fsConnect.Connected*/)
                return false;
            else
                return true;
        }


        // WINCH START
        private void toggleLaunchPrepare(object sender, RoutedEventArgs e)
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
                    if (_planeInfoResponse.BrakeParkingPosition == 100 && (assistantSettings["realisticRestrictions"] == 0 || _planeInfoResponse.SimOnGround == 100 || _planeInfoResponse.AltitudeAboveGround <= _planeInfoResponse.StaticCGtoGround * 1.25))
                    {
                        lastPreparePressed = absoluteTime;
                        addLogMessage("Creating winch");

                        cableLength = assistantSettings["stringLength"];
                        _winchPosition = _mathClass.getWinchPosition(_planeInfoResponse, cableLength - 10);

                        Application.Current.Dispatcher.Invoke(() => _radarClass.InsertWinch(RadarCanvas));
                        Application.Current.Dispatcher.Invoke(() => RadarScale.Value = Math.Min(maxRadarScale, cableLength / 1000 * 1.2));

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
        private void initiateLaunch(object sender, RoutedEventArgs e)
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

        private void abortLaunch()
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
                Application.Current.Dispatcher.Invoke(() => _radarClass.RemoveWinch(RadarCanvas));
                launchTime = 0;
                cableLength = 0;
            }
        }

        private void processLaunch()
        {
            bool applyForces;

            if (winchAbortInitiated)
            {
                _winchPosition = null;
                Application.Current.Dispatcher.Invoke(() => _radarClass.RemoveWinch(RadarCanvas));
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
                double bodyAcceleration = 0;

                if (thermalsDebugActive > 0)
                    Application.Current.Dispatcher.Invoke(() => _radarClass.UpdateWinch(RadarCanvas, _winchDirection, Math.Min(maxRadarScale, assistantSettings["RadarScale"])));

                // GET DRAFT CABLE TENSION
                double accelerationLimit = (assistantSettings["realisticFailures"] == 1 ? 6 : 30) * 9.81;
                double cableTension = _mathClass.getCableTension(cableLength, Math.Max(1, assistantSettings["elasticExtension"] / 2), _winchDirection);

                // SHORTEN THE STRING
                if (launchTime != 0 && launchTime - absoluteTime < 0 && cableLength > 10)
                {
                    double pitchCompensation = Math.Pow(Math.Abs(Math.Cos(_winchDirection.climbAngle)), 1.25);
                    double tensionMultiplier = 1 - Math.Pow(Math.Min(1, cableTension / 2), 2);

                    double timePassed = -launchTime + absoluteTime;
                    double StartTime = Math.Max(assistantSettings["winchSpeedUp"], 3);

                    if (timePassed < StartTime) // SMOOTH START
                    {
                        cableLength -= timePassed / StartTime * tensionMultiplier * pitchCompensation * targetVelocity * lastFrameTiming;
                    }
                    else
                    {
                        cableLength -= tensionMultiplier * pitchCompensation * targetVelocity * lastFrameTiming;
                    }

                    // GET FINAL CABLE TENSION
                    cableTension = _mathClass.getCableTension(cableLength, Math.Max(1, assistantSettings["elasticExtension"] / 2), _winchDirection);
                }

                // LEVEL UP GLIDER BEFORE LAUNCH
                if (_planeInfoResponse.SimOnGround == 100 && launchTime != 0 && _planeInfoResponse.AirspeedIndicated < 5 )
                {
                    levelUpGlider();
                }

                if (cableTension > 0)
                {
                    bodyAcceleration = _mathClass.getBodyVelocity(_winchDirection, _planeCommit, cableTension, accelerationLimit, -cableLength, -cableLengthPrev, -cableLengthPrePrev, lastFrameTiming);
                }

                applyForces = applyWinchForces(bodyAcceleration, accelerationLimit, _winchDirection, targetVelocity, "Winch", assistantSettings["connectionPoint"]);

                cableLengthPrePrev = cableLengthPrev;
                cableLengthPrev = cableLength;
            }

            if (applyForces)
            {
                if (!double.IsNaN(_planeCommit.VelocityBodyX) && !double.IsNaN(_planeCommit.VelocityBodyY) && !double.IsNaN(_planeCommit.VelocityBodyZ))
                {
                    try
                    {
                        _fsConnect.UpdateData(Definitions.PlaneCommit, _planeCommit);
                    }
                    catch (Exception ex)
                    {
                        addLogMessage(ex.Message);
                    }
                }
            }
        }

        public void levelUpGlider()
        {
            _planeRotate.RotationVelocityBodyX = 0;
            _planeRotate.RotationVelocityBodyY = 0;
            _planeRotate.RotationVelocityBodyZ = -Math.Sin(_planeInfoResponse.PlaneBank) * Math.Pow(Math.Abs(Math.Sin(_planeInfoResponse.PlaneBank)), 0.5);

            Console.WriteLine($"Leveling {_planeRotate.RotationVelocityBodyZ:F5}");

            if (!double.IsNaN(_planeRotate.RotationVelocityBodyX) && !double.IsNaN(_planeRotate.RotationVelocityBodyY) && !double.IsNaN(_planeRotate.RotationVelocityBodyZ))
            {
                try
                {
                    _fsConnect.UpdateData(Definitions.PlaneRotate, _planeRotate);
                }
                catch (Exception ex)
                {
                    addLogMessage(ex.Message);
                }
            }
        }

        private bool applyWinchForces(double bodyAcceleration, double accelerationLimit, winchDirection _winchDirection, double targetVelocity, string type, double connectionPoint)
        {
            if (double.IsNaN(bodyAcceleration) || lastFrameTiming == 0)
            {
                return false;
            }

            // RANDOM FAILURE
            if (assistantSettings["randomFailures"] > 0 && Math.Abs(bodyAcceleration) > 0)
            {
                double rnd = new Random().NextDouble();
                double thrs = Math.Pow(10, assistantSettings["randomFailures"]) * lastFrameTiming * Math.Abs(bodyAcceleration) / accelerationLimit / 1000;
                //Console.WriteLine(rnd + " / " + thrs);

                if (rnd < thrs)
                {
                    bodyAcceleration = 1.1 * accelerationLimit;
                }
            }


            if (type == "Tow" && (towCableLength > towCableDesired || towCableLength < towCableDesired - 1))
            {
                Console.WriteLine("Shortening tow rope from " + towCableLength + " to " + towCableDesired);

                towCableLength -= lastFrameTiming * (towCableLength > towCableDesired ? 1 : -1);
                towPrevDist -= lastFrameTiming * (towCableLength > towCableDesired ? 1 : -1);
                towPrePrevDist -= lastFrameTiming * (towCableLength > towCableDesired ? 1 : -1);
            }

            Console.WriteLine($"{type}: {bodyAcceleration / 9.81:F2}g {(type == "Winch" ? cableLength : towCableLength):F2}m / {_winchDirection.distance:F2}m h{(_winchDirection.heading * 180 / Math.PI):F0}deg p{(_winchDirection.pitch * 180 / Math.PI):F0}deg");

            double angleHLimit = (_planeInfoResponse.SimOnGround == 0 ? 89 : 179) * Math.PI / 180;
            double angleVLimit = 69 * Math.PI / 180;

            // WHAT IS THIS? SKIP!
            if (Math.Abs(bodyAcceleration) > 1000 * accelerationLimit || (insertTowPlanePressed != 0 && _planeInfoResponse.AbsoluteTime - insertTowPlanePressed < AIholdInterval))
            {
                Console.WriteLine("SKIP ITERATION: " + bodyAcceleration + " / " + accelerationLimit);
                return false;
            }
            // FAIL
            else if (Math.Abs(bodyAcceleration) > accelerationLimit || Math.Abs(_winchDirection.heading) > angleHLimit || Math.Abs(_winchDirection.pitch) > angleVLimit)
            {
                if (type == "Winch")
                {
                    showMessage(bodyAcceleration > accelerationLimit ?
                        type + " cable failure" :
                        type + " cable released. Gained altitude: " + (_planeInfoResponse.Altitude - _winchPosition.alt).ToString("0.0") + " meters", _fsConnect);

                    Application.Current.Dispatcher.Invoke(() => playSound("false"));
                    winchAbortInitiated = true;
                    Application.Current.Dispatcher.Invoke(() => abortLaunch());
                }
                else if (type == "Tow" && towingTarget != TARGETMAX)
                {
                    toggleTowCable(towingTarget);
                }
            }
            else
            {
                _planeCommit.VelocityBodyX += _winchDirection.localForceDirection.X * bodyAcceleration * lastFrameTiming;
                _mathClass.restrictAirspeed(_planeCommit.VelocityBodyX, targetVelocity, lastFrameTiming);
                _planeCommit.VelocityBodyY += _winchDirection.localForceDirection.Y * bodyAcceleration * lastFrameTiming;
                _mathClass.restrictAirspeed(_planeCommit.VelocityBodyY, targetVelocity, lastFrameTiming);
                _planeCommit.VelocityBodyZ += _winchDirection.localForceDirection.Z * bodyAcceleration * lastFrameTiming;
                _mathClass.restrictAirspeed(_planeCommit.VelocityBodyZ, targetVelocity, lastFrameTiming);

                // PROCESS NOSE CONNECTION POINT
                double degreeThershold = (_planeInfoResponse.SimOnGround == 0 ? 20 : 1) * Math.PI / 180;
                double accelThreshold = 9.81 / (_planeInfoResponse.SimOnGround == 0 ? 5 : 1000);

                if (bodyAcceleration > accelThreshold && connectionPoint != 0 && (Math.Abs(_winchDirection.pitch) > degreeThershold || Math.Abs(_winchDirection.heading) > degreeThershold))
                {
                    double rotationForce = 10 * (bodyAcceleration - accelThreshold) / accelerationLimit;

                    double sinHeading = Math.Sign(_winchDirection.heading) * Math.Pow(Math.Abs(Math.Sin(_winchDirection.heading / 2)), 1.5);
                    double sinPitch = Math.Sign(_winchDirection.pitch) * Math.Pow(Math.Abs(Math.Sin(_winchDirection.pitch / 2)), 1.5);

                    //Console.WriteLine("Math.Sign(_winchDirection.pitch)" + Math.Sign(_winchDirection.pitch) + " Math.Sin(_winchDirection.pitch / 2):" + Math.Sin(_winchDirection.pitch / 2) + " Math.Pow(Math.Sin(_winchDirection.pitch / 2), 1.5):" + Math.Pow(Math.Sin(_winchDirection.pitch / 2), 1.5));
                    //Console.WriteLine("_planeRotate.RotationVelocityBodyX:" + _planeRotate.RotationVelocityBodyX + " rotationForce:" + rotationForce + " rotationForce:" + rotationForce + " sinPitch:" + sinPitch + " lastFrameTiming:" + lastFrameTiming);

                    _planeRotate.RotationVelocityBodyX = (_planeRotate.RotationVelocityBodyX - rotationForce * sinPitch) * lastFrameTiming;
                    _planeRotate.RotationVelocityBodyY = (_planeRotate.RotationVelocityBodyY + (_planeInfoResponse.SimOnGround == 0 ? rotationForce : 10 * Math.Pow(Math.Abs(rotationForce / 10), 0.1)) * sinHeading)
                        * lastFrameTiming;
                    _planeRotate.RotationVelocityBodyZ = (_planeRotate.RotationVelocityBodyZ + lastFrameTiming * rotationForce * sinHeading) * lastFrameTiming;

                    Console.WriteLine($"Pitch {_planeRotate.RotationVelocityBodyX:F2} Heading {_planeRotate.RotationVelocityBodyY:F2}");

                    if (!double.IsNaN(_planeRotate.RotationVelocityBodyX) && !double.IsNaN(_planeRotate.RotationVelocityBodyY) && !double.IsNaN(_planeRotate.RotationVelocityBodyZ))
                    {
                        try
                        {
                            _fsConnect.UpdateData(Definitions.PlaneRotate, _planeRotate);
                        }
                        catch (Exception ex)
                        {
                            addLogMessage(ex.Message);
                        }
                    }
                }

                return true;
            }

            return false;
        }
        // WINCH END

        // TOW START
        private void toggleScanning(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("toggleScanning");

            _nearbyInfoResponse = new Dictionary<uint, winchPosition>();
            _nearbyInfoResponseLast = new Dictionary<uint, winchPosition>();
            _nearbyInfoResponsePreLast = new Dictionary<uint, winchPosition>();

            if (!validConnection())
            {
                Console.WriteLine("connection lost");
            }
            else
            {
                if (towScanMode == TowScanMode.Disabled) // START SEARCH
                {
                    towScanMode = TowScanMode.Scan;
                    Application.Current.Dispatcher.Invoke(() => changeButtonStatus(false, towToggleButton, true, "STOP"));
                    Application.Current.Dispatcher.Invoke(() => changeButtonStatus(true, towConnectButton, true, "Attach To Closest"));

                    toggleSidebarWindow(true);
                }
                else // STOP SEARCH
                {
                    towScanMode = TowScanMode.Disabled;
                    if (towingTarget != TARGETMAX)
                    {
                        toggleTowCable(towingTarget);
                    }

                    Application.Current.Dispatcher.Invoke(() => _radarClass.clearRadarNearby(RadarCanvas));

                    _nearbyInfoResponse = new Dictionary<uint, winchPosition>();

                    Application.Current.Dispatcher.Invoke(() => nearbyObjects.Children.Clear());
                    Application.Current.Dispatcher.Invoke(() => farObjects.Children.Clear());
                    Application.Current.Dispatcher.Invoke(() => currentTarget.Children.Clear());
                    Application.Current.Dispatcher.Invoke(() => changeButtonStatus(true, towToggleButton, true, "SEARCH"));
                    Application.Current.Dispatcher.Invoke(() => changeButtonStatus(true, towConnectButton, false, "Attach To Closest"));

                    //toggleSidebarWindow(false);
                }
            }
        }

        private void toggleTowClosest(object sender, EventArgs e)
        {
            attachTowCable(null, new EventArgs());
        }

        private void aiTowPlane(object sender, EventArgs e)
        {
            if (validConnection())
            {
                if (assistantSettings["realisticRestrictions"] == 0 || _planeInfoResponse.BrakeParkingPosition == 0 && (_planeInfoResponse.SimOnGround == 100 || _planeInfoResponse.AltitudeAboveGround <= _planeInfoResponse.StaticCGtoGround * 1.25))
                {
                    if (File.Exists(optPath + @"MISSIONS\Custom\CustomFlight\CUSTOMFLIGHT.PLN"))
                    {
                        plnPath = optPath + @"MISSIONS\Custom\CustomFlight\CUSTOMFLIGHT";
                    }
                    else if (assistantSettings["realisticTowProcedures"] != 0)
                    {
                        plnPath = "";
                        showMessage("Flight plan CUSTOMFLIGHT does not exist", _fsConnect);
                        return;
                    }

                    if (towScanMode == TowScanMode.Disabled)
                    {
                        toggleScanning(null, null);
                    }

                    if (towScanMode > TowScanMode.Disabled)
                    {
                        //towSearchRadius.SelectedIndex = 3;
                        RadarScale.Value = 0.3;
                        towScanType.SelectedIndex = 1;
                        towToggledAltitude = _planeInfoResponse.Altitude;

                        towScanMode = TowScanMode.TowInsert;
                        insertedTowPlane = new KeyValuePair<uint, bool>(TARGETMAX, false);
                        insertTowPlanePressed = _planeInfoResponse.AbsoluteTime;
                    }
                }
                else
                {
                    showMessage("Tow plane can be inserted only on the ground and with brakes disengaged", _fsConnect);
                }
            }
        }

        private void insertTowPlane()
        {
            if (towScanMode == TowScanMode.Disabled)
            {
                toggleScanning(null, null);
            }

            if (towScanMode > TowScanMode.Disabled)
            {
                addLogMessage("Inserting AI tow plane");

                if (assistantSettings["realisticTowProcedures"] == 0)
                {
                    GeoLocation newPlaneLocation = _mathClass.FindPointAtDistanceFrom(new GeoLocation(_planeInfoResponse.Latitude, _planeInfoResponse.Longitude), _planeInfoResponse.PlaneHeading, 0.08);

                    SIMCONNECT_DATA_INITPOSITION position = new SIMCONNECT_DATA_INITPOSITION();
                    //position.Altitude = 99999;
                    position.Latitude = newPlaneLocation.Latitude * 180 / Math.PI;
                    position.Longitude = newPlaneLocation.Longitude * 180 / Math.PI;
                    //position.Airspeed = 0;
                    //position.Pitch = -10;

                    _fsConnect.CreateNonATCAircraft(position, towPlaneModel.Text, Requests.TowPlane);
                }
                else
                {
                    _fsConnect.CreateEnrouteATCAircraft(plnPath, towPlaneModel.Text, 0, Requests.TowPlane);
                }

                //_fsConnect.LoadParkedATCAircraft("EDWW", towPlaneModel.Text, Requests.TowPlane);
            }
        }

        /*private void createFlightPlan()
        {
            // FIND CLOSESD ICAO - BROKEN!!!
            KeyValuePair<double, SIMCONNECT_DATA_FACILITY_AIRPORT> closesAirport = new KeyValuePair<double, SIMCONNECT_DATA_FACILITY_AIRPORT>();

            foreach (SIMCONNECT_DATA_FACILITY_AIRPORT airport in airportsArray)
            {
                double globalX = (airport.Longitude / 180 * Math.PI - _planeInfoResponse.Longitude) * Math.Cos(airport.Latitude / 180 * Math.PI) * 6378137;
                double globalZ = (airport.Latitude / 180 * Math.PI - _planeInfoResponse.Latitude) * 180 / Math.PI * 111694;
                double distance = Math.Pow(globalX * globalX + globalZ * globalZ, 0.5);

                if (airport.Icao == "LFPG")
                {
                    Console.WriteLine(airport.Icao + " " + distance);
                }

                if (closesAirport.Key == 0 || distance < closesAirport.Key)
                {
                    closesAirport = new KeyValuePair<double, SIMCONNECT_DATA_FACILITY_AIRPORT>(distance, airport);
                }
            }

            Console.WriteLine("Closest airport - " + closesAirport.Value.Icao + " distance " + closesAirport.Key);
        }*/

        private void assignTowPlane(uint id, winchPosition pos)
        {
            winchDirection direction = _mathClass.getForceDirection(pos, _planeInfoResponse);

            double menuValue = 0.2;
            towCableDesired = 40;
            double cableLength = towCableDesired;

            Console.WriteLine("assignTowPlane in " + direction.distance + "m");

            if (assistantSettings["realisticTowProcedures"] == 0)
            {
                //towSearchRadius.Text = menuValue.ToString(".0");
                RadarScale.Value = menuValue;
                towCableDesired *= 2;
                cableLength = towCableDesired - 10;
                //teleportTowPlane(id);
                //return;
            }
            else if (direction.distance + 10 < towCableDesired)
            {
                //towSearchRadius.Text = menuValue.ToString(".0");
                RadarScale.Value = menuValue;
            }
            else
            {
                cableLength = direction.distance;
                menuValue = (cableLength + 10) * 1.5;
                //towSearchRadius.Text = menuValue.ToString(".0");
                RadarScale.Value = menuValue;
            }

            insertedTowPlane = new KeyValuePair<uint, bool>(id, true);
            toggleTowCable(id, pos, cableLength);
            //assignFlightPlan(id);

            //showMessage("AI tow plane assigned", _fsConnect);
        }

        private void teleportTowPlane(uint id)
        {
            GeoLocation newPlaneLocation = _mathClass.FindPointAtDistanceFrom(new GeoLocation(_planeInfoResponse.Latitude, _planeInfoResponse.Longitude), _planeInfoResponse.PlaneHeading, 0.08);

            TowInfoResponse towInfo = new TowInfoResponse();
            towInfo.Altitude = _planeInfoResponse.Altitude;
            towInfo.Latitude = newPlaneLocation.Latitude;
            towInfo.Longitude = newPlaneLocation.Longitude;
            towInfo.Heading = _planeInfoResponse.PlaneHeading;
            towInfo.Bank = 0;

            assignFlightPlan(id);

            Console.WriteLine("Tow plane teleported");

            if (!double.IsNaN(towInfo.Altitude) && !double.IsNaN(towInfo.Latitude) && !double.IsNaN(towInfo.Longitude) && !double.IsNaN(towInfo.Heading) && !double.IsNaN(towInfo.Bank))
            {
                try
                {
                    _fsConnect.UpdateData(Definitions.TowPlane, towInfo, id);
                }
                catch (Exception ex)
                {
                    addLogMessage(ex.Message);
                }
            }
        }

        private void assignFlightPlan(uint id)
        {
            if (plnPath != "")
                _fsConnect.AISetAircraftFlightPlan(id, plnPath, Definitions.TowPlane);
        }

        private void nearbyInterval(object sender, EventArgs e)
        {
            if (validConnection())
            {
                if (threadServer != null && server != null)
                {
                    Location loc = new Location();
                    loc.lat = _planeInfoResponse.Latitude * 180 / Math.PI;
                    loc.lon = _planeInfoResponse.Longitude * 180 / Math.PI;
                    loc.speed = _planeInfoResponse.GpsGroundSpeed;
                    loc.alt = _planeInfoResponse.Altitude;
                    loc.time = DateTime.UtcNow;
                    loc.ha = _planeInfoResponse.PlaneHeading * 180 / Math.PI;
                    //loc.va

                    server.GpsUpdate(loc);
                }

                if (towScanMode > TowScanMode.Disabled /*&& _nearbyInfoResponse.Count > 0*/)
                {
                    if (insertTowPlanePressed != 0 && (_planeInfoResponse.AbsoluteTime - insertTowPlanePressed) > AIholdInterval)
                        insertTowPlanePressed = 0;

                    if (insertTowPlanePressed != 0)
                        Console.WriteLine("towingTarget: " + towingTarget + " time: " + (_planeInfoResponse.AbsoluteTime - insertTowPlanePressed) + " towScanMode: " + towScanMode + " insertedTowPlane: " + insertedTowPlane.ToString());

                    bool abort = false;
                    nearbyObjects.Children.Clear();
                    farObjects.Children.Clear();
                    currentTarget.Children.Clear();

                    SortedDictionary<double, Button> nearbyDict = new SortedDictionary<double, Button>();
                    SortedDictionary<double, Button> farDict = new SortedDictionary<double, Button>();
                    KeyValuePair<double, Button> curr = new KeyValuePair<double, Button>();

                    try
                    {
                        KeyValuePair<uint, winchPosition> tempPos = new KeyValuePair<uint, winchPosition>();
                        KeyValuePair<double, KeyValuePair<uint, winchPosition>> closestAI = new KeyValuePair<double, KeyValuePair<uint, winchPosition>>(0, tempPos);

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

                            // AVAILABLE OBJECTS + CURRENT TARGET
                            if ((assistantSettings["realisticRestrictions"] == 0 || _planeInfoResponse.BrakeParkingPosition == 0 && _planeInfoResponse.SimOnGround == 100) &&
                                dir.distance <= 1000 * Math.Min(allowedRadarScale, assistantSettings["RadarScale"]) || obj.Key == towingTarget)
                            {
                                label.Foreground = new SolidColorBrush(obj.Key == towingTarget ? Colors.DarkRed : Colors.DarkGreen);
                                label.BorderBrush = new SolidColorBrush(obj.Key == towingTarget ? Colors.DarkRed : Colors.DarkGreen);
                                label.Margin = new Thickness(5, 5, 5, 5);
                                label.Height = 26;
                                label.Click += attachTowCable;
                                label.Cursor = Cursors.Hand;

                                // IS IT OUR AI TOWPLANE?
                                if ((towScanMode >= TowScanMode.TowSearch || obj.Key == insertedTowPlane.Key) && towingTarget == TARGETMAX && obj.Value.title == "Tow Plane")
                                {
                                    if ((_nearbyInfoResponse.ContainsKey(obj.Key) || _nearbyInfoResponseLast.ContainsKey(obj.Key) || _nearbyInfoResponsePreLast.ContainsKey(obj.Key)) &&
                                        (assistantSettings["realisticRestrictions"] == 0 || _planeInfoResponse.BrakeParkingPosition == 0 && (_planeInfoResponse.SimOnGround == 100 || _planeInfoResponse.AltitudeAboveGround <= _planeInfoResponse.StaticCGtoGround * 1.25)))
                                    {
                                        addLogMessage("Towplane found");
                                        assignTowPlane(obj.Key, obj.Value);
                                    }
                                }

                                if (obj.Key == towingTarget)
                                {
                                    currentTarget.Children.Add(label);
                                    curr = new KeyValuePair<double, Button>(dir.distance, label);
                                }
                                else
                                {
                                    if (!nearbyDict.ContainsKey(dir.distance))
                                    {
                                        nearbyDict.Add(dir.distance, label);
                                    }
                                }
                            }
                            else
                            {
                                label.Margin = new Thickness(5, 1, 5, 1);
                                label.IsEnabled = false;

                                if (!farDict.ContainsKey(dir.distance))
                                {
                                    farDict.Add(dir.distance, label);
                                }
                            }

                            if (_nearbyInfoResponseLast.ContainsKey(obj.Key)) { _nearbyInfoResponsePreLast[obj.Key] = _nearbyInfoResponseLast[obj.Key]; }
                            if (_nearbyInfoResponse.ContainsKey(obj.Key)) { _nearbyInfoResponseLast[obj.Key] = _nearbyInfoResponse[obj.Key]; }

                            // CHECK CURRENT TARGET EXISTANCE
                            if (towingTarget != TARGETMAX &&
                                !_nearbyInfoResponse.ContainsKey(towingTarget) && !_nearbyInfoResponseLast.ContainsKey(towingTarget) && !_nearbyInfoResponsePreLast.ContainsKey(towingTarget))
                                abort = true;

                            if (abort)
                            {
                                addLogMessage("Tow plane lost");
                                toggleTowCable(towingTarget);
                            }
                        }

                        // AI TOW NOT FOUND - INSERT NEW ONE
                        if (_planeInfoResponse.AbsoluteTime - insertTowPlanePressed > 2 && towScanMode == TowScanMode.TowInsert)
                        {
                            if (assistantSettings["realisticTowProcedures"] == 0 || plnPath != "")
                            {
                                //createFlightPlan();
                                insertTowPlane();

                                // CONTINUE SEARCH
                                towScanMode = TowScanMode.TowSearch;
                            }
                            else
                            {
                                showMessage("CUSTOMFLIGHT.PLN not found", _fsConnect);
                                // FINISH SEARCH
                                towScanMode = TowScanMode.Scan;
                                insertedTowPlane = new KeyValuePair<uint, bool>(TARGETMAX, false);
                            }

                        }
                        // AI TOW INSERTED BUT NOT FOUND - TRY TO TELEPORT IT
                        else if (/*assistantSettings["realisticTowProcedures"] == 0 &&*/ towingTarget == TARGETMAX && (_planeInfoResponse.AbsoluteTime - insertTowPlanePressed) > 6 && towScanMode >= TowScanMode.TowSearch && insertedTowPlane.Key != TARGETMAX && !insertedTowPlane.Value)
                        {
                            Console.WriteLine("Teleporting tow plane");
                            teleportTowPlane(insertedTowPlane.Key);
                        }

                        _nearbyInfoResponse = new Dictionary<uint, winchPosition>();

                        foreach (KeyValuePair<Double, Button> btn in nearbyDict)
                            nearbyObjects.Children.Add(btn.Value);

                        foreach (KeyValuePair<Double, Button> btn in farDict)
                            farObjects.Children.Add(btn.Value);

                        if (curr.Key != 0)
                        {
                            nearbyDict.Add(curr.Key, curr.Value);
                        }

                        if (thermalsDebugActive > 0)
                            Application.Current.Dispatcher.Invoke(() => _radarClass.InsertRadarNearby(RadarCanvas, nearbyDict, this));

                    }
                    catch (Exception ex)
                    {
                        addLogMessage(ex.Message);
                    }

                }
            }
        }

        public void attachTowCable(object sender, EventArgs e)
        {
            uint newTarget = TARGETMAX;

            if (sender == null)
            {
                if (towingTarget != TARGETMAX)
                    newTarget = towingTarget;
                else if (nearbyObjects.Children.Count > 0)
                    uint.TryParse(((Button)nearbyObjects.Children[0]).Tag.ToString(), out newTarget);
            }
            else
                uint.TryParse(((Button)sender).Tag.ToString(), out newTarget);

            toggleTowCable(newTarget);
        }

        private void toggleTowCable(uint id, winchPosition position = null, double length = 0)
        {
            if (towingTarget == id)
            {
                towingTarget = TARGETMAX;
                towCableLength = 0;
                insertedTowPlane = new KeyValuePair<uint, bool>(TARGETMAX, false);

                showMessage("Tow rope released", _fsConnect);
                Application.Current.Dispatcher.Invoke(() => playSound("false"));
                Application.Current.Dispatcher.Invoke(() => changeButtonStatus(true, towConnectButton, true, "Attach To Closest"));
            }
            else if (position == null && !_nearbyInfoResponse.ContainsKey(id))
            {
                showMessage("Object " + id + " not in the list (yet?)", _fsConnect);
            }
            else if (assistantSettings["realisticRestrictions"] == 0 || _planeInfoResponse.BrakeParkingPosition == 0 && (_planeInfoResponse.SimOnGround == 100 || _planeInfoResponse.AltitudeAboveGround <= _planeInfoResponse.StaticCGtoGround * 1.25))
            {
                towingTarget = id;
                lightToggled = absoluteTime;

                // FINISH SEARCH
                towScanMode = TowScanMode.Scan;
                //insertedTowPlane = new KeyValuePair<uint, bool>(id, true);

                if (position == null) { position = _nearbyInfoResponse[id]; }

                winchDirection direction = _mathClass.getForceDirection(position, _planeInfoResponse);
                towCableLength = length > 0 ? length + 10 : direction.distance + 10;
                towPrevDist = towCableLength;
                towPrePrevDist = towCableLength;

                showMessage("Tow rope connected to #" + id, _fsConnect);
                Application.Current.Dispatcher.Invoke(() => playSound("false"));
                Application.Current.Dispatcher.Invoke(() => changeButtonStatus(false, towConnectButton, true, "Release Tow Rope"));
            }
            else
            {
                showMessage("Tow rope can be connected only on the ground and with brakes disengaged", _fsConnect);
            }

            Application.Current.Dispatcher.Invoke(() => nearbyInterval(new object(), new EventArgs()));
        }

        private void processTowing()
        {
            Console.WriteLine(Environment.NewLine + "processTowing");

            if (_nearbyInfoResponse.ContainsKey(towingTarget))
            {
                // BLINK LIGHTS
                if (absoluteTime - lightToggled > 1.5)
                {
                    lightToggled = absoluteTime;
                    _planeCommit.LIGHTLANDING = _planeCommit.LIGHTLANDING == 100 ? 0 : 100;
                    _planeCommit.LIGHTTAXI = _planeCommit.LIGHTLANDING == 100 ? 0 : 100;
                }

                double bodyAcceleration = 0;

                // GET ANGLE TO TUG POSITION
                winchPosition winchPosition = _nearbyInfoResponse[towingTarget];
                winchDirection winchDirection = _mathClass.getForceDirection(winchPosition, _planeInfoResponse);

                // SET DESIRED ROPE LENGTH
                towCableDesired = Math.Max(assistantSettings["realisticTowProcedures"] == 0 || _planeInfoResponse.SimOnGround != 100 || _planeInfoResponse.OnAnyRunway == 100 ? 80 : 40, winchPosition.airspeed);

                // LEVEL UP GLIDER BEFORE LAUNCH
                if (_planeInfoResponse.SimOnGround == 100 && _planeInfoResponse.AirspeedIndicated < 5)
                {
                    levelUpGlider();
                }

                // GET FINAL CABLE TENSION
                double accelerationLimit = (assistantSettings["realisticFailures"] == 1 ? 8 : 40) * 9.81;
                if (_planeInfoResponse.SimOnGround == 100)
                {
                    accelerationLimit /= 2;
                }
                double cableTension = _mathClass.getCableTension(towCableLength, Math.Max(1, assistantSettings["towElasticExtension"]), winchDirection);
                double targetVelocity = winchPosition.airspeed > 25 ? winchPosition.airspeed * 2 : 50;

                if (cableTension > 0)
                {
                    bodyAcceleration = _mathClass.getBodyVelocity(winchDirection, _planeCommit, cableTension, accelerationLimit, winchDirection.distance, towPrevDist, towPrePrevDist, lastFrameTiming);
                }

                if (!double.IsNaN(cableTension) && lastFrameTiming != 0 && applyWinchForces(bodyAcceleration, accelerationLimit, winchDirection, targetVelocity, "Tow", assistantSettings["towConnectionPoint"]) &&
                   !double.IsNaN(_planeCommit.VelocityBodyX) && !double.IsNaN(_planeCommit.VelocityBodyY) && !double.IsNaN(_planeCommit.VelocityBodyZ))
                {
                    try
                    {
                        _fsConnect.UpdateData(Definitions.PlaneCommit, _planeCommit);
                    }
                    catch (Exception ex)
                    {
                        addLogMessage(ex.Message);
                    }
                }

                towPrePrevDist = towPrevDist;
                towPrevDist = winchDirection.distance;
            }
        }

        // TOW END


        // LAUNCHPAD START
        private void toggleLaunchpadPrepare(object sender, RoutedEventArgs e)
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
        private void abortLaunchpad()
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
        private void processLaunchpad()
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

                    if (!double.IsNaN(_planeRotate.RotationVelocityBodyX) && !double.IsNaN(_planeRotate.RotationVelocityBodyY) && !double.IsNaN(_planeRotate.RotationVelocityBodyZ))
                    {
                        try
                        {
                            _fsConnect.UpdateData(Definitions.PlaneRotate, _planeRotate);
                        }
                        catch (Exception ex)
                        {
                            addLogMessage(ex.Message);
                        }
                    }
                }
            }
            // STICK TO THE POSITION
            else
            {
                double accel = _planeCommit.VelocityBodyZ - _planeCommitLast.VelocityBodyZ;
                _planeCommit.VelocityBodyZ -= _planeCommit.VelocityBodyZ * lastFrameTiming + accel;
            }

            if (!double.IsNaN(_planeCommit.VelocityBodyX) && !double.IsNaN(_planeCommit.VelocityBodyY) && !double.IsNaN(_planeCommit.VelocityBodyZ))
            {
                try
                {
                    _fsConnect.UpdateData(Definitions.PlaneCommit, _planeCommit);
                }
                catch (Exception ex)
                {
                    addLogMessage(ex.Message);
                }
            }
        }
        // LAUNCHPAD END

        // LANDING START
        private void toggleLandingPrepare(object sender, RoutedEventArgs e)
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
                showMessage("Tailhook deployed", _fsConnect);
                Application.Current.Dispatcher.Invoke(() => playSound("true"));
                Application.Current.Dispatcher.Invoke(() => changeButtonStatus(false, hookPrepareButton, true, "Retract tailhook"));
            }
            else
            {
                arrestorsAbortInitiated = true;
            }
        }
        private void processLanding()
        {
            if (arrestorsAbortInitiated)
            {
                // RESET HOOK
                _planeCommit.TailhookPosition = 0;

                arrestorsAbortInitiated = false;
                _carrierPosition = null;
                arrestorConnectedTime = 0;
                //showMessage("Tailhook retracted", _fsConnect);
                Application.Current.Dispatcher.Invoke(() => changeButtonStatus(_fsConnect != null ? true : false, hookPrepareButton, true, "Deploy tailhook"));
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

                    if (timeLeft <= 0 || assistantSettings["realisticFailures"] == 1 && (cableTension / lastFrameTiming > 100 || _carrierDirection.distance > 105))
                    {
                        showMessage(
                            timeLeft <= 0 ? "Arresting cable released. Distance: " + _carrierDirection.distance.ToString("0.0") + " meters" :
                            "Arresting cable failure. Distance " + _carrierDirection.distance.ToString("0.0") + " meters", _fsConnect);
                        Application.Current.Dispatcher.Invoke(() => playSound("false"));
                        arrestorsAbortInitiated = true;
                        _planeCommit.TailhookPosition = 50;
                    }
                    else if (!double.IsNaN(cableTension) && lastFrameTiming != 0 && cableTension != 0 && _carrierDirection.localForceDirection.Norm > 0)
                    {
                        // LIMIT ROTATION VELOCITY
                        _planeRotate.RotationVelocityBodyX *= lastFrameTiming;
                        _planeRotate.RotationVelocityBodyY *= lastFrameTiming;
                        _planeRotate.RotationVelocityBodyZ *= lastFrameTiming;

                        if (!double.IsNaN(_planeRotate.RotationVelocityBodyX) && !double.IsNaN(_planeRotate.RotationVelocityBodyY) && !double.IsNaN(_planeRotate.RotationVelocityBodyZ))
                        {
                            try
                            {
                                _fsConnect.UpdateData(Definitions.PlaneRotate, _planeRotate);
                            }
                            catch (Exception ex)
                            {
                                addLogMessage(ex.Message);
                            }
                        }

                        _planeCommit.VelocityBodyX += _carrierDirection.localForceDirection.X * cableTension;
                        _planeCommit.VelocityBodyY += _carrierDirection.localForceDirection.Y * cableTension;
                        _planeCommit.VelocityBodyZ += _carrierDirection.localForceDirection.Z * cableTension;

                        // REMOVE VERTICAL VELOCITY
                        if (_planeCommit.VelocityBodyY > 0.1)
                            _planeCommit.VelocityBodyY = -0.01;
                    }
                }
            }


            if (!double.IsNaN(_planeCommit.VelocityBodyX) && !double.IsNaN(_planeCommit.VelocityBodyY) && !double.IsNaN(_planeCommit.VelocityBodyZ))
            {
                try
                {
                    _fsConnect.UpdateData(Definitions.PlaneCommit, _planeCommit);
                }
                catch (Exception ex)
                {
                    addLogMessage(ex.Message);
                }
            }
        }
        // LANDING END

        // THERMALS START
        private void thermalsLoadMap(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Iittle Navmap Userpoints (*.csv)|*.csv";
            openFileDialog.FilterIndex = 2;
            openFileDialog.RestoreDirectory = true;
            if (openFileDialog.ShowDialog() == true)
            {
                string content = File.ReadAllText(openFileDialog.FileName);
                insertThermals(content);

                thermalsMapPath.Text = thermalsList.Count + thermalsListAPI.Count > 0 ? (thermalsList.Count + thermalsListAPI.Count).ToString() + " thermals total" : "No thermals loaded";
            }
        }

        private void thermalsClear(object sender, RoutedEventArgs e)
        {
            thermalsList = new List<winchPosition>();
            Application.Current.Dispatcher.Invoke(() => _radarClass.clearRadarThermals(RadarCanvas, "Thermal"));
            thermalsListAPI = new List<winchPosition>();
            Application.Current.Dispatcher.Invoke(() => _radarClass.clearRadarThermals(RadarCanvas, "ThermalAPI"));
            thermalsMapPath.Text = "No thermals loaded";
        }

        private void enableApiThermals(Button btn, bool enable, bool force = false)
        {

            Application.Current.Dispatcher.Invoke(() => changeButtonStatus(!enable, btn));

            if (!enable)
            {
                if (force || (!assistantSettings.ContainsKey("KK7Autoload") || assistantSettings["KK7Autoload"] == 0) && (!assistantSettings.ContainsKey("OpenAipAutoload") || assistantSettings["OpenAipAutoload"] == 0))
                {
                    Console.WriteLine("dsiableApiThermals");
                    thermalsListAPI = new List<winchPosition>();
                    apiThermalsLoadedPosition = null;
                    apiThermalsLoadedTime = 0;
                    Application.Current.Dispatcher.Invoke(() => thermalsMapPath.Text = thermalsList.Count + thermalsListAPI.Count > 0 ? (thermalsList.Count + thermalsListAPI.Count).ToString() + " thermals total" : "No thermals loaded");

                    // DISABLE THERMALS
                    if (thermalsWorking && thermalsList.Count == 0)
                    {
                        toggleThermals(null, null);
                    }
                }
            }
            else
            {
                Console.WriteLine("enableApiThermals");
            }
        }

        private void loadThermalsApiData()
        {
            if ((assistantSettings["KK7Autoload"] == 1 || assistantSettings["OpenAipAutoload"] == 1) && _planeInfoResponse.Latitude != 0 && _planeInfoResponse.Longitude != 0)
            {
                thermalsListAPI = new List<winchPosition>();
                apiThermalsLoadedPosition = new GeoLocation(_planeInfoResponse.Latitude, _planeInfoResponse.Longitude);
                apiThermalsLoadedTime = _planeInfoResponse.AbsoluteTime;

                // S W N E
                double[] bounds = getThermalsBounds(new GeoLocation(_planeInfoResponse.Latitude, _planeInfoResponse.Longitude), true);

                if (bounds[0] != 0 && bounds[1] != 0 && bounds[2] != 0 && bounds[3] != 0 && bounds[0] != bounds[2] && bounds[1] != bounds[3])
                {
                    if (assistantSettings.ContainsKey("KK7Autoload") && assistantSettings["KK7Autoload"] == 1)
                    {
                        string url = "https://thermal.kk7.ch/api/hotspots/all/" + bounds[0].ToString().Replace(',', '.') + "," + bounds[1].ToString().Replace(',', '.') + "," + bounds[2].ToString().Replace(',', '.') + "," + bounds[3].ToString().Replace(',', '.') + "?csv&limit=5000";
                        Console.WriteLine(url);

                        try
                        {
                            var lWebClient = new WebClient();
                            string respond = lWebClient.DownloadString(url);

                            if (respond.Length > 150)
                            {
                                insertApiThermals(respond, bounds);
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Unable to load thermals from URL" + Environment.NewLine + url + Environment.NewLine + "error: " + ex.Message);
                        }
                    }

                    if (assistantSettings.ContainsKey("OpenAipAutoload") && assistantSettings["OpenAipAutoload"] == 1 && Directory.Exists("openaip"))
                    {
                        try
                        {

                            var files = Directory.EnumerateFiles("openaip", "*_hot.aip", SearchOption.TopDirectoryOnly);
                            foreach (var file in files)
                            {
                                string xml = File.ReadAllText(file);

                                if (!string.IsNullOrEmpty(xml))
                                {
                                    //Console.WriteLine("Parsing " + file);

                                    string csv = "";
                                    foreach (XElement hotspot in XElement.Parse(xml).Element("HOTSPOTS").Elements("HOTSPOT"))
                                    {
                                        XElement geolocation = hotspot.Element("GEOLOCATION");
                                        string lat = geolocation.Element("LAT").Value;
                                        string lng = geolocation.Element("LON").Value;
                                        string reliability = hotspot.Element("RELIABILITY").Value;

                                        if (!string.IsNullOrEmpty(lat) && !string.IsNullOrEmpty(lng) && !string.IsNullOrEmpty(reliability))
                                        {
                                            csv += lat + "," + lng + ",1," + reliability.Replace("0.", "") + Environment.NewLine;
                                        }
                                    }

                                    insertApiThermals(csv, bounds);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Unable to load thermals from local files. error: " + ex.Message);
                        }
                    }
                }
                else
                {
                    MessageBox.Show("Failed to request hotspots data");
                }

                if (thermalsListAPI.Count > 0)
                {
                    showMessage(thermalsListAPI.Count + " hotspots loaded from API sources", _fsConnect);
                }
                else
                {
                    showMessage("No hotspots for this area from API sources", _fsConnect);
                }
            }


            Application.Current.Dispatcher.Invoke(() => thermalsMapPath.Text = thermalsList.Count + thermalsListAPI.Count > 0 ? (thermalsList.Count + thermalsListAPI.Count).ToString() + " thermals total" : "No thermals loaded");
        }

        private double[] getThermalsBounds(GeoLocation coords, bool returnDegree)
        {
            double lat = coords.Latitude * 180 / Math.PI;
            double lon = coords.Longitude * 180 / Math.PI;
            int lonCount = (int)Math.Round(1 / Math.Cos(coords.Latitude)) - 1;

            // S W N E
            double s = Math.Floor(lat - 0.0000001);
            double w = Math.Floor(lon - lonCount - 0.0000001);
            double n = Math.Ceiling(lat + 0.0000001);
            double e = Math.Ceiling(lon + lonCount + 0.0000001);
            return new double[] {
                s * (!returnDegree ? Math.PI / 180 : 1),
                w * (!returnDegree ? Math.PI / 180 : 1),
                n * (!returnDegree ? Math.PI / 180 : 1),
                e * (!returnDegree ? Math.PI / 180 : 1)
            };
        }

        private bool coordinateInsideBounds(GeoLocation apiThermalsLoadedPosition, double[] bounds)
        {
            if (apiThermalsLoadedPosition.Latitude < bounds[0] || apiThermalsLoadedPosition.Latitude > bounds[2] || apiThermalsLoadedPosition.Longitude < bounds[1] || apiThermalsLoadedPosition.Longitude > bounds[3])
            {
                return false;
            }

            return true;
        }

        private void insertThermals(string content)
        {
            //List<winchPosition> radarThermals = new List<winchPosition>();
            Application.Current.Dispatcher.Invoke(() => _radarClass.clearRadarThermals(RadarCanvas, "Thermal"));

            foreach (string line in content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
            { // Location,,thermal,40.17048,-111.96616,5000,11.95587,10,,,250,2021-01-04T23:32:47.770,
                if (!String.IsNullOrWhiteSpace(line))
                {
                    string[] data = line.Split(',');
                    if (data.Length > 7 && data[2].ToLower().Trim() == "thermal")
                    {
                        double radius = 0.5;
                        double strength = 10;

                        if (double.TryParse(data[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double lat) &&
                            double.TryParse(data[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double lng) &&
                            double.TryParse(data[5].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double alt) &&
                            (data[7].Contains(" ") && double.TryParse(data[7].Split(' ')[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out radius) && double.TryParse(data[7].Split(' ')[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out strength) ||
                            double.TryParse(data[7].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out radius)))
                        {
                            winchPosition _thermalPosition = new winchPosition(new GeoLocation(lat / 180 * Math.PI, lng / 180 * Math.PI), 0.305 * alt, 1852 * radius, strength / 1.9 * 4);
                            thermalsList.Add(_thermalPosition);
                            //radarThermals.Add(_thermalPosition);
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

            if (thermalsList.Count > 0)
                Application.Current.Dispatcher.Invoke(() => _radarClass.insertRadarThermals(RadarCanvas, thermalsList, "Thermal"));
        }

        private void insertApiThermals(string content, double[] bounds)
        {
            Application.Current.Dispatcher.Invoke(() => _radarClass.clearRadarThermals(RadarCanvas, "ThermalAPI"));

            foreach (string line in content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
            { // Location,,thermal,40.17048,-111.96616,5000,11.95587,10,,,250,2021-01-04T23:32:47.770,
                if (!String.IsNullOrWhiteSpace(line) && !line.Contains("latitude"))
                {
                    string[] data = line.Split(',');
                    if (data.Length >= 4)
                    {
                        double radius = 0.9;
                        double strength = 15; // KNOTS

                        if (double.TryParse(data[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double lat) &&
                            double.TryParse(data[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double lng) &&
                            double.TryParse(data[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double alt) &&
                            double.TryParse(data[3], NumberStyles.Any, CultureInfo.InvariantCulture, out double probability)
                        )
                        {
                            if (!coordinateInsideBounds(new GeoLocation(lat, lng), bounds))
                            {
                                continue;
                            }

                            double modifier = probability / 100;
                            lat = lat / 180 * Math.PI;
                            lng = lng / 180 * Math.PI;
                            radius *= 1852 * modifier;

                            // CHECK SIBLINGS
                            if (thermalsListAPI.Count > 0)
                            {
                                bool cnt = false;

                                foreach (winchPosition therm in thermalsListAPI)
                                {
                                    double distance = Math.Abs(_mathClass.findDistanceBetweenPoints(lat, lng, therm.location.Latitude, therm.location.Longitude));

                                    if ((distance - radius - therm.radius) < 10000 * (modifier * therm.airspeed / strength))
                                    {
                                        cnt = true;
                                        break;
                                    }
                                }

                                if (cnt)
                                {
                                    continue;
                                }
                            }


                            winchPosition _thermalPosition = new winchPosition(new GeoLocation(lat, lng), 0, radius, strength / 1.9 * modifier);
                            thermalsListAPI.Add(_thermalPosition);
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

            if (thermalsListAPI.Count > 0)
                Application.Current.Dispatcher.Invoke(() => _radarClass.insertRadarThermals(RadarCanvas, thermalsListAPI, "ThermalAPI"));
        }
        private void kk7CsvExport(object sender, RoutedEventArgs e)
        {
            if (thermalsListAPI.Count > 0)
            {
                string csv = "";

                foreach (winchPosition thermal in thermalsListAPI)
                {
                    csv += "Location,,thermal," + (thermal.location.Latitude * 180 / Math.PI).ToString(CultureInfo.InvariantCulture) + "," + (thermal.location.Longitude * 180 / Math.PI).ToString(CultureInfo.InvariantCulture) + ",0,1," + (thermal.radius / 1852).ToString("0.0", CultureInfo.InvariantCulture) + " " + (thermal.airspeed * 1.9 / 4).ToString("0.0", CultureInfo.InvariantCulture) + ",,,250,2020-01-01T00:00:00.000," + Environment.NewLine;
                }

                SaveFileDialog sfd = new SaveFileDialog();

                sfd.Filter = "LittleNavMap userpoints (*.csv)|*.csv";
                sfd.FilterIndex = 1;

                if (sfd.ShowDialog() == true)
                {
                    try
                    {
                        File.WriteAllText(sfd.FileName, csv);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Failed to save API data. Error: " + ex.Message);
                    }
                }
            }
        }
        private void toggleThermals(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("toggleThermals");

            if (thermalsWorking)
            {
                thermalsWorking = false;
                //showMessage("Thermals disabled", _fsConnect);
                Application.Current.Dispatcher.Invoke(() => playSound("false"));
                Application.Current.Dispatcher.Invoke(() => changeButtonStatus(true, thermalsToggleButton, true, "Enable thermals"));
            }
            else if (!validConnection())
            {
                Console.WriteLine("connection lost");
            }
            else
            {
                if (thermalsList.Count == 0 && thermalsListAPI.Count == 0)
                {
                    Application.Current.Dispatcher.Invoke(() => thermalsMapPath.Text = "No thermal maps loaded");
                }

                windDirection = 0;
                windVelocity = 0;
                dayTimeModifier = 1;
                overcastModifier = 1;

                _fsConnect.RequestData(Requests.WeatherData, Definitions.WeatherData);

                thermalsWorking = true;
                //showMessage("Thermals enabled", _fsConnect);
                Application.Current.Dispatcher.Invoke(() => playSound("true"));
                Application.Current.Dispatcher.Invoke(() => changeButtonStatus(false, thermalsToggleButton, true, "Disable thermals"));
            }
        }
        private void processThermals(List<winchPosition> list, bool API = false)
        {
            // WIND MODIFIERS
            double windModifier = 0;
            double thermalLeaning = 0;
            if (assistantSettings.ContainsKey("thermalsType") && assistantSettings["thermalsType"] >= 1)
            {
                windModifier = Math.Pow(Math.Min(Math.Max(0, windVelocity / 50), 1), 0.5);
                thermalLeaning = _planeInfoResponse.AltitudeAboveGround * windModifier;
            }

            int id = 0;
            foreach (winchPosition thermal in list)
            {
                double inversionLayer = _planeInfoResponse.AltitudeAboveGround / 3 * 0.305;
                double height = 0;
                double acAltitude = 0;
                double thermalRadius = thermal.radius;

                // DRIFT
                if (assistantSettings.ContainsKey("thermalsType") && assistantSettings["thermalsType"] >= 2)
                {
                    thermal.location = _mathClass.FindPointAtDistanceFrom(thermal.location, windDirection - Math.PI, windVelocity / 1000 / 4 * lastFrameTiming);
                }

                GeoLocation thermalCenter = new GeoLocation(thermal.location.Latitude, thermal.location.Longitude);

                // AGL
                if (thermal.alt >= 1000)
                {
                    height = thermal.alt;
                    acAltitude = _planeInfoResponse.AltitudeAboveGround;
                }
                // MSL
                else
                {
                    height = Math.Max(0, assistantSettings["thermalsHeight"] * 0.305) + thermal.alt;
                    acAltitude = _planeInfoResponse.Altitude;
                }

                thermalRadius *= 1 + windModifier;
                double topEdge = height - inversionLayer;

                if (thermalRadius > 0)
                {
                    double finalModifier = 0.1;

                    // LEANING
                    if (thermalLeaning > 10)
                    {
                        thermalCenter = _mathClass.FindPointAtDistanceFrom(thermalCenter, windDirection - Math.PI, thermalLeaning / 1000);
                    }

                    winchPosition thermalTemp = new winchPosition(thermalCenter, _planeInfoResponse.Altitude - 10 * thermalRadius, thermalRadius);
                    winchDirection _thermalDirection = _mathClass.getForceDirection(thermalTemp, _planeInfoResponse);

                    if (acAltitude < topEdge + 2 * inversionLayer && _thermalDirection.groundDistance < thermalTemp.radius)
                    {
                        //Console.WriteLine("Thermal leaning: " + thermalLeaning.ToString("0.0") + " width scale: " + (1 + windModifier));

                        double horizontalModifier = Math.Pow(Math.Abs(1 - _thermalDirection.groundDistance / thermalTemp.radius), 0.5); // DISTANCE TO THE CENTER
                        double verticalModifier = acAltitude < topEdge ?
                            Math.Pow(Math.Abs(acAltitude / (topEdge)), 0.25) : // UNDER INVERSION
                            (inversionLayer - (acAltitude - topEdge)) / (inversionLayer); // ABOVE INVERSION
                        double pitchBankModifier = Math.Abs(Math.Cos(_planeInfoResponse.PlaneBank)) * Math.Abs(Math.Cos(_planeInfoResponse.PlanePitch)); // ROTATION
                        double airspeedModifier = Math.Pow(Math.Abs(1 - _planeCommit.VelocityBodyZ / 100), 0.25); // ATTEMPT TO PREVENT OVERSPEED
                        double ambientModifier = (1 - windModifier) * overcastModifier * dayTimeModifier;

                        finalModifier = horizontalModifier * verticalModifier * pitchBankModifier * airspeedModifier * ambientModifier;
                        double liftAmount = thermal.airspeed * finalModifier;

                        // COMPARE VERTICAL VELOCITY AND UPLIFT
                        Console.WriteLine("LiftY: " + liftAmount + " VerticalSpeed: " + _planeInfoResponse.VerticalSpeed);
                        if (acAltitude < topEdge && _planeInfoResponse.VerticalSpeed < liftAmount)
                        {
                            liftAmount = Math.Min(10, liftAmount - _planeInfoResponse.VerticalSpeed);
                        }
                        else
                        {
                            liftAmount = 0;
                        }

                        //Console.WriteLine("VelocityBodyY: " + _planeCommit.VelocityBodyY + " / " + (_planeCommit.VelocityBodyY + ((double)_thermalDirection.localForceDirection.Y) * liftAmount) + " (" + liftAmount + ")");
                        Console.WriteLine("Thermal direction: x" + (-(double)_thermalDirection.localForceDirection.X) * liftAmount + " y" + ((double)_thermalDirection.localForceDirection.Y) * liftAmount + " z" + (-(double)_thermalDirection.localForceDirection.Z) * liftAmount + " (" + liftAmount + ")");
                        _planeCommit.VelocityBodyX -= ((double)_thermalDirection.localForceDirection.X) * liftAmount * lastFrameTiming;
                        _planeCommit.VelocityBodyY -= ((double)_thermalDirection.localForceDirection.Y) * liftAmount * lastFrameTiming / 2;
                        _planeCommit.VelocityBodyZ += ((double)_thermalDirection.localForceDirection.Z) * liftAmount * lastFrameTiming;

                        // FORWARD SPEED COMPENSATION
                        _planeCommit.VelocityBodyZ -= ((double)_thermalDirection.localForceDirection.Y) * liftAmount * lastFrameTiming / 2;
                        //_planeCommit.VelocityBodyZ += _planeCommit.VelocityBodyZ * Math.Pow(Math.Abs(lastFrameTiming), 2.5) * horizontalModifier * verticalModifier * airspeedModifier;
                        if (!double.IsNaN(_planeCommit.VelocityBodyX) && !double.IsNaN(_planeCommit.VelocityBodyY) && !double.IsNaN(_planeCommit.VelocityBodyZ))
                        {
                            try
                            {
                                if (liftAmount != 0.0)
                                {
                                    _fsConnect.UpdateData(Definitions.PlaneCommit, _planeCommit);
                                }
                            }
                            catch (Exception ex)
                            {
                                addLogMessage(ex.Message);
                            }
                        }
                    }

                    if (thermalsDebugActive > 0 && (!assistantSettings.ContainsKey("RadarScale") || scaleRefresh > 0 || _thermalDirection.groundDistance / 1.25 < Math.Min(allowedRadarScale, assistantSettings["RadarScale"]) * 1000 + thermalTemp.radius))
                    {
                        Application.Current.Dispatcher.Invoke(() => _radarClass.updateRadarThermals(RadarCanvas, (!API ? "Thermal_" : "ThermalAPI_") + id, _thermalDirection, thermalTemp, finalModifier, Math.Min(maxRadarScale, assistantSettings["RadarScale"])));
                    }
                }

                id++;
            }

            if (scaleRefresh > 0)
                scaleRefresh--;
        }
        // THERMALS END

        // DATA EXCHANGE
        private void HandleReceivedFsData(object sender, FsDataReceivedEventArgs e)
        {
            if (validConnection())
            {
                try
                {
                    // COMMON DATA
                    if (e.RequestId == (uint)Requests.PlaneInfo)
                    {
                        //Console.WriteLine(JsonConvert.SerializeObject(e.Data, Formatting.Indented));
                        _planeInfoResponseLast = _planeInfoResponse;
                        _planeInfoResponse = (PlaneInfoResponse)(e.Data);

                        absoluteTime = _planeInfoResponse.AbsoluteTime;

                        trackControlChanges();

                        Application.Current.Dispatcher.Invoke(() => VerticalWindPos.Height = Math.Max(0, _planeInfoResponse.AmbientWindY * 1.94) * 6.25);
                        Application.Current.Dispatcher.Invoke(() => VerticalWindNeg.Height = Math.Abs(Math.Min(0, _planeInfoResponse.AmbientWindY * 1.94) * 6.25));

                        // UPDATE KK7 DATA
                        if ((assistantSettings.ContainsKey("KK7Autoload") && assistantSettings["KK7Autoload"] == 1 || assistantSettings.ContainsKey("OpenAipAutoload") && assistantSettings["OpenAipAutoload"] == 1) &&
                            thermalsWorking && _planeInfoResponse.Latitude != 0 && _planeInfoResponse.Longitude != 0 && _planeInfoResponse.AbsoluteTime > 20 + apiThermalsLoadedTime)
                        {
                            if (apiThermalsLoadedPosition == null)
                            {
                                loadThermalsApiData();
                            }
                            else
                            {
                                double[] bounds = getThermalsBounds(apiThermalsLoadedPosition, false);
                                // S W N E
                                if (!coordinateInsideBounds(new GeoLocation(_planeInfoResponse.Latitude, _planeInfoResponse.Longitude), bounds))
                                {
                                    addLogMessage($"Plane outside of loaded thermals area: {_planeInfoResponse.Latitude:F5} {_planeInfoResponse.Longitude:F5} - {bounds[0]:F5} {bounds[1]:F5} {bounds[2]:F5} {bounds[3]:F5} ");
                                    loadThermalsApiData();
                                }
                            }
                        }

                        if (thermalsDebugActive > 0)
                            Application.Current.Dispatcher.Invoke(() => _radarClass.updateCompassWind(RadarCanvas, _planeInfoResponse.PlaneHeading, _weatherReport.AmbientWindDirection, _weatherReport.AmbientWindVelocity * 1.94384));
                    }
                    // LAUNCH DATA
                    else if (e.RequestId == (uint)Requests.PlaneCommit)
                    {
                        //Console.WriteLine(JsonConvert.SerializeObject(e.Data, Formatting.Indented));
                        _planeCommitLast = _planeCommit;
                        _planeCommit = (PlaneInfoCommit)e.Data;

                        absoluteTime = _planeCommit.AbsoluteTime;

                        // PAUSE?
                        if (_planeInfoResponse.SimOnGround != 100 &&
                            (_planeInfoResponse.AirspeedIndicated == _planeInfoResponseLast.AirspeedIndicated &&
                            _planeInfoResponse.GpsGroundSpeed == _planeInfoResponseLast.GpsGroundSpeed &&
                            _planeInfoResponse.Altitude == _planeInfoResponseLast.Altitude &&
                            _planeInfoResponse.PlaneBank == _planeInfoResponseLast.PlaneBank &&
                            _planeInfoResponse.PlanePitch == _planeInfoResponseLast.PlanePitch))
                        {
                            lastFrameTiming = 0;
                        }
                        else
                        {
                            lastFrameTiming = absoluteTime - swLast;
                        }

                        if (lastFrameTiming > 0.001 && lastFrameTiming <= 1.0 && _planeInfoResponse.IsSlewActive != 100)
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
                                //Application.Current.Dispatcher.Invoke(() => thermalsDebugData.Children.Clear());

                                if (_planeInfoResponse.AirspeedIndicated < 100)
                                {
                                    processThermals(thermalsList);
                                    processThermals(thermalsListAPI, true);
                                }
                            }
                        }

                        swLast = absoluteTime;
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
                        winchPosition nearbyPosition = new winchPosition(new GeoLocation(response.Latitude, response.Longitude), response.Altitude, 0, response.Airspeed, response.FlightNumber == "9999" || insertedTowPlane.Key == e.ObjectID ? "Tow Plane" : response.Title, response.Category);
                        _nearbyInfoResponse[e.ObjectID] = nearbyPosition;

                        // TOWING IN PROCESS
                        if (towingTarget == e.ObjectID)
                        {
                            processTowing();
                        }

                        // RADAR
                        if (thermalsDebugActive > 0)
                            Application.Current.Dispatcher.Invoke(() => _radarClass.updateRadarNearby(RadarCanvas, e.ObjectID, _mathClass.getForceDirection(nearbyPosition, _planeInfoResponse), nearbyPosition, towingTarget == e.ObjectID, Math.Min(maxRadarScale, assistantSettings["RadarScale"])));

                        if (response.FlightNumber == "9999" || insertedTowPlane.Key == e.ObjectID)
                        {
                            bool AIhold = assistantSettings["realisticTowProcedures"] == 0 && insertTowPlanePressed != 0 && _planeInfoResponse.AbsoluteTime - insertTowPlanePressed < AIholdInterval;
                            // HOLD INSERTED PLANE FOR A WHILE
                            if (AIhold)
                            {
                                Console.WriteLine("Holding AI tow plane #" + e.ObjectID);
                                TowInfoResponse towInfo = new TowInfoResponse();
                                towInfo.Altitude = response.Altitude;
                                towInfo.Latitude = response.Latitude;
                                towInfo.Longitude = response.Longitude;
                                towInfo.Heading = response.Heading;
                                towInfo.Bank = 0;
                                towInfo.VelocityBodyX = 0;
                                towInfo.VelocityBodyY = 0;
                                towInfo.VelocityBodyZ = 0;

                                if (!double.IsNaN(towInfo.Altitude) && !double.IsNaN(towInfo.Latitude) && !double.IsNaN(towInfo.Longitude) && !double.IsNaN(towInfo.Heading) && !double.IsNaN(towInfo.Bank) && !double.IsNaN(towInfo.VelocityBodyZ))
                                {
                                    Console.WriteLine("Leveling AI tow plane #" + e.ObjectID);
                                    try
                                    {
                                        _fsConnect.UpdateData(Definitions.TowPlane, towInfo, e.ObjectID);
                                    }
                                    catch (Exception ex)
                                    {
                                        addLogMessage(ex.Message);
                                    }
                                }
                            }
                            // LEVEL UP TAXIING AI TOW PLANE
                            else if (Math.Abs(response.Bank) > 1 * Math.PI / 180 && response.SimOnGround == 100)
                            {
                                TowInfoPitch towCommit = new TowInfoPitch();
                                towCommit.Bank = response.Bank - Math.Sign(response.Bank) * lastFrameTiming / 50;
                                towCommit.Heading = response.Heading;
                                towCommit.VelocityBodyY = response.Verticalspeed * Math.Cos(response.Bank);

                                if (!double.IsNaN(towCommit.Bank))
                                {
                                    try
                                    {
                                        _fsConnect.UpdateData(Definitions.TowPlaneCommit, towCommit, e.ObjectID);
                                    }
                                    catch (Exception ex)
                                    {
                                        addLogMessage(ex.Message);
                                    }
                                }
                            }
                            // CONTROL ALTITUDE of AI TOW PLANE
                            else if (insertedTowPlane.Key == e.ObjectID && towingTarget == e.ObjectID &&
                                (assistantSettings["realisticTowProcedures"] == 0 || response.Airspeed > assistantSettings["aiSpeed"] / 1.96))
                            {
                                TowInfoPitch towCommit = new TowInfoPitch();
                                towCommit.Bank = response.Bank;
                                towCommit.Pitch = 0;
                                towCommit.Heading = response.Heading;
                                towCommit.VelocityBodyZ = response.Airspeed;
                                towCommit.VelocityBodyY = response.Verticalspeed;

                                if (assistantSettings["realisticTowProcedures"] == 0)
                                {
                                    double liftPower = towCommit.VelocityBodyZ > 15 ? (towCommit.VelocityBodyZ - 15) * 0.1 : 0;

                                    // CONTROL DUMB AIRSPEED
                                    towCommit.VelocityBodyZ += 5 * lastFrameTiming;

                                    // LIMIT ALTITUDE
                                    if (response.Altitude > towToggledAltitude + 2500 && _planeInfoResponse.AltitudeAboveGround > 100)
                                    {
                                        Console.WriteLine("AI Alt limit");
                                        towCommit.VelocityBodyY = -0.1;
                                    }
                                    // LIMIT SINK RATE
                                    else if (response.Verticalspeed < -1)
                                    {
                                        Console.WriteLine("AI Sink limit");
                                        towCommit.VelocityBodyY = liftPower;
                                    }
                                    // ADD VERICAL SPEED
                                    else if (response.Altitude < towToggledAltitude + 2000 && towCommit.VelocityBodyZ > 5)
                                    {
                                        Console.WriteLine("AI Lift up");
                                        towCommit.VelocityBodyY = Math.Min(10, liftPower);
                                    }
                                }

                                // LIMIT AIRSPEED
                                if (towCommit.VelocityBodyZ > assistantSettings["aiSpeed"] / 1.96)
                                {
                                    towCommit.VelocityBodyZ = 0.9 * assistantSettings["aiSpeed"] / 1.96 + 0.1 * towCommit.VelocityBodyZ;
                                }

                                if (!double.IsNaN(towCommit.Heading) && !double.IsNaN(towCommit.Pitch) && !double.IsNaN(towCommit.Bank) && !double.IsNaN(towCommit.VelocityBodyY) &&
                                    !double.IsNaN(towCommit.VelocityBodyZ))
                                {
                                    try
                                    {
                                        _fsConnect.UpdateData(Definitions.TowPlaneCommit, towCommit, e.ObjectID);
                                    }
                                    catch (Exception ex)
                                    {
                                        addLogMessage(ex.Message);
                                    }
                                }
                            }
                        }
                    }
                    // ENGINE DATA
                    else if (e.RequestId == (uint)Requests.PlaneEngineData)
                    {
                        //Console.WriteLine(e.ObjectID);
                        //Console.WriteLine(JsonConvert.SerializeObject(e.Data, Formatting.Indented));
                        _planeEngineData = (PlaneEngineData)e.Data;
                    }

                    // WEATHER DATA
                    else if (e.RequestId == (uint)Requests.WeatherData)
                    {
                        //Console.WriteLine(e.ObjectID);
                        //Console.WriteLine(JsonConvert.SerializeObject(e.Data, Formatting.Indented));
                        _weatherReport = (WeatherReport)e.Data;

                        // WIND
                        if (_weatherReport.AmbientWindDirection - windDirection > Math.PI)
                        {
                            _weatherReport.AmbientWindDirection -= 2 * Math.PI;
                        }
                        else if (_weatherReport.AmbientWindDirection - windDirection < -Math.PI)
                        {
                            _weatherReport.AmbientWindDirection += 2 * Math.PI;
                        }

                        windDirection = 0.9 * (windDirection + 2 * Math.PI) + 0.1 * (_weatherReport.AmbientWindDirection + 2 * Math.PI);
                        windVelocity = 0.9 * windVelocity + 0.1 * _weatherReport.AmbientWindVelocity;

                        while (windDirection > Math.PI) { windDirection -= 2 * Math.PI; }
                        while (windDirection < -Math.PI) { windDirection += 2 * Math.PI; }

                        // WEATHER
                        switch (_weatherReport.AmbientPrecipState)
                        {
                            case 4: // RAIN
                                overcastModifier = 0.9 * overcastModifier;
                                break;
                            case 8: // SNOW
                                overcastModifier = 0.9 * overcastModifier + 0.02;
                                break;
                            case 2: // DRY
                            default:
                                overcastModifier = 0.9 * overcastModifier + 0.1;
                                break;
                        }

                        // DAYTIME
                        switch (_weatherReport.TimeOfDay)
                        {
                            case 0: // DAWN
                                dayTimeModifier = 0.9 * dayTimeModifier + 0.01;
                                break;
                            case 1: // DAY
                                dayTimeModifier = 0.9 * dayTimeModifier + 0.1;
                                break;
                            case 2: // DUSK
                            case 3: // NIGHT
                                dayTimeModifier = 0.9 * dayTimeModifier;
                                break;
                        }
                    }


                    else
                    {
                        Console.WriteLine("Unknown request ID " + (uint)Requests.PlaneInfo + " received (type " + e.Data.GetType() + ")");
                    }
                }
                catch (Exception ex)
                {
                    addLogMessage("Could not handle received FS data: " + ex.Message);
                }
            }
        }

        // 23-01-2021 THIS REQUEST IS BROKEN
        /*private void HandleReceivedAirports(object sender, AirportDataReceivedEventArgs e)
        {
            try
            {
                //Console.WriteLine(JsonConvert.SerializeObject(e.Data, Formatting.Indented));
                if (airportsArray == null)
                {
                    airportsArray = (Object[])e.Data;
                }
                else
                {
                    airportsArray = airportsArray.Concat((Object[])e.Data).ToArray();
                }

                Console.WriteLine("Airports data received: " + airportsArray.Count());
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not handle received AP data: " + ex.Message);
            }
        }*/

        private void HandleReceivedSystemEvent(object sender, ObjectAddremoveEventReceivedEventArgs e)
        {
            try
            {
                Console.WriteLine("System EVENT data received ObjectID: " + e.ObjectID + " RequestId: " + e.RequestId + " respondID: " + e.Data);

                switch (e.RequestId)
                {
                    case (uint)Definitions.SystemEvents:
                        if (towScanMode == TowScanMode.TowSearch && insertedTowPlane.Key == TARGETMAX)
                        {
                            insertedTowPlane = new KeyValuePair<uint, bool>((uint)e.Data, false);
                            //towScanMode = TowScanMode.Scan;
                            Console.WriteLine("Tow plane number " + insertedTowPlane);

                            if (assistantSettings["realisticTowProcedures"] == 0 && towingTarget == TARGETMAX && towScanMode >= TowScanMode.TowSearch)
                            {
                                Console.WriteLine("Teleporting tow plane");
                                _fsConnect.AIReleaseControl(insertedTowPlane.Key, Requests.TowPlane);
                                teleportTowPlane(insertedTowPlane.Key);
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                addLogMessage("Could not handle received EVENT data: " + ex.Message);
            }
        }


        private void trackControlChanges()
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

        private void runControlFunction(string fieldName)
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
                        method.Invoke(Application.Current.MainWindow, new object[] { null, null });
                    }
                }
            }
        }

        private void InitializeDataDefinitions(FsConnect fsConnect)
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
            definition.Add(new SimProperty(FsSimVar.IsSlewActive, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.GpsGroundSpeed, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.AirspeedIndicated, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.Verticalspeed, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.AmbientWindY, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));

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
            //cDefinition.Add(new SimProperty(FsSimVar.WaterRudderHandlePosition, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.LIGHTLANDING, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.LIGHTTAXI, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));

            fsConnect.RegisterDataDefinition<PlaneInfoCommit>(Definitions.PlaneCommit, cDefinition);

            List<SimProperty> rDefinition = new List<SimProperty>();
            rDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyX, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            rDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyY, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            rDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyZ, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));

            fsConnect.RegisterDataDefinition<PlaneInfoRotate>(Definitions.PlaneRotate, rDefinition);

            List<SimProperty> nDefinition = new List<SimProperty>();
            nDefinition.Add(new SimProperty(FsSimVar.Title, FsUnit.None, SIMCONNECT_DATATYPE.STRING256));
            nDefinition.Add(new SimProperty(FsSimVar.Category, FsUnit.None, SIMCONNECT_DATATYPE.STRING256));
            nDefinition.Add(new SimProperty(FsSimVar.ATCFLIGHTNUMBER, FsUnit.None, SIMCONNECT_DATATYPE.STRING8));
            nDefinition.Add(new SimProperty(FsSimVar.PlaneLatitude, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            nDefinition.Add(new SimProperty(FsSimVar.PlaneLongitude, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            nDefinition.Add(new SimProperty(FsSimVar.PlaneAltitude, FsUnit.Meter, SIMCONNECT_DATATYPE.FLOAT64));
            nDefinition.Add(new SimProperty(FsSimVar.AirspeedTrue, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            nDefinition.Add(new SimProperty(FsSimVar.Verticalspeed, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            nDefinition.Add(new SimProperty(FsSimVar.PlaneHeading, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            nDefinition.Add(new SimProperty(FsSimVar.PlaneBank, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            nDefinition.Add(new SimProperty(FsSimVar.SimOnGround, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            nDefinition.Add(new SimProperty(FsSimVar.OnAnyRunway, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            //nDefinition.Add(new SimProperty(FsSimVar.AmbientInCloud, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));

            fsConnect.RegisterDataDefinition<NearbyInfoResponse>(Definitions.NearbyObjects, nDefinition);

            List<SimProperty> tDefinition = new List<SimProperty>();
            tDefinition.Add(new SimProperty(FsSimVar.PlaneLatitude, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            tDefinition.Add(new SimProperty(FsSimVar.PlaneLongitude, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            tDefinition.Add(new SimProperty(FsSimVar.PlaneAltitude, FsUnit.Meter, SIMCONNECT_DATATYPE.FLOAT64));
            tDefinition.Add(new SimProperty(FsSimVar.PlaneHeading, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            tDefinition.Add(new SimProperty(FsSimVar.PlaneBank, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            tDefinition.Add(new SimProperty(FsSimVar.VelocityBodyX, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            tDefinition.Add(new SimProperty(FsSimVar.VelocityBodyY, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            tDefinition.Add(new SimProperty(FsSimVar.VelocityBodyZ, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));

            fsConnect.RegisterDataDefinition<TowInfoResponse>(Definitions.TowPlane, tDefinition);

            List<SimProperty> pDefinition = new List<SimProperty>();
            pDefinition.Add(new SimProperty(FsSimVar.PlaneHeading, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            pDefinition.Add(new SimProperty(FsSimVar.PlanePitch, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            pDefinition.Add(new SimProperty(FsSimVar.PlaneBank, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            pDefinition.Add(new SimProperty(FsSimVar.VelocityBodyY, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            pDefinition.Add(new SimProperty(FsSimVar.VelocityBodyZ, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));

            fsConnect.RegisterDataDefinition<TowInfoPitch>(Definitions.TowPlaneCommit, pDefinition);

            List<SimProperty> eDefinition = new List<SimProperty>();
            eDefinition.Add(new SimProperty(FsSimVar.ENGTORQUE1, FsUnit.FootPounds, SIMCONNECT_DATATYPE.FLOAT64));
            eDefinition.Add(new SimProperty(FsSimVar.ENGTORQUE2, FsUnit.FootPounds, SIMCONNECT_DATATYPE.FLOAT64));
            eDefinition.Add(new SimProperty(FsSimVar.ENGTORQUE3, FsUnit.FootPounds, SIMCONNECT_DATATYPE.FLOAT64));
            eDefinition.Add(new SimProperty(FsSimVar.ENGTORQUE4, FsUnit.FootPounds, SIMCONNECT_DATATYPE.FLOAT64));
            eDefinition.Add(new SimProperty(FsSimVar.TURBTHRUST1, FsUnit.FootPounds, SIMCONNECT_DATATYPE.FLOAT64));
            eDefinition.Add(new SimProperty(FsSimVar.TURBTHRUST2, FsUnit.FootPounds, SIMCONNECT_DATATYPE.FLOAT64));
            eDefinition.Add(new SimProperty(FsSimVar.TURBTHRUST3, FsUnit.FootPounds, SIMCONNECT_DATATYPE.FLOAT64));
            eDefinition.Add(new SimProperty(FsSimVar.TURBTHRUST4, FsUnit.FootPounds, SIMCONNECT_DATATYPE.FLOAT64));

            fsConnect.RegisterDataDefinition<PlaneEngineData>(Definitions.PlaneEngineData, eDefinition);

            List<SimProperty> wDefinition = new List<SimProperty>();
            wDefinition.Add(new SimProperty(FsSimVar.AmbientAirTemperature, FsUnit.Celsius, SIMCONNECT_DATATYPE.FLOAT64));
            wDefinition.Add(new SimProperty(FsSimVar.AmbientBarometerPressure, FsUnit.Millibars, SIMCONNECT_DATATYPE.FLOAT64));
            wDefinition.Add(new SimProperty(FsSimVar.AmbientDensity, FsUnit.SlugsPerCubiFeet, SIMCONNECT_DATATYPE.FLOAT64));
            wDefinition.Add(new SimProperty(FsSimVar.AmbientInCloud, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            wDefinition.Add(new SimProperty(FsSimVar.AmbientPrecipState, FsUnit.Meter, SIMCONNECT_DATATYPE.FLOAT64));//  2 = None 4 = Rain 8 = Snow
            wDefinition.Add(new SimProperty(FsSimVar.AmbientPressure, FsUnit.Millibars, SIMCONNECT_DATATYPE.FLOAT64));
            wDefinition.Add(new SimProperty(FsSimVar.AmbientSeaLevelPressure, FsUnit.Millibars, SIMCONNECT_DATATYPE.FLOAT64));
            wDefinition.Add(new SimProperty(FsSimVar.AmbientStandardAtmTemperature, FsUnit.Celsius, SIMCONNECT_DATATYPE.FLOAT64));
            wDefinition.Add(new SimProperty(FsSimVar.AmbientTemperature, FsUnit.Celsius, SIMCONNECT_DATATYPE.FLOAT64));
            wDefinition.Add(new SimProperty(FsSimVar.AmbientVisibility, FsUnit.Meter, SIMCONNECT_DATATYPE.FLOAT64));
            wDefinition.Add(new SimProperty(FsSimVar.AmbientWindDirection, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            wDefinition.Add(new SimProperty(FsSimVar.AmbientWindVelocity, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            wDefinition.Add(new SimProperty(FsSimVar.LocalDayOfYear, FsUnit.Meter, SIMCONNECT_DATATYPE.FLOAT64));
            wDefinition.Add(new SimProperty(FsSimVar.LocalTime, FsUnit.Second, SIMCONNECT_DATATYPE.FLOAT64));
            wDefinition.Add(new SimProperty(FsSimVar.TimeOfDay, FsUnit.Meter, SIMCONNECT_DATATYPE.FLOAT64));// 0 = Dawn      1 = Day     2 = Dusk    3 = Night



            fsConnect.RegisterDataDefinition<WeatherReport>(Definitions.WeatherData, wDefinition);
        }

        private void changeButtonStatus(bool active, Button btn, bool? enabled = null, string text = "")
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

        private static IEnumerable<T> FindLogicalChildren<T>(DependencyObject depObj) where T : DependencyObject
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
        private void processSettings(bool write = true)
        {
            foreach (ComboBox tb in FindLogicalChildren<ComboBox>(Application.Current.MainWindow))
            {
                if (tb.Name != null && tb.Name != "towPlaneModel")
                {
                    if (write && assistantSettings.ContainsKey(tb.Name))
                    {
                        ComboBox cb = (ComboBox)window.FindName(tb.Name);

                        if (cb.IsEditable == true)
                            cb.Text = assistantSettings[tb.Name].ToString();
                        else if (cb.Items.Count > (int)assistantSettings[tb.Name])
                        {
                            cb.SelectedIndex = (int)assistantSettings[tb.Name];
                            Console.WriteLine(tb.Name + " " + assistantSettings[tb.Name].ToString() + "(" + cb.Items.Count + ")");
                        }
                        else
                        {
                            assistantSettings[tb.Name] = 0;
                        }
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

            foreach (CheckBox tb in FindLogicalChildren<CheckBox>(Application.Current.MainWindow))
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

            foreach (Slider tb in FindLogicalChildren<Slider>(Application.Current.MainWindow))
                if (tb.Name != null)
                {
                    if (write && assistantSettings.ContainsKey(tb.Name))
                    {
                        if (tb.Name == "RadarScale")
                        {
                            assistantSettings[tb.Name] = Math.Min(maxRadarScale, assistantSettings[tb.Name]);
                        }

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

            foreach (TextBox tb in FindLogicalChildren<TextBox>(Application.Current.MainWindow))
                if (tb.Name != null)
                {
                    if (write)
                    //if (write && assistantSettings.ContainsKey(tb.Name + "_"))
                    {
                        foreach (var val in assistantSettings)
                        {
                            if (val.Key.StartsWith(tb.Name + "_"))
                            {
                                tb.Text = val.Key.Replace(tb.Name + "_", "");
                            }
                        }
                    }
                    else if (!write)
                    {
                        if (!assistantSettings.ContainsKey(tb.Name + "_" + tb.Text))
                            assistantSettings.Add(tb.Name + "_" + tb.Text, 0);
                        else
                            assistantSettings[tb.Name + "_" + tb.Text] = 0;
                    }
                }
        }

        private void loadSettings()
        {
            if (File.Exists("assistantSettings.json"))
            {
                try
                {
                    JsonConvert.PopulateObject(File.ReadAllText("assistantSettings.json"), assistantSettings);
                    processSettings(true);

                    WindowBackground.Opacity = assistantSettings.ContainsKey("transparentBackground") && assistantSettings["transparentBackground"] == 1 ? 0.2 : 1;
                    Application.Current.MainWindow.Topmost = assistantSettings.ContainsKey("alwaysOnTop") && assistantSettings["alwaysOnTop"] == 1;
                    OnTopIcon.Content = getIconImage(alwaysOnTop.IsChecked == true, (OnTopIcon.Tag.ToString()));
                    TransparentIcon.Content = getIconImage(transparentBackground.IsChecked == true, (TransparentIcon.Tag.ToString()));

                    enableApiThermals(thermalsKK7Button, assistantSettings.ContainsKey("KK7Autoload") && assistantSettings["KK7Autoload"] == 1);
                    enableApiThermals(thermalsOpenAipButton, assistantSettings.ContainsKey("OpenAipAutoload") && assistantSettings["OpenAipAutoload"] == 1);
                    toggleSidebarWindow(assistantSettings.ContainsKey("sidebarActive") && assistantSettings["sidebarActive"] == 1);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }

        }

        private void saveSettings(object sender, RoutedEventArgs e)
        {
            try
            {
                if (loaded)
                {
                    processSettings(false);
                    File.WriteAllText("assistantSettings.json", JsonConvert.SerializeObject(assistantSettings));

                    // ADDITIONAL ACTIONS
                    if (sender != null && sender.GetType() == typeof(CheckBox))
                    {
                        if (((CheckBox)sender).Name == "transparentBackground")
                        {
                            WindowBackground.Opacity = ((CheckBox)sender).IsChecked == true ? 0.2 : 1;
                            TransparentIcon.Content = getIconImage(((CheckBox)sender).IsChecked == true, (TransparentIcon.Tag.ToString()));
                        }
                        else if (((CheckBox)sender).Name == "alwaysOnTop")
                        {
                            Application.Current.MainWindow.Topmost = ((CheckBox)sender).IsChecked == true;
                            OnTopIcon.Content = getIconImage(((CheckBox)sender).IsChecked == true, (OnTopIcon.Tag.ToString()));
                        }
                        else if (((CheckBox)sender).Name == "KK7Autoload")
                        {
                            enableApiThermals(thermalsKK7Button, ((CheckBox)sender).IsChecked == true);
                        }
                        else if (((CheckBox)sender).Name == "OpenAipAutoload")
                        {
                            enableApiThermals(thermalsOpenAipButton, ((CheckBox)sender).IsChecked == true);
                        }
                        else if (((CheckBox)sender).Name == "sidebarActive")
                        {
                            toggleSidebarWindow(((CheckBox)sender).IsChecked == true);
                        }
                    }
                    else if (sender != null && sender.GetType() == typeof(ComboBox))
                    {
                        if (((ComboBox)sender).Name == "nmeaServer")
                        {
                            if (((ComboBox)sender).SelectedIndex != 0)
                            {
                                ServerStop();
                                ServerStart(((ComboBoxItem)((ComboBox)sender).SelectedItem).Tag.ToString());
                            }
                            else
                            {
                                ServerStop();
                            }
                        }
                    }
                    else if (sender != null && sender.GetType() == typeof(Slider))
                    {
                        if (((Slider)sender).Name == "RequestsFrequency")
                        {
                            launchTimer.Interval = new TimeSpan(0, 0, 0, 0, 1000 / Math.Max(5, (int)assistantSettings["RequestsFrequency"]));
                            //Console.WriteLine("NEW INTERVAL: " + launchTimer.Interval.ToString());
                        }
                        else if (((Slider)sender).Name == "RadarScale")
                        {
                            _radarClass.updateRadarCover(RadarCanvas, assistantSettings["RadarScale"] / maxRadarScale );
                            scaleRefresh = 2;
                        }
                    }
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
            if (e.Uri.ToString().Contains("msfs.touching.cloud"))
            {
                resetMissedUpdates(null, null);
            }

            System.Diagnostics.Process.Start(e.Uri.ToString().Contains("//") ? e.Uri.AbsoluteUri : e.Uri.ToString());
        }

        private void kk7WebMap(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://thermal.kk7.ch/");
        }
        private void OpenAipWebMap(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start("http://maps.openaip.net/");
        }


        private void toggleControlOptions(object sender, RoutedEventArgs e)
        {
            foreach (var element in ((StackPanel)((Button)sender).Parent).Children)
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
        private void showMessage(string text, FsConnect _fsConnect)
        {
            if (assistantSettings["displayTips"] == 1)
            {
                try
                {
                    _fsConnect.SetText(text, 1);
                }
                catch { }
            }

            addLogMessage(text);
            Console.WriteLine(text);
        }

        private void addLogMessage(string text, int clr = 0)
        {
            if (System.Windows.Application.Current != null)
            {
                Color color = Colors.Black;
                if (clr == 1) { color = Colors.DarkOrange; }
                else if (clr == 2) { color = Colors.DarkRed; }

                Application.Current.Dispatcher.Invoke(() => messagesLog.Children.Insert(0, makeTextBlock(DateTime.UtcNow.ToString("u") + ": " + text, color, 10, TextWrapping.Wrap)));
            }
        }

        private void playSound(string soundName)
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

        private TextBlock makeTextBlock(string text, Color color, int size = 0, TextWrapping wrap = TextWrapping.NoWrap)
        {
            TextBlock textblock = new TextBlock();
            textblock.Text = text;
            textblock.Foreground = new SolidColorBrush(color);
            textblock.FontFamily = new FontFamily("Consolas");
            textblock.TextWrapping = wrap;
            if (size > 0)
                textblock.FontSize = size;

            return textblock;
        }

        // UI

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void OnTopIconClick(object sender, EventArgs e)
        {
            alwaysOnTop.IsChecked = !alwaysOnTop.IsChecked;
            saveSettings(alwaysOnTop, null);
        }
        private void TransparentIconClick(object sender, EventArgs e)
        {
            transparentBackground.IsChecked = !transparentBackground.IsChecked;
            saveSettings(transparentBackground, null);
        }
        private void toggleHotspotsAutoload(object sender, RoutedEventArgs e)
        {
            KK7Autoload.IsChecked = !KK7Autoload.IsChecked;
            saveSettings(KK7Autoload, null);
        }

        private void toggleOpenAipAutoload(object sender, RoutedEventArgs e)
        {
            OpenAipAutoload.IsChecked = !OpenAipAutoload.IsChecked;
            saveSettings(OpenAipAutoload, null);
        }

        private void toggleSidebar(object sender, RoutedEventArgs e)
        {
            sidebarActive.IsChecked = !sidebarActive.IsChecked;
            saveSettings(sidebarActive, null);
        }
        private void toggleMainbar(object sender, RoutedEventArgs e)
        {
            toggleMainbarWindow(thermalsDebugActive == 2);
        }


        private void toggleSidebarWindow(bool? state = null)
        {
            if (state == true || state == null && toggleSidebarButton.Text == ">")
            {
                toggleSidebarButton.Text = "<";
                thermalsDebugActive = 1;
                SidebarColumn.Width = GridLength.Auto;
                window.Width = 550;
            }
            else
            {
                toggleSidebarButton.Text = ">";
                thermalsDebugActive = 0;
                SidebarColumn.Width = new GridLength(0);
                window.Width = 275;
            }
        }
        private void toggleMainbarWindow(bool state)
        {
            if (state)
            {
                toggleMainButton.Text = ">";
                thermalsDebugActive = 1;
                MaincontentColumn.Width = GridLength.Auto;
                window.Width = 550;
            }
            else
            {
                toggleMainButton.Text = "<";
                thermalsDebugActive = 2;
                MaincontentColumn.Width = new GridLength(0);
                window.Width = 275;
            }
        }

        private void MinimizeIconClick(object sender, EventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        private void closeIconClick(object sender, EventArgs e)
        {

            if (validConnection())
            {
                toggleConnect(null, null);
            }

            ServerStop();


            //if (_fsConnect == null || MessageBox.Show("Connection currently is active. You sure to close application?", "Warning", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                Application.Current.Shutdown();
            }
        }

        private Image getIconImage(bool state, string names)
        {
            Image img = new Image();
            img.Source = new BitmapImage(new Uri(names.Split('|')[state ? 1 : 0], UriKind.Relative));

            return img;
        }

        // UPDATES START
        private void setNewsLabel(string counter)
        {
            if (counter != "0")
            {
                Application.Current.Dispatcher.Invoke(() => newsLink.Text = "(" + counter + ")");
                Application.Current.Dispatcher.Invoke(() => SettingsTab.Background = new SolidColorBrush(Color.FromArgb(10, 255, 0, 0)));
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() => newsLink.Text = "");
                Application.Current.Dispatcher.Invoke(() => SettingsTab.Background = new SolidColorBrush(Colors.Transparent));
            }
        }
        private void resetMissedUpdates(object sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() => newLastRead.Text = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds.ToString());
            saveSettings(newLastRead, null);
            setNewsLabel("0");
        }
        private void triggerCheckUpdate(object sender, EventArgs e)
        {
            _ = CheckUpdateAsync();
        }

        private async Task CheckUpdateAsync()
        {
            //string pubVer = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            {
                var client = new HttpClient();
                if (!assistantSettings.TryGetValue("newLastRead", out double date))
                    date = 0;
#if DEBUG
                string data = await client.GetStringAsync("https://msfs.touching.cloud/kineticassistant/?last_read=" + date + "&build=DEBUG");
#else
                string data = await client.GetStringAsync("https://msfs.touching.cloud/kineticassistant/?last_read=" + date + "&build=RELEASE");
#endif
                // GET NEWS COUNT
                Regex regexNews = new Regex(@"news=(\d+)");
                Match matchNews = regexNews.Match(data);
                if (matchNews.Groups.Count >= 2)
                {
                    setNewsLabel(matchNews.Groups[1].ToString());
                }
            }
        }
        // UPDATES END

        // TCP SERVER START
        private void addServerIPs()
        {
            String strHostName = Dns.GetHostName();
            IPHostEntry iphostentry = Dns.GetHostByName(strHostName);
            foreach (IPAddress ipaddress in iphostentry.AddressList)
            {
                ComboBoxItem option = new ComboBoxItem();
                option.Tag = ipaddress.ToString();
                option.Content = "XCSoar server: " + option.Tag;
                nmeaServer.Items.Add(option);
            }
        }

        private void ServerStart(string ip = "127.0.0.1")
        {
            addLogMessage("NMEA Server: START");
            if (threadServer == null)
            {
                try
                {
                    server = new Server(true, new CallbackUI(ServerStatus), ip);
                    threadServer = new Thread(new ThreadStart(server.Start));
                    threadServer.Start();
                }
                catch (Exception ex)
                {
                    addLogMessage("Can't start NMEA server: " + ex.Message);
                }

                while (!threadServer.IsAlive) ;
            }
        }

        private void ServerStop()
        {
            addLogMessage("NMEA Server: STOP");
            if (threadServer != null)
            {
                try
                {
                    server.Stop();
                    threadServer.Join(1000);
                    threadServer = null;
                }
                catch (Exception ex)
                {
                    addLogMessage("Can't stop NMEA server: " + ex.Message);
                }
            }
        }
        private void ServerStatus(ServerStatus status)
        {
            switch (status.status)
            {
                case ServerStatusCode.OK:
                    addLogMessage("NMEA Server: OK");
                    break;
                case ServerStatusCode.ERROR:
                    addLogMessage("NMEA Server: ERROR");
                    ServerStop();
                    break;
                case ServerStatusCode.CONN:
                    addLogMessage("NMEA Server: " + status.value);
                    break;
            }
        }
        // TCP SERVER ENDS
    }
}