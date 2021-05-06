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
using System.Windows.Documents;

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
        static Mutex mutex;

        string zipDirectory = "";

        private FsConnect _fsConnect;

        private PlaneInfoResponse _planeInfoResponse;
        private PlaneInfoResponse _planeInfoResponseLast;

        private PlaneInfoCommit _planeCommit;

        private PlaneAvionicsResponse _planeAvionicsResponse;
        private PlaneAvionicsResponse _planeAvionicsResponseLast;

        private PlaneInfoRotate _planeRotate;
        private PlaneEngineData _planeEngineData;
        private PlaneInfoRotateAccel _planeRotateAccel;

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
        private double windDirection = 0;
        private double windVelocity = 0;
        private double thermalFlow = 0;
        private double dayTimeModifier = 1;
        private double overcastModifier = 1;
        private int scaleRefresh = 0;

        // IF NOT 0 - TOWING ACTIVE
        private TowScanMode towScanMode = TowScanMode.Disabled;
        private double towToggledAltitude;
        public static uint TARGETMAX = 99999999;
        private uint towingTarget = TARGETMAX;
        private double lightToggled = 0;
        private KeyValuePair<uint, bool> insertedTowPlane = new KeyValuePair<uint, bool>(TARGETMAX, false);
        private double towCableLength = 0;
        private double towPrevDist = 0;
        private double towPrePrevDist = 0;
        private double towCableDesired = 40;
        private bool towPlaneInserted = false;
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

        private double lastPacketReceived = 0;
        private double lostPacketRecovered = 0;

        private double absoluteTime = 0;
        private double swCurrent = 0;
        private double swLast = 0;
        private double lastFrameTiming;
        DispatcherTimer dispatcherTimer;
        DispatcherTimer launchTimer;
        DispatcherTimer nearbyTimer;
        private double lastPreparePressed = 0;

        string optPath;
        string plnPath;

        private bool loaded = false;
        MathClass _mathClass;
        RadarClass _radarClass;
        Tracking _trackingClass;

        Dictionary<string, double> assistantSettings;
        Dictionary<string, double> controlTimestamps;

        MediaPlayer soundPlayer = null;

        bool taskInProcess = false;

        private Server server;
        private Thread threadServer;

        // RADAR
#if DEBUG
        double allowedRadarScale = 5;
        double allowedRecords = 5;
        double allowedRecordLength = 900; // SEC
#else
        double allowedRadarScale = 50;
        double allowedRecords = 999;
        double allowedRecordLength = 36000; // SEC
#endif
        double maxRadarScale = 50;
        byte[] bitmapdata;
        double lastRadarRequest = 0;
        bool httpServerActive = false;

        public MainWindow()
        {
            mutex = new Mutex(false, "KineticAssistant");

            if (!mutex.WaitOne(TimeSpan.FromSeconds(2), false))
            {
                MessageBox.Show("KineticAssistant already launched");
                Application.Current.Shutdown();
                return;
            }

            DataContext = new SimvarsViewModel();
            _mathClass = new MathClass();
            _radarClass = new RadarClass();
            _trackingClass = new Tracking();

            assistantSettings = new Dictionary<string, double>();

            // PREPARE CONTROLS DATA
            controlTimestamps = new Dictionary<string, double>();
            foreach (var field in typeof(PlaneAvionicsResponse).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
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

            // NMEA
            if (assistantSettings.ContainsKey("nmeaServer") && assistantSettings["nmeaServer"] != 0)
            {
                ServerStart(((ComboBoxItem)nmeaServer.SelectedItem).Tag.ToString());
            }

            // HTTP
            if (assistantSettings.ContainsKey("panelServer") && assistantSettings["panelServer"] != 0)
            {
                startHTTP();
            }

            string path = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + @"\kinetic-panel.zip";
            if (File.Exists(path))
            {
                TextBlock txt = makeTextBlock("", Colors.Blue);
                Hyperlink link = new Hyperlink();

                link.NavigateUri = new Uri(path);
                link.RequestNavigate += Hyperlink_RequestNavigate;
                link.Inlines.Add("Install Kinetic Panel");
                link.FontSize = 14;
                txt.HorizontalAlignment = HorizontalAlignment.Center;
                txt.Inlines.Add(link);

                settingsContainer.Children.Add(txt);
            }

            // COMMON SIM VALUES REQUEST TIMER
            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 250);
            dispatcherTimer.Tick += new EventHandler(commonInterval);
            dispatcherTimer.Start();

            launchTimer = new DispatcherTimer();
            launchTimer.Interval = new TimeSpan(0, 0, 0, 0, 1000 / Math.Max(10, (int)(assistantSettings.ContainsKey("RequestsFrequency") ? assistantSettings["RequestsFrequency"] : 10)));
            launchTimer.Tick += new EventHandler(launchInterval);
            launchTimer.Start();

            nearbyTimer = new DispatcherTimer();
            nearbyTimer.Interval = new TimeSpan(0, 0, 0, 0, 20000 / Math.Max(10, (int)(assistantSettings.ContainsKey("RequestsFrequency") ? assistantSettings["RequestsFrequency"] : 10)));
            nearbyTimer.Tick += new EventHandler(nearbyInterval);
            nearbyTimer.Start();

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
                _fsConnect.RequestData(Requests.PlaneAvionics, Definitions.PlaneAvionics);

                if (_winchPosition == null && _carrierPosition == null && targedCatapultVelocity == 0 && !thermalsWorking && towingTarget == TARGETMAX && !taskInProcess)
                {
                    lastPacketReceived += 0.25;
                    _fsConnect.RequestData(Requests.PlaneInfo, Definitions.PlaneInfo);

                    // PACKET LOST??
                    if (lastPacketReceived >= 5)
                    {
                        Console.WriteLine("Packet lost, reconnect (" + lastPacketReceived + "s)");
                        toggleQuickConnect(false);
                        toggleQuickConnect(true);
                    }
                }

                if (towScanMode > TowScanMode.Disabled && towingTarget == TARGETMAX && !_trackingClass.ghostPlayerActive())
                    _fsConnect.RequestData(Requests.NearbyObjects, Definitions.NearbyObjects, (uint)(1000 * Math.Min(allowedRadarScale, (assistantSettings.ContainsKey("RadarScale") ? assistantSettings["RadarScale"] : allowedRadarScale))), getTowObjectType());

                if (taskInProcess)
                    _fsConnect.RequestData(Requests.PlaneEngineData, Definitions.PlaneEngineData);

                // RADAR PANEL
                if (assistantSettings.ContainsKey("panelServer") && assistantSettings["panelServer"] != 0 && (absoluteTime - lastRadarRequest) <= 0.25)
                {
                    RadarContainer.Background = new SolidColorBrush(Colors.White);
                    generateRadarBitmap();
                }
                else if (absoluteTime - lastRadarRequest > 1 && absoluteTime - lastRadarRequest < 2) // RESTORE TRANSPARENCY
                {
                    RadarContainer.Background = new SolidColorBrush(Colors.Transparent);
                }
            }
        }

        public void generateRadarBitmap()
        {
            Rect bounds = new Rect(1, 1, 275, 250);
            double dpi = 96d;

            RenderTargetBitmap rtb = new RenderTargetBitmap((int)bounds.Width, (int)bounds.Height, dpi, dpi, PixelFormats.Pbgra32);

            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                VisualBrush vb = new VisualBrush(RadarContainer);
                dc.DrawRectangle(vb, null, new Rect(new Point(), bounds.Size));
            }

            rtb.Render(dv);

            MemoryStream ms = new MemoryStream();
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            encoder.Save(ms);
            bitmapdata = ms.ToArray();

            //rtbString = Convert.ToBase64String(bitmapdata);

            //Console.WriteLine("Radar generate");
        }

        private void launchInterval(object sender, EventArgs e)
        {
            if (validConnection())
            {
                if (_winchPosition != null || _carrierPosition != null || targedCatapultVelocity != 0 || thermalsWorking || towingTarget != TARGETMAX || taskInProcess)
                {
                    lastPacketReceived += 1 / (double)Math.Max(10, (int)(assistantSettings.ContainsKey("RequestsFrequency") ? assistantSettings["RequestsFrequency"] : 10));
                    _fsConnect.RequestData(Requests.PlaneInfo, Definitions.PlaneInfo);

                    // PACKET LOST??
                    if (lastPacketReceived >= 5)
                    {
                        Console.WriteLine("Packet lost, reconnect (" + lastPacketReceived + "s)");
                        toggleQuickConnect(false);
                        toggleQuickConnect(true);
                    }
                }

                if (towScanMode > TowScanMode.Disabled && (towingTarget != TARGETMAX || _trackingClass.ghostPlayerActive()))
                    _fsConnect.RequestData(Requests.NearbyObjects, Definitions.NearbyObjects, (uint)(1000 * Math.Min(allowedRadarScale, assistantSettings["RadarScale"])), getTowObjectType());

                if (assistantSettings.ContainsKey("towType") && assistantSettings["towType"] == 2 && _planeAvionicsResponse.SimOnGround == 100 && _planeInfoResponse.GpsGroundSpeed < 5 &&
                    (launchTime == 0 || launchTime - absoluteTime > 0) && (_nearbyInfoResponse == null || !_nearbyInfoResponse.ContainsKey(towingTarget)))
                {
                    levelUpGlider(true);
                }
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
            switch ((int)assistantSettings["towType"])
            {
                case 1:
                    return SIMCONNECT_SIMOBJECT_TYPE.AIRCRAFT;
                case 2:
                    return SIMCONNECT_SIMOBJECT_TYPE.AIRCRAFT;
                case 3:
                    return SIMCONNECT_SIMOBJECT_TYPE.GROUND;
                case 4:
                    return SIMCONNECT_SIMOBJECT_TYPE.BOAT;
                case 5:
                    return SIMCONNECT_SIMOBJECT_TYPE.HELICOPTER;
                default:
                    return SIMCONNECT_SIMOBJECT_TYPE.ALL;
            }

        }

        private void toggleQuickConnect(bool newStatus)
        {
            if (newStatus)
            {
                try
                {
                    _fsConnect = new FsConnect();
                    _fsConnect.Connect("Kinetic Assistant", "127.0.0.1", 500, SimConnectProtocol.Ipv4);
                }
                catch (Exception ex)
                {
                    addLogMessage(ex.Message, 2);
                    return;
                }

                _fsConnect.FsDataReceived += HandleReceivedFsData;
                //_fsConnect.AirportDataReceived += HandleReceivedAirports;
                _fsConnect.ObjectAddremoveEventReceived += HandleReceivedSystemEvent;
                //_fsConnect.SystemEventReceived += HandleReceivedSystemEvent;

                Console.WriteLine("Initializing data definitions");
                InitializeDataDefinitions(_fsConnect);

                _planeInfoResponse.AbsoluteTime = 0;
                _planeInfoResponseLast.AbsoluteTime = 0;

                lastPacketReceived = 0;
                lostPacketRecovered = 0;

                _fsConnect.RequestData(Requests.PlaneInfo, Definitions.PlaneInfo);
                //_fsConnect.RequestData(Requests.PlaneCommit, Definitions.PlaneCommit);
                //_fsConnect.RequestFacilitiesList(Requests.Airport);

                ClearRecords(null, null);
            }
            else
            {
                try
                {
                    _fsConnect.Disconnect();
                    _fsConnect.Dispose();
                }
                catch (Exception ex)
                {
                    addLogMessage(ex.Message, 2);
                }

                _fsConnect = null;
            }
        }

        private void toggleConnect(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("toggleConnect");

            if (!validConnection())
            {
                Application.Current.Dispatcher.Invoke(() => _radarClass.InitRadar(RadarCanvas, assistantSettings["RadarScale"] / maxRadarScale, allowedRadarScale));

                try
                {
                    _fsConnect = new FsConnect();
                    _fsConnect.Connect("Kinetic Assistant", "127.0.0.1", 500, SimConnectProtocol.Ipv4);
                }
                catch (Exception ex)
                {
                    addLogMessage(ex.Message, 2);
                    return;
                }

                _fsConnect.FsDataReceived += HandleReceivedFsData;
                //_fsConnect.AirportDataReceived += HandleReceivedAirports;
                _fsConnect.ObjectAddremoveEventReceived += HandleReceivedSystemEvent;
                //_fsConnect.SystemEventReceived += HandleReceivedSystemEvent;

                Console.WriteLine("Initializing data definitions");
                InitializeDataDefinitions(_fsConnect);

                addLogMessage("Kinetic Assistant connected");
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

                lastPacketReceived = 0;
                lostPacketRecovered = 0;
                _planeInfoResponse.AbsoluteTime = 0;
                _planeInfoResponseLast.AbsoluteTime = 0;

                _fsConnect.RequestData(Requests.PlaneInfo, Definitions.PlaneInfo);
                //_fsConnect.RequestData(Requests.PlaneCommit, Definitions.PlaneCommit);
                //_fsConnect.RequestFacilitiesList(Requests.Airport);
            }
            else
            {
                //Application.Current.Dispatcher.Invoke(() => _radarClass.ClearRadar(RadarCanvas));

                if (towScanMode > TowScanMode.Disabled)
                {
                    toggleScanning(null, null);
                }

                try
                {
                    addLogMessage("Kinetic Assistant disconnected");
                    //Application.Current.Dispatcher.Invoke(() => playSound("error"));
                    _fsConnect.Disconnect();
                    _fsConnect.Dispose();
                }
                catch (Exception ex)
                {
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

        private bool restrictionsPassed()
        {
            if (assistantSettings["realisticRestrictions"] == 0 || _planeAvionicsResponse.BrakeParkingPosition != 100 && (_planeAvionicsResponse.SimOnGround == 100 || _planeInfoResponse.AltitudeAboveGround <= _planeAvionicsResponse.StaticCGtoGround * 1.25))
                return true;

            return false;
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
                _planeAvionicsResponse.WaterRudderHandlePosition = 0;
                _planeAvionicsResponseLast.WaterRudderHandlePosition = 0;

                if (_winchPosition == null) // PREPARE TO LAUNCH
                {
                    if (restrictionsPassed())
                    {
                        _planeAvionicsResponse.WaterRudderHandlePosition = 50;
                        _planeAvionicsResponseLast.WaterRudderHandlePosition = 50;

                        lastPreparePressed = absoluteTime;
                        addLogMessage("Creating winch");

                        cableLength = Math.Max(100, assistantSettings["stringLength"]);
                        _winchPosition = _mathClass.getWinchPosition(_planeInfoResponse, cableLength - 10);

                        Application.Current.Dispatcher.Invoke(() => _radarClass.InsertWinch(RadarCanvas));
                        Application.Current.Dispatcher.Invoke(() => RadarScale.Value = Math.Min(maxRadarScale, cableLength / 1000 * 1.2));

                        Console.WriteLine($"Current location: {_planeInfoResponse.Latitude * 180 / Math.PI} {_planeInfoResponse.Longitude * 180 / Math.PI}");
                        Console.WriteLine($"Winch location: {_winchPosition.location.Latitude * 180 / Math.PI} {_winchPosition.location.Longitude * 180 / Math.PI}");
                        Console.WriteLine($"Bearing: {_planeInfoResponse.PlaneHeading * 180 / Math.PI}deg Distance: {cableLength / 1000}km");

                        //showMessage("Winch cable connected - disengage parking brakes to launch", _fsConnect);
                        Application.Current.Dispatcher.Invoke(() => changeButtonStatus(false, launchPrepareButton, true, "Release winch cable"));
                        launchTime = absoluteTime + 5.001;
                        showMessage("Launch in " + Math.Floor(launchTime - absoluteTime) + " seconds", _fsConnect);
                        Application.Current.Dispatcher.Invoke(() => playSound("true"));
                    }
                    else
                    {
                        showMessage("Winch can be activated only on the ground and with brakes disengaged", _fsConnect);
                        Application.Current.Dispatcher.Invoke(() => playSound("error"));
                    }
                }
                else // ABORT LAUNCH
                {
                    showMessage("Winch cable released. Gained altitude: " + (_planeInfoResponse.Altitude - _winchPosition.alt).ToString("0.0") + " meters", _fsConnect);
                    Application.Current.Dispatcher.Invoke(() => playSound("false"));
                    Application.Current.Dispatcher.Invoke(() => abortLaunch());
                }

                commitAvionicsData();
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

                _planeAvionicsResponse.WaterRudderHandlePosition = 0;
                _planeAvionicsResponseLast.WaterRudderHandlePosition = 0;

                if (validConnection())
                    commitAvionicsData();
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

                _planeAvionicsResponse.WaterRudderHandlePosition = 0;
                _planeAvionicsResponseLast.WaterRudderHandlePosition = 0;
                commitAvionicsData();
            }
            else
            {
                // GET ANGLE TO WINCH POSITION
                winchDirection _winchDirection = _mathClass.getForceDirection(_winchPosition, _planeInfoResponse);
                double targetVelocity = 0.514 * Math.Max(10, assistantSettings["targetSpeed"]);
                double bodyAcceleration = 0;

                if (thermalsDebugActive > 0 || httpServerActive)
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
                levelUpGlider();

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

        public void levelUpGlider(bool control = false)
        {
            if (_planeAvionicsResponse.SimOnGround == 100 && _planeInfoResponse.GpsGroundSpeed < 5)
            {
                _planeRotate.RotationVelocityBodyX = 0;
                _planeRotate.RotationVelocityBodyY = _planeAvionicsResponse.RudderPosition / 500 * lastFrameTiming;
                _planeRotate.RotationVelocityBodyZ = -Math.Sin(_planeInfoResponse.PlaneBank) * Math.Pow(Math.Abs(Math.Sin(_planeInfoResponse.PlaneBank)), 0.5);

                //Console.WriteLine($"Leveling {_planeRotate.RotationVelocityBodyZ:F5}");

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

                if (control)
                {
                    double pushSpeed = 0.9 * _planeInfoResponse.GpsGroundSpeed + 0.1 * -_planeAvionicsResponse.YokeYPosition * lastFrameTiming / 10;
                    _planeCommit.VelocityBodyX = 0;
                    _planeCommit.VelocityBodyY = pushSpeed * Math.Sin(_planeInfoResponse.PlanePitch);
                    _planeCommit.VelocityBodyZ = pushSpeed;

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

            //Console.WriteLine($"{type}: {bodyAcceleration / 9.81:F2}g {(type == "Winch" ? cableLength : towCableLength):F2}m / {_winchDirection.distance:F2}m h{(_winchDirection.heading * 180 / Math.PI):F0}deg p{(_winchDirection.pitch * 180 / Math.PI):F0}deg");

            double angleHLimit = (_planeAvionicsResponse.SimOnGround == 0 ? 89 : 179) * Math.PI / 180;
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
                double degreeThershold = (_planeAvionicsResponse.SimOnGround == 0 ? 20 : 1) * Math.PI / 180;
                double accelThreshold = 9.81 / (_planeAvionicsResponse.SimOnGround == 0 ? 5 : 50);

                if (bodyAcceleration > accelThreshold && connectionPoint != 0 && (Math.Abs(_winchDirection.pitch) > degreeThershold || Math.Abs(_winchDirection.heading) > degreeThershold))
                {
                    double rotationForce = 10 * (bodyAcceleration - accelThreshold) / accelerationLimit;

                    double sinHeading = Math.Sign(_winchDirection.heading) * Math.Pow(Math.Abs(Math.Sin(_winchDirection.heading / 2)), 1.5);
                    double sinPitch = Math.Sign(_winchDirection.pitch) * Math.Pow(Math.Abs(Math.Sin(_winchDirection.pitch / 2)), 1.5);

                    //Console.WriteLine("Math.Sign(_winchDirection.pitch)" + Math.Sign(_winchDirection.pitch) + " Math.Sin(_winchDirection.pitch / 2):" + Math.Sin(_winchDirection.pitch / 2) + " Math.Pow(Math.Sin(_winchDirection.pitch / 2), 1.5):" + Math.Pow(Math.Sin(_winchDirection.pitch / 2), 1.5));
                    //Console.WriteLine("_planeRotate.RotationVelocityBodyX:" + _planeRotate.RotationVelocityBodyX + " rotationForce:" + rotationForce + " rotationForce:" + rotationForce + " sinPitch:" + sinPitch + " lastFrameTiming:" + lastFrameTiming);

                    _planeRotateAccel.RotationAccelerationBodyX += -rotationForce * sinPitch * lastFrameTiming;
                    _planeRotateAccel.RotationAccelerationBodyY += (_planeAvionicsResponse.SimOnGround == 0 ? rotationForce : 10 * Math.Pow(Math.Abs(rotationForce / 10), 0.1)) * sinHeading
                        * lastFrameTiming;
                    _planeRotateAccel.RotationAccelerationBodyZ += (lastFrameTiming * rotationForce * sinHeading) * lastFrameTiming;

                    //Console.WriteLine($"Pitch {_planeRotateAccel.RotationAccelerationBodyX:F2} Heading {_planeRotateAccel.RotationAccelerationBodyY:F2}");

                    if (!double.IsNaN(_planeRotateAccel.RotationAccelerationBodyX) && Math.Abs(_planeRotateAccel.RotationAccelerationBodyX) < 1000 && 
                        !double.IsNaN(_planeRotateAccel.RotationAccelerationBodyY) && Math.Abs(_planeRotateAccel.RotationAccelerationBodyY) < 1000 && 
                        !double.IsNaN(_planeRotateAccel.RotationAccelerationBodyZ) && Math.Abs(_planeRotateAccel.RotationAccelerationBodyZ) < 1000)
                    {
                        try
                        {
                            _fsConnect.UpdateData(Definitions.PlaneRotateAccel, _planeRotateAccel);
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
                if (restrictionsPassed())
                {
                    if (towPlaneTrack.Items.Count > 0)
                    {
                        string trackName = ((ComboBoxItem)(towPlaneTrack.SelectedItem)).Tag.ToString();
                        if (!string.IsNullOrEmpty(trackName) && File.Exists(trackName))
                        {
                            Console.WriteLine("Tow plane inserting");
                            ClearRecords(null, null);
                            changePlayStatus(false);
                            beforeLoadTrackRecord(1);
                            loadTrackRecord(trackName, true);
                            towPlaneInserted = true;
                            Console.WriteLine("Tow plane inserted");
                        }
                    }
                    else
                    {
                        showMessage("No flight records found in TOWPLANES folder", _fsConnect);
                    }
                }
                else
                {
                    showMessage("Tow plane can be inserted only on the ground and with brakes disengaged", _fsConnect);
                }
            }
        }

        private void assignTowPlane(uint id, winchPosition pos)
        {
            winchDirection direction = _mathClass.getForceDirection(pos, _planeInfoResponse);

            double menuValue = 0.2;
            towCableDesired = 40;
            double cableLength = towCableDesired;

            Console.WriteLine("assignTowPlane in " + direction.distance + "m");

            //towSearchRadius.Text = menuValue.ToString(".0");
            towCableDesired *= 2;
            cableLength = towCableDesired - 10;
            //teleportTowPlane(id);
            //return;

            if (RadarScale.Value < menuValue)
            {
                RadarScale.Value = menuValue;
                assistantSettings["RadarScale"] = RadarScale.Value;
                saveSettings(RadarScale, null);
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
            towInfo.VelocityBodyX = towInfo.VelocityBodyY = towInfo.VelocityBodyZ = 0;

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

        private void teleportGhostPlane(GhostPlane gp, double progress = 0)
        {
            if (gp.TrackPoints.Count > 0)
            {
                int i = 0;
                foreach (TrackPoint point in gp.TrackPoints)
                {
                    if (point.Timer >= progress)
                        break;

                    i++;
                }

                if (i < gp.TrackPoints.Count)
                {

                    TowInfoResponse towInfo = new TowInfoResponse();
                    towInfo.Altitude = gp.TrackPoints[i].Elevation;
                    towInfo.Latitude = gp.TrackPoints[i].Location.Latitude;
                    towInfo.Longitude = gp.TrackPoints[i].Location.Longitude;
                    towInfo.Heading = gp.TrackPoints[i].Heading * Math.PI / 180;
                    towInfo.Bank = gp.TrackPoints[i].Roll * Math.PI / 180;
                    towInfo.VelocityBodyX = towInfo.VelocityBodyY = towInfo.VelocityBodyZ = 0;

                    if (!double.IsNaN(towInfo.Altitude) && !double.IsNaN(towInfo.Latitude) && !double.IsNaN(towInfo.Longitude) && !double.IsNaN(towInfo.Heading) && !double.IsNaN(towInfo.Bank))
                    {
                        try
                        {
                            _fsConnect.UpdateData(Definitions.TowPlane, towInfo, gp.ID);
                        }
                        catch (Exception ex)
                        {
                            addLogMessage(ex.Message);
                        }
                    }
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

                            string title = _trackingClass.possiblyGetPlaneName(obj.Key, obj.Value.title.Replace("_", " "));
                            Console.WriteLine("title: " + title);
                            label.Content = title + " (" + dir.distance.ToString(".0m") + ")";

                            if (dir.distance < 2)
                            {
                                // SKIP PLAYER
                            }
                            // AVAILABLE OBJECTS + CURRENT TARGET
                            else if (dir.distance <= 1000 * Math.Min(allowedRadarScale, assistantSettings["RadarScale"]) || obj.Key == towingTarget)
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
                                    if ((_nearbyInfoResponse.ContainsKey(obj.Key) || _nearbyInfoResponseLast.ContainsKey(obj.Key) || _nearbyInfoResponsePreLast.ContainsKey(obj.Key)) && restrictionsPassed())
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
                        /*if (_planeInfoResponse.AbsoluteTime - insertTowPlanePressed > 2 && towScanMode == TowScanMode.TowInsert)
                        {
                            if (plnPath != "")
                            {
                                //createFlightPlan();
                                insertTowPlane();

                                // CONTINUE SEARCH
                                towScanMode = TowScanMode.TowSearch;
                            }
                        }
                        // AI TOW INSERTED BUT NOT FOUND - TRY TO TELEPORT IT
                        else */if (towingTarget == TARGETMAX && (_planeInfoResponse.AbsoluteTime - insertTowPlanePressed) > 6 && towScanMode >= TowScanMode.TowSearch && insertedTowPlane.Key != TARGETMAX && !insertedTowPlane.Value)
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

                        if (thermalsDebugActive > 0 || httpServerActive)
                            Application.Current.Dispatcher.Invoke(() => _radarClass.InsertRadarNearby(RadarCanvas, nearbyDict, this));

                    }
                    catch (Exception ex)
                    {
                        addLogMessage(ex.Message);
                    }


                    // SET REPLAY SLIDERS
                    try
                    {
                        foreach (GhostPlane ghostPlane in _trackingClass.ghostPlanes)
                        {
                            if (ghostPlane.Progress != 0 && ghostPlane.ID != 99999999)
                            {
                                foreach (StackPanel group in GhostsList.Children)
                                    if (group.Tag.ToString() == ghostPlane.ID.ToString())
                                    {
                                        foreach (var el in group.Children)
                                            if (el.GetType() == typeof(Slider))
                                            {
                                                ((Slider)el).Value = ghostPlane.Progress;
                                                break;
                                            }

                                        break;
                                    }
                            }
                        }
                    }
                    catch { }
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
            _planeAvionicsResponse.WaterRudderHandlePosition = 0;
            _planeAvionicsResponseLast.WaterRudderHandlePosition = 0;

            if (towingTarget == id)
            {
                _planeAvionicsResponse.WaterRudderHandlePosition = 0;
                _planeAvionicsResponseLast.WaterRudderHandlePosition = 0;

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
            else if (restrictionsPassed())
            {
                if (assistantSettings.ContainsKey("towType") && assistantSettings["towType"] == 1) {
                    _planeAvionicsResponse.WaterRudderHandlePosition = 100;
                    _planeAvionicsResponseLast.WaterRudderHandlePosition = 100;
                }

                towingTarget = id;
                lightToggled = absoluteTime;

                // FINISH SEARCH
                towScanMode = TowScanMode.Scan;
                //insertedTowPlane = new KeyValuePair<uint, bool>(id, true);

                if (position == null && _nearbyInfoResponse.ContainsKey(id)) { position = _nearbyInfoResponse[id]; }

                if (position != null)
                {
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
                    showMessage("Failed to start towing", _fsConnect);
                }
            }
            else
            {
                showMessage("Tow rope can be connected only on the ground and with brakes disengaged", _fsConnect);
            }

            Application.Current.Dispatcher.Invoke(() => nearbyInterval(new object(), new EventArgs()));

            if (validConnection())
                commitAvionicsData();
        }

        private void processTowing()
        {
            //Console.WriteLine(Environment.NewLine + "processTowing");

            if (_nearbyInfoResponse.ContainsKey(towingTarget))
            {
                // BLINK LIGHTS
                if (towingTarget != insertedTowPlane.Key && absoluteTime - lightToggled > 1.5)
                {
                    lightToggled = absoluteTime;
                    _planeAvionicsResponse.LIGHTLANDING = _planeAvionicsResponse.LIGHTLANDING == 100 ? 0 : 100;
                    _planeAvionicsResponse.LIGHTTAXI = _planeAvionicsResponse.LIGHTLANDING == 100 ? 0 : 100;
                    commitAvionicsData();
                }

                double bodyAcceleration = 0;

                // GET ANGLE TO TUG POSITION
                winchPosition winchPosition = _nearbyInfoResponse[towingTarget];
                winchDirection winchDirection = _mathClass.getForceDirection(winchPosition, _planeInfoResponse);

                // SET DESIRED ROPE LENGTH
                towCableDesired = Math.Max(/*_planeAvionicsResponse.SimOnGround != 100 && _planeAvionicsResponse.OnAnyRunway != 100 ? 80 : 40*/80, winchPosition.airspeed);

                // LEVEL UP GLIDER BEFORE LAUNCH
                levelUpGlider();

                // GET FINAL CABLE TENSION
                double accelerationLimit = (assistantSettings["realisticFailures"] == 1 ? 8 : 40) * 9.81;
                if (_planeAvionicsResponse.SimOnGround == 100)
                {
                    accelerationLimit /= 2;
                }
                double cableTension = _mathClass.getCableTension(towCableLength, Math.Max(1, assistantSettings["elasticExtension"]), winchDirection);
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
                    if (_planeAvionicsResponse.BrakeParkingPosition == 100 && (_planeAvionicsResponse.SimOnGround == 100 || _planeInfoResponse.AltitudeAboveGround <= _planeAvionicsResponse.StaticCGtoGround * 1.25))
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
                _planeAvionicsResponse.LaunchbarPosition = 0;
                targedCatapultVelocity = 0;

                if (validConnection())
                    commitAvionicsData();
            }
        }
        private void processLaunchpad()
        {
            if (launchpadAbortInitiated)
            {
                _planeAvionicsResponse.LaunchbarPosition = 0;
                commitAvionicsData();
                targedCatapultVelocity = 0;
                launchpadAbortInitiated = false;
            }
            else if (_planeAvionicsResponse.BrakeParkingPosition == 0)
            {
                // ANIMATE LAUNCHPAD
                if (_planeAvionicsResponse.LaunchbarPosition != 100) { _planeAvionicsResponse.LaunchbarPosition = 100; commitAvionicsData(); }

                // ENCREASE SPEED
                if (_planeCommit.VelocityBodyZ < 0.514 * assistantSettings["catapultTargetSpeed"])
                {
                    double diff = lastFrameTiming * (0.514 * assistantSettings["catapultTargetSpeed"] - targedCatapultVelocity);
                    targedCatapultVelocity -= diff;
                    _planeCommit.VelocityBodyZ += diff;

                    if (_planeCommit.VelocityBodyZ >= 0.9 * 0.514 * assistantSettings["catapultTargetSpeed"] || _planeAvionicsResponse.SimOnGround != 100)
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
                double accel = _planeCommit.VelocityBodyZ - _planeInfoResponseLast.VelocityBodyZ;
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
                _planeAvionicsResponse.TailhookPosition = 0;
                commitAvionicsData();

                arrestorsAbortInitiated = false;
                _carrierPosition = null;
                arrestorConnectedTime = 0;
                //showMessage("Tailhook retracted", _fsConnect);
                Application.Current.Dispatcher.Invoke(() => changeButtonStatus(_fsConnect != null ? true : false, hookPrepareButton, true, "Deploy tailhook"));
            }
            else
            {
                // ANIMATE HOOK
                if (_planeAvionicsResponse.TailhookPosition < 100)
                {
                    _planeAvionicsResponse.TailhookPosition = Math.Min(_planeAvionicsResponse.TailhookPosition + 50 * Math.Min(0.1, lastFrameTiming), 100);
                    commitAvionicsData();
                }

                // SET CONTACT POINT
                if (_planeCommit.VelocityBodyZ > 20 && _carrierPosition.alt == 0 && _planeAvionicsResponse.TailhookPosition == 100 &&
                    (_planeAvionicsResponse.SimOnGround == 100 || _planeInfoResponse.AltitudeAboveGround <= _planeAvionicsResponse.StaticCGtoGround * 1.25))
                {
                    _carrierPosition.alt = _planeInfoResponse.Altitude - _planeAvionicsResponse.StaticCGtoGround;
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

                    double accel = _planeCommit.VelocityBodyZ - _planeInfoResponseLast.VelocityBodyZ;

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
                        _planeAvionicsResponse.TailhookPosition = 50;
                        commitAvionicsData();
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

                thermalsClearButton.Content = thermalsList.Count + thermalsListAPI.Count > 0 ? "Remove " + (thermalsList.Count + thermalsListAPI.Count).ToString() + " thermals" : "No thermals loaded";
            }
        }

        private void thermalsClear(object sender, RoutedEventArgs e)
        {
            thermalsList = new List<winchPosition>();
            Application.Current.Dispatcher.Invoke(() => _radarClass.clearRadarThermals(RadarCanvas, "Thermal"));
            thermalsListAPI = new List<winchPosition>();
            Application.Current.Dispatcher.Invoke(() => _radarClass.clearRadarThermals(RadarCanvas, "ThermalAPI"));
            Application.Current.Dispatcher.Invoke(() => thermalsClearButton.Content = "No thermals loaded");
        }

        private void enableApiThermals(Button btn, bool enable, bool force = false)
        {

            Application.Current.Dispatcher.Invoke(() => changeButtonStatus(!enable, btn, true, enable ? "Disable API thermals" : "Enable API thermals"));

            if (!enable)
            {
                if (force || (!assistantSettings.ContainsKey("APIthermalsAutoload") || assistantSettings["APIthermalsAutoload"] == 0))
                {
                    Console.WriteLine("dsiableApiThermals");
                    thermalsListAPI = new List<winchPosition>();
                    apiThermalsLoadedPosition = null;
                    apiThermalsLoadedTime = 0;
                    Application.Current.Dispatcher.Invoke(() => thermalsClearButton.Content = thermalsList.Count + thermalsListAPI.Count > 0 ? "Remove " + (thermalsList.Count + thermalsListAPI.Count).ToString() + " thermals" : "No thermals loaded");

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
            if ((assistantSettings["APIthermalsAutoload"] == 1) && _planeInfoResponse.Latitude != 0 && _planeInfoResponse.Longitude != 0)
            {
                thermalsListAPI = new List<winchPosition>();
                apiThermalsLoadedPosition = new GeoLocation(_planeInfoResponse.Latitude, _planeInfoResponse.Longitude);
                apiThermalsLoadedTime = _planeInfoResponse.AbsoluteTime;

                // S W N E
                double[] bounds = getThermalsBounds(new GeoLocation(_planeInfoResponse.Latitude, _planeInfoResponse.Longitude), true);

                if (bounds[0] != 0 && bounds[1] != 0 && bounds[2] != 0 && bounds[3] != 0 && bounds[0] != bounds[2] && bounds[1] != bounds[3])
                {
                    if (assistantSettings.ContainsKey("APIthermalsAutoload") && assistantSettings["APIthermalsAutoload"] == 1)
                    {
                        string url = "https://thermal.kk7.ch/api/hotspots/csv/all/" + bounds[0].ToString().Replace(',', '.') + "," + bounds[1].ToString().Replace(',', '.') + "," + bounds[2].ToString().Replace(',', '.') + "," + bounds[3].ToString().Replace(',', '.');
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

                    if (assistantSettings.ContainsKey("APIthermalsAutoload") && assistantSettings["APIthermalsAutoload"] == 1 && Directory.Exists("openaip"))
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
                    Application.Current.Dispatcher.Invoke(() => _radarClass.insertRadarThermals(RadarCanvas, thermalsListAPI, "ThermalAPI"));
                    showMessage(thermalsListAPI.Count + " hotspots loaded from API sources", _fsConnect);
                    Application.Current.Dispatcher.Invoke(() => APICsvExportButton.Visibility = Visibility.Visible);
                }
                else
                {
                    showMessage("No hotspots for this area from API sources", _fsConnect);
                    Application.Current.Dispatcher.Invoke(() => APICsvExportButton.Visibility = Visibility.Collapsed);
                }
            }

            Application.Current.Dispatcher.Invoke(() => thermalsClearButton.Content = thermalsList.Count + thermalsListAPI.Count > 0 ? "Remove " + (thermalsList.Count + thermalsListAPI.Count).ToString() + " thermals" : "No thermals loaded");
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
                            winchPosition _thermalPosition = new winchPosition(new GeoLocation(lat / 180 * Math.PI, lng / 180 * Math.PI), 0.305 * alt, 1852 * radius, strength / 1.9 * 5);
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
                        double strength = 20; // KNOTS

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
        }
        private void APICsvExport(object sender, RoutedEventArgs e)
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
                    Application.Current.Dispatcher.Invoke(() => thermalsClearButton.Content = "No thermal maps loaded");
                }

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
                double height = 0;
                double acAltitude = 0;
                double thermalRadius = thermal.radius;
                double finalModifier = 0.1;

                // DRIFT
                if (assistantSettings.ContainsKey("thermalsType") && assistantSettings["thermalsType"] >= 2)
                {
                    thermal.location = _mathClass.FindPointAtDistanceFrom(thermal.location, windDirection - Math.PI, windVelocity / 1000 / 4 * lastFrameTiming);
                }

                GeoLocation thermalCenter = new GeoLocation(thermal.location.Latitude, thermal.location.Longitude);

                // AGL
                acAltitude = _planeInfoResponse.AltitudeAboveGround;
                if (thermal.alt >= 1000)
                {
                    height = thermal.alt;
                }
                // MSL
                else
                {
                    height = Math.Max(1000, assistantSettings["thermalsHeight"] * 0.305) + thermal.alt - (_planeInfoResponse.Altitude - _planeInfoResponse.AltitudeAboveGround);
                }

                thermalRadius *= 1 + windModifier;

                if (thermalRadius > 0)
                {
                    // LEANING
                    if (thermalLeaning > 10)
                    {
                        thermalCenter = _mathClass.FindPointAtDistanceFrom(thermalCenter, windDirection - Math.PI, thermalLeaning / 1000);
                    }

                    winchPosition thermalTemp = new winchPosition(thermalCenter, height, thermalRadius);
                    thermalTemp.alt = _planeInfoResponse.Altitude - 10 * thermalTemp.radius;
                    winchDirection _thermalDirection = _mathClass.getForceDirection(thermalTemp, _planeInfoResponse);

                    if (_thermalDirection.groundDistance < thermalTemp.radius)
                    {
                        //Console.WriteLine("Thermal leaning: " + thermalLeaning.ToString("0.0") + " width scale: " + (1 + windModifier));

                        // DISTANCE TO THE CENTER
                        double horizontalModifier = thermalTemp.radius - _thermalDirection.groundDistance < 100 ?
                            (thermalTemp.radius - _thermalDirection.groundDistance) / 100 : 1;

                        // DISTANCE TO THE TOP
                        double verticalModifier = acAltitude < height ?
                            (height - acAltitude < 500 ? (height - acAltitude) / 500 : 1) : // UNDER INVERSION
                            Math.Max(-1, (height - acAltitude) / 500); // ABOVE INVERSION

                        if (verticalModifier > 0)
                        {
                            // ATTITUDE
                            double pitchBankModifier = Math.Abs(Math.Cos(_planeInfoResponse.PlaneBank)) * Math.Abs(Math.Cos(_planeInfoResponse.PlanePitch)); // ROTATION

                            // AIRSPEED
                            double airspeedModifier = _planeInfoResponse.AirspeedIndicated < 150 ? Math.Pow(Math.Max(0, 1 - _planeInfoResponse.AirspeedIndicated / 150), 0.5) : 0; // OVERSPEED
                            airspeedModifier *= _planeInfoResponse.AirspeedIndicated > 15 ? Math.Pow(Math.Max(0, (_planeInfoResponse.AirspeedIndicated - 15) / 135), 0.5) : 0; // STALL
                            airspeedModifier = Math.Min(1, 3 * airspeedModifier); // POW COMPENSATION

                            double ambientModifier = (1 - windModifier) * overcastModifier * dayTimeModifier;

                            finalModifier = horizontalModifier * verticalModifier * ambientModifier;

                            // WIND INDICATION
                            thermalFlow += thermal.airspeed * finalModifier;

                            finalModifier *= pitchBankModifier * airspeedModifier;
                            double liftAmount = thermal.airspeed * finalModifier + Math.Abs(_planeInfoResponse.AmbientWindY);

                            // COMPARE VERTICAL VELOCITY AND UPLIFT
                            if (finalModifier > 0 && _planeInfoResponse.VerticalSpeed < liftAmount)
                            {
                                //liftAmount = Math.Min(liftAmount, Math.Pow(liftAmount - _planeInfoResponse.VerticalSpeed, 2));
                                liftAmount = liftAmount - Math.Max(0, _planeInfoResponse.VerticalSpeed);
                            }
                            else
                            {
                                liftAmount /= 2.0;
                            }

                            Console.WriteLine("LiftY: " + (-_thermalDirection.localForceDirection.Y * liftAmount) + " thermalFlow: " + thermalFlow + " VSpeed: " + _planeInfoResponse.VerticalSpeed);

                            if (liftAmount != 0)
                            {


                                double oldMagnitude = Math.Pow(Math.Pow(_planeCommit.VelocityBodyX, 2) + Math.Pow(_planeCommit.VelocityBodyY, 2) + Math.Pow(_planeCommit.VelocityBodyZ, 2), 0.5);
                                _planeCommit.VelocityBodyY -= _thermalDirection.localForceDirection.Y * liftAmount * lastFrameTiming;

                                // FORWARD SPEED COMPENSATION
                                if (_planeCommit.VelocityBodyZ > 10 && _planeCommit.VelocityBodyZ < 50)
                                {
                                    _planeCommit.VelocityBodyZ += Math.Max(0, Math.Min(liftAmount, (40 - 0.8 * _planeCommit.VelocityBodyZ) / 40))
                                        * Math.Cos(Math.Min(0, _planeInfoResponse.PlanePitch))
                                        * lastFrameTiming;
                                    /*double newMagnitude = Math.Pow(Math.Pow(_planeCommit.VelocityBodyX, 2) + Math.Pow(_planeCommit.VelocityBodyY, 2) + Math.Pow(_planeCommit.VelocityBodyZ, 2), 0.5);
                                    double mod = (newMagnitude / oldMagnitude - 1) * 2 + 1;
                                    Console.WriteLine(mod);
                                    if (mod > 1)
                                    {
                                        _planeCommit.VelocityBodyX *= mod;
                                        _planeCommit.VelocityBodyZ *= mod;
                                    }*/
                                }

                                if (!double.IsNaN(_planeCommit.VelocityBodyX) && !double.IsNaN(_planeCommit.VelocityBodyY) && !double.IsNaN(_planeCommit.VelocityBodyZ))
                                {
                                    try
                                    {
                                        _fsConnect.UpdateData(Definitions.PlaneCommit, _planeCommit);


                                        /*string bar = "||||||||||||||||||||||||||||||||||||||||||||||||||";
                                        Application.Current.Dispatcher.Invoke(() => thermalsDebugData.Height = 90);
                                        Application.Current.Dispatcher.Invoke(() => messagesLogScroll.Height = 0);
                                        Application.Current.Dispatcher.Invoke(() => thermalsDebugData.Children.Add(makeTextBlock("To center:  " + bar.Substring(0, (int)(horizontalModifier * 100 / 4)), Colors.Black, 12)));
                                        Application.Current.Dispatcher.Invoke(() => thermalsDebugData.Children.Add(makeTextBlock("To top:     " + bar.Substring(0, (int)(verticalModifier * 100 / 4)), (height - acAltitude) > 0 ? Colors.Black : Colors.DarkRed, 12)));
                                        Application.Current.Dispatcher.Invoke(() => thermalsDebugData.Children.Add(makeTextBlock("Pitch/bank: " + bar.Substring(0, (int)(pitchBankModifier * 100 / 4)), Colors.Black, 12)));
                                        Application.Current.Dispatcher.Invoke(() => thermalsDebugData.Children.Add(makeTextBlock("Velocity:   " + bar.Substring(0, (int)(airspeedModifier * 100 / 4)), Colors.Black, 12)));
                                        Application.Current.Dispatcher.Invoke(() => thermalsDebugData.Children.Add(makeTextBlock("Ambient:    " + bar.Substring(0, (int)(ambientModifier * 100 / 4)) + Environment.NewLine + Environment.NewLine, ambientModifier > 0 ? Colors.Black : Colors.DarkRed, 12)));
                                        Application.Current.Dispatcher.Invoke(() => thermalsDebugData.Children.Add(makeTextBlock("Total lift: " + bar.Substring(0, (int)(finalModifier * 100 / 4)) + Environment.NewLine + Environment.NewLine, finalModifier > 0 ? Colors.Black : Colors.DarkRed, 12)));*/
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine(ex.Message);
                                    }

                                }

                                /*try
                                {
                                    PlaneInfoLift planeInfoLift = new PlaneInfoLift();
                                    planeInfoLift.Altitude = _planeInfoResponse.Altitude + liftAmount * lastFrameTiming;
                                    _fsConnect.UpdateData(Definitions.PlaneLift, planeInfoLift);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(ex.Message);
                                }*/
                            }
                        }
                        else
                        {
                            finalModifier = verticalModifier;
                        }
                    }

                    if ((thermalsDebugActive > 0 || httpServerActive) && (!assistantSettings.ContainsKey("RadarScale") || scaleRefresh > 0 || _thermalDirection.groundDistance / 1.25 < Math.Min(allowedRadarScale, assistantSettings["RadarScale"]) * 1000 + thermalTemp.radius))
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
                    // PLANE INFO
                    if (e.RequestId == (uint)Requests.PlaneInfo)
                    {
                        //Console.WriteLine(JsonConvert.SerializeObject(e.Data, Formatting.Indented));
                        _planeInfoResponseLast = _planeInfoResponse;
                        _planeInfoResponse = (PlaneInfoResponse)(e.Data);

                        swCurrent = _planeInfoResponse.AbsoluteTime;

                        // PAUSE?
                        if (_planeAvionicsResponse.SimOnGround != 100 &&
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
                            lastFrameTiming = swCurrent - swLast;
                        }

                        absoluteTime += lastFrameTiming;

                        _planeRotate = new PlaneInfoRotate();
                        _planeRotate.RotationVelocityBodyX = _planeInfoResponse.RotationVelocityBodyX;
                        _planeRotate.RotationVelocityBodyY = _planeInfoResponse.RotationVelocityBodyY;
                        _planeRotate.RotationVelocityBodyZ = _planeInfoResponse.RotationVelocityBodyZ;

                        _planeRotateAccel = new PlaneInfoRotateAccel();
                        _planeRotateAccel.RotationAccelerationBodyX = _planeInfoResponse.RotationAccelerationBodyX;
                        _planeRotateAccel.RotationAccelerationBodyY = _planeInfoResponse.RotationAccelerationBodyY;
                        _planeRotateAccel.RotationAccelerationBodyZ = _planeInfoResponse.RotationAccelerationBodyZ;

                        _planeCommit = new PlaneInfoCommit();
                        _planeCommit.VelocityBodyX = _planeInfoResponse.VelocityBodyX;
                        _planeCommit.VelocityBodyY = _planeInfoResponse.VelocityBodyY;
                        _planeCommit.VelocityBodyZ = _planeInfoResponse.VelocityBodyZ;

                        Application.Current.Dispatcher.Invoke(() => VerticalWindPos.Height = 125 - Math.Min(Math.Max(0, (_planeInfoResponse.AmbientWindY + thermalFlow) * 1.94) * 6.25, 125));
                        Application.Current.Dispatcher.Invoke(() => VerticalWindNeg.Height = 125 - Math.Min(Math.Abs(Math.Min(0, (_planeInfoResponse.AmbientWindY + thermalFlow) * 1.94) * 6.25), 125));

                        // UPDATE KK7 DATA
                        if (assistantSettings.ContainsKey("APIthermalsAutoload") && assistantSettings["APIthermalsAutoload"] == 1 &&
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

                        if (thermalsDebugActive > 0 || httpServerActive)
                            Application.Current.Dispatcher.Invoke(() => _radarClass.updateCompassWind(RadarCanvas, _planeInfoResponse.PlaneHeading, _weatherReport.AmbientWindDirection, _weatherReport.AmbientWindVelocity * 1.94384));

                        // PAST COMMIT
                        if (lastPacketReceived != 0 /*1000 * lastFrameTiming > interval * 0.75*/ && lastFrameTiming > 0.001 && lastFrameTiming <= 1.0 && _planeAvionicsResponse.IsSlewActive != 100)
                        {
                            // TRACK RECORDING
                            if (_trackingClass.trackRecording != null && _trackingClass.recordingCounter > 0)
                            {
                                // FREE VERSION
                                if (allowedRecordLength < _trackingClass.recordingCounter)
                                {
                                    Application.Current.Dispatcher.Invoke(() => setTrackRecording(false));
                                }
                                else
                                {
                                    _trackingClass.captureTrackPoint(_planeInfoResponse, _planeAvionicsResponse, absoluteTime, 0.5);
                                    _trackingClass.recordingCounter += Math.Max(0, lastFrameTiming);
                                }
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
                            thermalFlow = 0;
                            if (thermalsWorking)
                            {
                                //Application.Current.Dispatcher.Invoke(() => thermalsDebugData.Children.Clear());
                                //Application.Current.Dispatcher.Invoke(() => thermalsDebugData.Height = 0);
                                //Application.Current.Dispatcher.Invoke(() => messagesLogScroll.Height = 90);

                                if (_planeInfoResponse.AirspeedIndicated < 100)
                                {
                                    processThermals(thermalsList);
                                    processThermals(thermalsListAPI, true);
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("Frame skip, lastFrameTiming: " + lastFrameTiming);

                            if (lastFrameTiming > 0 && lastFrameTiming < 50 && _trackingClass.recordingCounter > 0)
                            {
                                _trackingClass.recordingCounter += Math.Max(0, lastFrameTiming);
                            }
                        }

                        swLast = swCurrent;
                        lastPacketReceived = 0;
                    }
                    // PLANE AVIONICS
                    else if (e.RequestId == (uint)Requests.PlaneAvionics)
                    {
                        //Console.WriteLine(JsonConvert.SerializeObject(e.Data, Formatting.Indented));
                        _planeAvionicsResponseLast = _planeAvionicsResponse;
                        _planeAvionicsResponse = (PlaneAvionicsResponse)(e.Data);

                        trackControlChanges();
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
                        if (thermalsDebugActive > 0 || httpServerActive)
                            Application.Current.Dispatcher.Invoke(() => _radarClass.updateRadarNearby(RadarCanvas, e.ObjectID, _mathClass.getForceDirection(nearbyPosition, _planeInfoResponse), nearbyPosition, towingTarget == e.ObjectID, Math.Min(maxRadarScale, assistantSettings["RadarScale"])));

                        // PLAY RECORDING
                        if (_trackingClass.ghostPlayerActive(e.ObjectID))
                        {
                            GhostCommit towCommit = new GhostCommit();
                            towCommit = _trackingClass.updateGhostPlayer(e.ObjectID, response, towCommit, _fsConnect, _mathClass, absoluteTime);
                            if (_trackingClass.message.Key != 0)
                            {
                                switch (_trackingClass.message.Value)
                                {
                                    case "RELEASE":
                                        if (towingTarget == _trackingClass.message.Key)
                                            attachTowCable(null, null);
                                        break;
                                    case "REMOVE":
                                        Application.Current.Dispatcher.Invoke(() => removeRecordSlider(_trackingClass.message.Key));
                                        break;
                                    default:
                                        showMessage(_trackingClass.message.Value, _fsConnect);
                                        break;
                                }

                                _trackingClass.message = new KeyValuePair<uint, string>();
                            }

                            if (!double.IsNaN(towCommit.RotationVelocityBodyX) && !double.IsNaN(towCommit.RotationVelocityBodyY) && !double.IsNaN(towCommit.RotationVelocityBodyZ) && !double.IsNaN(towCommit.VelocityBodyX) &&
                                !double.IsNaN(towCommit.VelocityBodyZ) && !double.IsNaN(towCommit.VelocityBodyZ))
                            {
                                try
                                {
                                    _fsConnect.UpdateData(Definitions.GhostCommit, towCommit, e.ObjectID);
                                }
                                catch (Exception ex)
                                {
                                    addLogMessage(ex.Message);
                                }
                            }
                        }
                        else if (response.FlightNumber == "9999" || insertedTowPlane.Key == e.ObjectID)
                        {
                            bool AIhold = insertTowPlanePressed != 0 && _planeInfoResponse.AbsoluteTime - insertTowPlanePressed < AIholdInterval;
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
                                towCommit.Pitch = response.Pitch;
                                towCommit.VelocityBodyY = response.Verticalspeed * Math.Cos(response.Bank);
                                towCommit.VelocityBodyZ = response.Airspeed;

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
                            case 8: // SNOW
                                overcastModifier = 0.5 * overcastModifier + 0.1 * (1 + 4 * Math.Max(0, 1 - _weatherReport.AmbientPrecipRate / 100));
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
                                dayTimeModifier = 0.95 * dayTimeModifier + 0.025;
                                break;
                            case 1: // DAY
                                dayTimeModifier = 0.9 * dayTimeModifier + 0.1;
                                break;
                            case 2: // DUSK
                            case 3: // NIGHT
                                dayTimeModifier = 0.95 * dayTimeModifier + 0.01;
                                break;
                        }

                        Console.WriteLine($"windDirection: {windDirection:F2} windVelocity: {windVelocity:F2} dayTimeModifier: {dayTimeModifier:F2} overcastModifier: {overcastModifier:F}");
                    }
                    else
                    {
                        Console.WriteLine("Unknown request ID " + (uint)e.RequestId + " received (type " + e.Data.GetType() + ")");
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

                        GhostPlane gp = _trackingClass.tryCaptureGhostPlane((uint)e.Data, absoluteTime);
                        if (gp.TrackPoints != null && gp.TrackPoints.Count > 0)
                        {
                            //_fsConnect.AIReleaseControl(insertedTowPlane.Key, Requests.TowPlane);
                            Application.Current.Dispatcher.Invoke(() => addRecordSlider(gp));
                            teleportGhostPlane(gp);
                        }

                        if (towScanMode == TowScanMode.TowSearch && insertedTowPlane.Key == TARGETMAX)
                        {
                            insertedTowPlane = new KeyValuePair<uint, bool>((uint)e.Data, false);
                            //towScanMode = TowScanMode.Scan;
                            Console.WriteLine("Tow plane number " + insertedTowPlane);

                            if (towingTarget == TARGETMAX && towScanMode >= TowScanMode.TowSearch)
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
            if (_planeAvionicsResponse.TotalWeight != 0)
            {
                foreach (var field in typeof(PlaneAvionicsResponse).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    if (controlTimestamps.ContainsKey(field.Name) && field.FieldType == typeof(double) && field.Name.StartsWith("LIGHT") &&
                        (double)field.GetValue(_planeAvionicsResponse) != (double)field.GetValue(_planeAvionicsResponseLast))
                    {
                        if (absoluteTime - controlTimestamps[field.Name] < 1.0)
                        {
                            Console.WriteLine(field.Name + " toggle detected");

                            // FIND FUNCTION TO TOGGLE
                            Application.Current.Dispatcher.Invoke(() => runControlFunction(field.Name));

                            controlTimestamps[field.Name] = 0;
                        }
                        else
                        {
                            controlTimestamps[field.Name] = absoluteTime;
                        }
                    }

                // CAPTURE WINCH/TOW TRIGGER
                if (_planeAvionicsResponse.WaterRudderHandlePosition != _planeAvionicsResponseLast.WaterRudderHandlePosition)
                {
                    if (_planeAvionicsResponse.WaterRudderHandlePosition < 50)
                    {
                        if (_winchPosition != null) // WINCH RELEASE
                        {
                            Console.WriteLine("Winch release detected");
                            Application.Current.Dispatcher.Invoke(() => toggleLaunchPrepare(null, null));
                        }

                        if (towingTarget != TARGETMAX) // TOW RELEASE
                        {
                            Console.WriteLine("Tow release detected");
                            Application.Current.Dispatcher.Invoke(() => toggleTowCable(towingTarget));
                        }
                    }

                    if (_planeAvionicsResponse.WaterRudderHandlePosition == 50) // WINCH CONNECT
                    {
                        Console.WriteLine("Winch connect detected");
                        Application.Current.Dispatcher.Invoke(() => toggleLaunchPrepare(null, null));
                    }
                    else if (_planeAvionicsResponse.WaterRudderHandlePosition == 100) // TOW INSERT
                    {
                        Console.WriteLine("Tow connect detected");
                        Application.Current.Dispatcher.Invoke(() => aiTowPlane(null, null));
                    }
                }
            }
        }

        private void runControlFunction(string fieldName)
        {
            foreach (ComboBox tb in FindLogicalChildren<ComboBox>(window))
            {
                if (tb.Tag != null)
                {
                    try
                    {
                        if (tb.SelectedItem != null && tb.SelectedItem.ToString().Replace(" ", "") == fieldName)
                        {
                            Console.WriteLine("Triggering function " + tb.Tag.ToString());
                            Type type = Application.Current.MainWindow.GetType();
                            MethodInfo method = type.GetMethod(tb.Tag.ToString(), BindingFlags.NonPublic | BindingFlags.Instance);
                            if (method != null)
                            {
                                method.Invoke(Application.Current.MainWindow, new object[] { null, null });
                            }
                            else
                            {
                                addLogMessage("Function " + tb.Tag.ToString() + " can't be triggered");
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        private void InitializeDataDefinitions(FsConnect fsConnect)
        {
            Console.WriteLine("InitializeDataDefinitions");

            // PLANE INFO
            List<SimProperty> definition = new List<SimProperty>();
            definition.Add(new SimProperty(FsSimVar.PlaneLatitude, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.PlaneLongitude, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.PlaneAltitudeAboveGround, FsUnit.Meter, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.PlaneAltitude, FsUnit.Meter, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.AbsoluteTime, FsUnit.Second, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.PlaneHeading, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.PlanePitch, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.PlaneBank, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.GpsGroundSpeed, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.AirspeedIndicated, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.Verticalspeed, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.AmbientWindY, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.VelocityBodyX, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.VelocityBodyY, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.VelocityBodyZ, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.RotationVelocityBodyX, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.RotationVelocityBodyY, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            definition.Add(new SimProperty(FsSimVar.RotationVelocityBodyZ, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));

            // VELOCITY COMMIT
            fsConnect.RegisterDataDefinition<PlaneInfoResponse>(Definitions.PlaneInfo, definition);

            List<SimProperty> cDefinition = new List<SimProperty>();
            cDefinition.Add(new SimProperty(FsSimVar.VelocityBodyX, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.VelocityBodyY, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.VelocityBodyZ, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));

            fsConnect.RegisterDataDefinition<PlaneInfoCommit>(Definitions.PlaneCommit, cDefinition);

            // AVIONICS
            List<SimProperty> aDefinition = new List<SimProperty>();
            aDefinition.Add(new SimProperty(FsSimVar.Title, FsUnit.None, SIMCONNECT_DATATYPE.STRING256));
            aDefinition.Add(new SimProperty(FsSimVar.Type, FsUnit.None, SIMCONNECT_DATATYPE.STRING256));
            aDefinition.Add(new SimProperty(FsSimVar.Model, FsUnit.None, SIMCONNECT_DATATYPE.STRING256));
            aDefinition.Add(new SimProperty(FsSimVar.StaticCGtoGround, FsUnit.Meter, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.SimOnGround, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.BrakeParkingPosition, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.OnAnyRunway, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.IsSlewActive, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.TotalWeight, FsUnit.Pounds, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.SimRate, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.TailhookPosition, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.LaunchbarPosition, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.WaterRudderHandlePosition, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.Smoke, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.YokeYPosition, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.YokeXPosition, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.RudderPosition, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));

            aDefinition.Add(new SimProperty(FsSimVar.LIGHTPANEL, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.LIGHTSTROBE, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.LIGHTLANDING, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.LIGHTTAXI, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.LIGHTBEACON, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.LIGHTNAV, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.LIGHTLOGO, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.LIGHTWING, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.LIGHTRECOGNITION, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.LIGHTCABIN, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.LIGHTGLARESHIELD, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.LIGHTPEDESTRAL, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            aDefinition.Add(new SimProperty(FsSimVar.LIGHTPOTENTIOMETER, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));

            fsConnect.RegisterDataDefinition<PlaneAvionicsResponse>(Definitions.PlaneAvionics, aDefinition);

            List<SimProperty> lDefinition = new List<SimProperty>();
            lDefinition.Add(new SimProperty(FsSimVar.PlaneAltitude, FsUnit.Meter, SIMCONNECT_DATATYPE.FLOAT64));

            fsConnect.RegisterDataDefinition<PlaneInfoLift>(Definitions.PlaneLift, lDefinition);

            List<SimProperty> rDefinition = new List<SimProperty>();
            rDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyX, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            rDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyY, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            rDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyZ, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));

            fsConnect.RegisterDataDefinition<PlaneInfoRotate>(Definitions.PlaneRotate, rDefinition);

            List<SimProperty> raDefinition = new List<SimProperty>();
            raDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyX, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            raDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyY, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            raDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyZ, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));

            fsConnect.RegisterDataDefinition<PlaneInfoRotateAccel>(Definitions.PlaneRotateAccel, raDefinition);

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
            nDefinition.Add(new SimProperty(FsSimVar.PlanePitch, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
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

            List<SimProperty> gDefinition = new List<SimProperty>();
            gDefinition.Add(new SimProperty(FsSimVar.VelocityBodyX, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            gDefinition.Add(new SimProperty(FsSimVar.VelocityBodyY, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            gDefinition.Add(new SimProperty(FsSimVar.VelocityBodyZ, FsUnit.MeterPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            gDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyX, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            gDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyY, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            gDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyZ, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));

            fsConnect.RegisterDataDefinition<GhostCommit>(Definitions.GhostCommit, gDefinition);

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
            wDefinition.Add(new SimProperty(FsSimVar.AmbientPrecipRate, FsUnit.Meter, SIMCONNECT_DATATYPE.FLOAT64));
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

        public void commitAvionicsData()
        {
            try
            {
                if (!double.IsNaN(_planeAvionicsResponse.WaterRudderHandlePosition) && !double.IsNaN(_planeAvionicsResponse.TotalWeight) && !double.IsNaN(_planeAvionicsResponse.Smoke) &&
                    !double.IsNaN(_planeAvionicsResponse.SimRate) && !double.IsNaN(_planeAvionicsResponse.BrakeParkingPosition) && !double.IsNaN(_planeAvionicsResponse.IsSlewActive) &&
                    !double.IsNaN(_planeAvionicsResponse.LaunchbarPosition) && !double.IsNaN(_planeAvionicsResponse.OnAnyRunway) && !double.IsNaN(_planeAvionicsResponse.SimOnGround))
                    _fsConnect.UpdateData(Definitions.PlaneAvionics, _planeAvionicsResponse);
            }
            catch (Exception ex)
            {
                addLogMessage(ex.Message);
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

                    enableApiThermals(APIthermalsButton, assistantSettings.ContainsKey("APIthermalsAutoload") && assistantSettings["APIthermalsAutoload"] == 1);
                    toggleSidebarWindow(assistantSettings.ContainsKey("sidebarActive") && assistantSettings["sidebarActive"] == 1);
                    
                    TowPlaneTrackContainer.Visibility = assistantSettings.ContainsKey("towType") && assistantSettings["towType"] == 1 ? Visibility.Visible : Visibility.Collapsed;
                    towInsertContainer.Visibility = assistantSettings.ContainsKey("towType") && assistantSettings["towType"] == 1 ? Visibility.Visible : Visibility.Collapsed;

                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }

            // LOAD TOW PLANE RECORDS
            if (Directory.Exists(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\TOWPLANES\\"))
            {
                int counter = 0;
                var files = Directory.EnumerateFiles("TOWPLANES", "*.gpx", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    ComboBoxItem item = new ComboBoxItem();

                    if (counter < allowedRecords)
                    {
                        item.Content = Path.GetFileNameWithoutExtension(file);
                        item.Tag = file;
                    }
                    else
                    {
                        item.Content = "KineticAssistant+";
                        item.Tag = "";
                        item.IsEnabled = false;
                    }

                    towPlaneTrack.Items.Add(item);

                    counter++;
                }

                if (towPlaneTrack.Items.Count > 0)
                    towPlaneTrack.SelectedIndex = 0;
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
                        else if (((CheckBox)sender).Name == "APIthermalsAutoload")
                        {
                            enableApiThermals(APIthermalsButton, ((CheckBox)sender).IsChecked == true);

                            if (((CheckBox)sender).IsChecked != true)
                            {
                                APICsvExportButton.Visibility = Visibility.Collapsed;
                            }
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
                        if (((ComboBox)sender).Name == "panelServer")
                        {
                            if (((ComboBox)sender).SelectedIndex != 0 && !httpServerActive)
                            {
                                startHTTP();
                            }
                        }
                        else if (((ComboBox)sender).Name == "aircraftType" || ((ComboBox)sender).Name == "preferredServer" || ((ComboBox)sender).Name == "showPrivate")
                        {
                            //loadEventsList(null, null);
                        }
                        else if (((ComboBox)sender).Name == "towType")
                        {
                            TowPlaneTrackContainer.Visibility = Visibility.Collapsed;
                            towInsertContainer.Visibility = Visibility.Collapsed;
                            pushPicture.Visibility = Visibility.Collapsed;

                            switch (((ComboBox)sender).SelectedIndex)
                            {
                                case 0: // PLAYER
                                    break;
                                case 1: // TRACK
                                    TowPlaneTrackContainer.Visibility = Visibility.Visible;
                                    towInsertContainer.Visibility = Visibility.Visible;
                                    break;
                                case 2: // PUSH
                                    pushPicture.Visibility = Visibility.Visible;
                                    break;
                                case 3: // GROUND
                                    break;
                                case 4: // BOAT
                                    break;
                                case 5: // HELI
                                    break;
                            }
                        }
                    }
                    else if (sender != null && sender.GetType() == typeof(Slider))
                    {
                        if (((Slider)sender).Name == "RequestsFrequency")
                        {
                            launchTimer.Interval = new TimeSpan(0, 0, 0, 0, 1000 / Math.Max(10, (int)assistantSettings["RequestsFrequency"]));
                            nearbyTimer.Interval = new TimeSpan(0, 0, 0, 0, 20000 / Math.Max(10, (int)(assistantSettings.ContainsKey("RequestsFrequency") ? assistantSettings["RequestsFrequency"] : 10)));
                        }
                        else if (((Slider)sender).Name == "RadarScale")
                        {
                            if (allowedRadarScale < 50)
                                _radarClass.updateRadarCover(RadarCanvas, assistantSettings["RadarScale"] / maxRadarScale);
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
            string link = e.Uri.ToString().Replace(@"file:///", "");
            if (e.Uri.ToString().Contains("msfs.touching.cloud"))
            {
                resetMissedUpdates(null, null);
            }

            System.Diagnostics.Process.Start(e.Uri.ToString().Contains("//") ? e.Uri.AbsoluteUri : e.Uri.ToString());
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
            Console.WriteLine(text);
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
            APIthermalsAutoload.IsChecked = !APIthermalsAutoload.IsChecked;
            saveSettings(APIthermalsAutoload, null);
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
            if (!validConnection() || MessageBox.Show("Connection currently is active. You sure to close application?", "Warning", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                if (validConnection())
                {
                    toggleConnect(null, null);
                }

                ServerStop();

                mutex.ReleaseMutex();
                Application.Current.Shutdown();
            }
        }

        private Image getIconImage(bool state, string names)
        {
            Image img = new Image();
            img.Source = new BitmapImage(new Uri("media/" + names.Split('|')[state ? 1 : 0], UriKind.Relative));

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
            try
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
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
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

        // TRACKING START
        public void StartRecord(object sender, EventArgs e)
        {
            if (_trackingClass.trackRecording == null)
            {
                setTrackRecording(true);
            }
            else
            {
                setTrackRecording(false);
                _trackingClass.trackRecording = null;
            }

        }

        public void setTrackRecording(bool enable, bool save = true)
        {
            if (validConnection())
            {
                if (enable)
                {
                    Application.Current.Dispatcher.Invoke(() => changeButtonStatus(false, StartRecordButton));
                    _trackingClass.lastTrackCapture = 0;
                    _trackingClass.trackRecording = new List<TrackPoint>();
                    _trackingClass.lastTrackCapture = 0;
                    _trackingClass.recordingCounter = 0.0001;

                    if (save)
                        showMessage("Track recording started", _fsConnect);
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(() => changeButtonStatus(true, StartRecordButton));

                    if (save)
                    {
                        zipDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + @"\RECORDINGS\";
                        if (!Directory.Exists(zipDirectory))
                        {
                            try
                            {
                                Directory.CreateDirectory(zipDirectory);
                            }
                            catch
                            {
                                MessageBox.Show("Can't create recordings directory");
                                return;
                            }
                        }
                        string filename = _planeAvionicsResponse.Title + " - " + DateTime.UtcNow.ToString("s").Replace(":", "-") + ".gpx";
                        KeyValuePair<double, string> trackContentAligned = _trackingClass.buildTrackFile(AppName.Text, "", _planeInfoResponse, _planeAvionicsResponse, _mathClass, "recorded_track_" + filename, true);
                        _trackingClass.saveTrackfile(trackContentAligned.Value, zipDirectory, "recorded_track_" + filename);

                        showMessage("Recorded track saved, length: " + _trackingClass.recordingCounter.ToString("0") + " seconds", _fsConnect);
                    }

                    //_trackingClass.trackRecording = null;
                    _trackingClass.recordingCounter = 0;

                }
            }
        }

        public void LoadRecord(object sender, EventArgs e)
        {
            if (validConnection())
            {
                beforeLoadTrackRecord(0);

                OpenFileDialog sfd = new OpenFileDialog();
                sfd.Multiselect = true;

                sfd.Filter = "Flight track (*.gpx)|*.gpx";
                sfd.FilterIndex = 1;

                if (sfd.ShowDialog() == true)
                {
                    int i = 0;
                    foreach (String file in sfd.FileNames)
                    {
                        if (File.Exists(file))
                        {
                            Task.Delay(i * 500).ContinueWith(t => loadTrackRecord(file, true));
                        }
                        i++;
                    }
                }
                else
                {
                    addLogMessage("File not found");
                }
            }
        }

        public void beforeLoadTrackRecord(int mode)
        {
            if (mode == 1)
            {
                towType.SelectedIndex = 1;
                if (RadarScale.Value < 0.2)
                {
                    RadarScale.Value = 0.2;
                    assistantSettings["RadarScale"] = RadarScale.Value;
                }
                saveSettings(towType, null);
            }
            else if (assistantSettings["RadarScale"] < 5)
            {
                RadarScale.Value = 5;
                assistantSettings["RadarScale"] = 5;
                saveSettings(RadarScale, null);
            }

            _trackingClass.ghostTeleport = new winchPosition();
            if (mode == 1 || mode == 0 && assistantSettings.ContainsKey("ghostTeleport") && assistantSettings["ghostTeleport"] != 0)
            {
                _trackingClass.ghostTeleport.radius = _planeInfoResponse.PlaneHeading;
                _trackingClass.ghostTeleport.alt = _planeInfoResponse.Altitude;
                _trackingClass.ghostTeleport.location = _mathClass.FindPointAtDistanceFrom(new GeoLocation(_planeInfoResponse.Latitude, _planeInfoResponse.Longitude), _planeInfoResponse.PlaneHeading, 0.075);
            }
        }

        public void loadTrackRecord(string path, bool start = true)
        {
            GhostPlane gp = _trackingClass.parseTrackFile(path, _mathClass, allowedRecordLength);
            if (gp.TrackPoints.Count > 0)
            {
                if (start)
                {
                    double distance = _mathClass.findDistanceBetweenPoints(_planeInfoResponse.Latitude, _planeInfoResponse.Longitude, gp.TrackPoints[0].Location.Latitude, gp.TrackPoints[0].Location.Longitude);
                    if (distance < 5000)
                    {
                        SIMCONNECT_DATA_INITPOSITION position = new SIMCONNECT_DATA_INITPOSITION();
                        position.Altitude = gp.TrackPoints[0].Elevation;
                        position.Latitude = gp.TrackPoints[0].Location.Latitude * 180 / Math.PI;
                        position.Longitude = gp.TrackPoints[0].Location.Longitude * 180 / Math.PI;
                        //position.Airspeed = 0;
                        position.Heading = gp.TrackPoints[0].Heading * 180 / Math.PI;
                        position.Pitch = gp.TrackPoints[0].Pitch * 180 / Math.PI;
                        position.Bank = gp.TrackPoints[0].Roll * 180 / Math.PI;
                        position.Airspeed = 0;
                        position.OnGround = 0;// (uint)(gp.TrackPoints[0].AltitudeAboveGround < 5 ? 1 : 0);

                        _fsConnect.CreateNonATCAircraft(position, gp.Name, Requests.TowPlane);

                        Console.WriteLine("AI plane inserted altitude: " + position.Altitude);
                    }
                    else
                    {
                        showMessage($"Failed to insert AI plane - you are {distance / 1000:F1}km away from ghost position", _fsConnect);
                    }
                }
            }
            else
            {
                addLogMessage("Failed to parse GPX file");
            }
        }

        public void addRecordSlider(GhostPlane gp)
        {
            StackPanel group = new StackPanel();
            group.Name = "Record_" + gp.ID;
            group.Tag = gp.ID;
            group.Margin = new Thickness(0, 0, 0, 10);

            group.Children.Add(makeTextBlock(gp.Name + "(" + gp.Length.ToString("0") + "s)", Colors.Black, 12, TextWrapping.Wrap));

            Slider slider = new Slider();
            slider.Tag = gp.ID;
            slider.Minimum = 0;
            slider.Maximum = gp.Length;
            slider.TickFrequency = 60;
            slider.AutoToolTipPlacement = System.Windows.Controls.Primitives.AutoToolTipPlacement.TopLeft;
            slider.AutoToolTipPrecision = 0;
            slider.AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler(changeGhostPlaneProgress), true);
            group.Children.Add(slider);

            GhostsList.Children.Add(group);

            if (towPlaneInserted)
            {
                if (PlayRecordsButton.Content.ToString() == "PLAY")
                    PlayRecords(PlayRecordsButton, null);

                //towingTarget = gp.ID;
                towScanMode = TowScanMode.TowInsert;
                insertedTowPlane = new KeyValuePair<uint, bool>(gp.ID, true);
                towPlaneInserted = false;
            }
        }

        public void removeRecordSlider(uint ID)
        {
            _trackingClass.clearRecord(_fsConnect, ID);

            for (int i = GhostsList.Children.Count - 1; i >= 0; i--)
            {
                if (GhostsList.Children[i].GetType() == typeof(StackPanel) && ((StackPanel)GhostsList.Children[i]).Tag.ToString() == ID.ToString())
                {
                    GhostsList.Children.Remove((StackPanel)GhostsList.Children[i]);
                }
            }

            if (GhostsList.Children.Count == 0 && towScanMode != TowScanMode.Disabled)
            {
                toggleScanning(null, null);

                if (PlayRecordsButton.Content.ToString() == "STOP")
                    PlayRecords(PlayRecordsButton, null);
            }
        }
        public void changeGhostPlaneProgress(object sender, MouseButtonEventArgs e)
        {
            Slider slider = (Slider)sender;
            int ID = int.Parse(slider.Tag.ToString());
            Console.WriteLine("Progress update of " + ID + ": " + slider.Value);
            int index = _trackingClass.ghostPlanes.FindIndex(m => m.ID == ID);
            if (index >= 0)
            {
                GhostPlane gp = _trackingClass.ghostPlanes[index];
                gp.Progress = slider.Value;

                _trackingClass.ghostPlanes[index] = gp;
                teleportGhostPlane(_trackingClass.ghostPlanes[index], slider.Value);
            }
        }

        public void stopRecords()
        {
            int index;
            while ((index = _trackingClass.ghostPlanes.FindIndex(m => m.Progress != 0)) >= 0)
            {
                GhostPlane gp = _trackingClass.ghostPlanes[index];
                gp.Progress = 0;
                _trackingClass.ghostPlanes[index] = gp;
                teleportGhostPlane(_trackingClass.ghostPlanes[index], 0);
            }
        }

        public void PlayRecords(object sender, EventArgs e)
        {
            Button btn = (Button)sender;
            if (btn.Content.ToString() == "PLAY")
            {
                changePlayStatus(true);
            }
            else
            {
                changePlayStatus(false);
            }

        }

        public void changePlayStatus(bool newStatus)
        {
            if (newStatus)
            {
                Application.Current.Dispatcher.Invoke(() => changeButtonStatus(false, PlayRecordsButton, true, "STOP"));
                if (towScanMode == TowScanMode.Disabled)
                    toggleScanning(null, null);

                _trackingClass.playRecords(absoluteTime);
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() => changeButtonStatus(true, PlayRecordsButton, true, "PLAY"));
                stopRecords();
            }

        }

        public void ClearRecords(object sender, EventArgs e)
        {
            _trackingClass.clearRecords(_fsConnect);
            Application.Current.Dispatcher.Invoke(() => GhostsList.Children.Clear());
        }
        // TRACKING ENDS

        // HTTP START
        public async void startHTTP()
        {
            await listenHTTP();
            generateRadarBitmap();
        }

        async Task listenHTTP()
        {
            // WEB
            httpServerActive = true;

            ServicePointManager.DefaultConnectionLimit = 500;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.MaxServicePoints = 500;

            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:8212/");
            listener.Start();

            await Task.Run(async () => //This code runs on a new thread, control is returned to the caller on the UI thread.
            {
                while (true)
                {
                    try
                    {
                        var t = listener.GetContextAsync();
                        HttpListenerContext context = t.Result;
                        HttpListenerRequest request = context.Request;
                        // Obtain a response object.
                        HttpListenerResponse response = context.Response;
                        response.Headers.Add("content-type", "image/png");
                        //response.Headers.Add("content-type", "text/html");
                        response.Headers.Add("vary", "Accept-Encoding");
                        response.Headers.Add("Cache-Control", "no-cache");


                        byte[] buffer = await Task.Run(() => GetRadarImage(request.QueryString.Get("zoom")));
                        //string responseString = await Task.Run(() => getRadarString());
                        //byte[] buffer = System.Text.Encoding.UTF8.GetBytes("<!doctype html><html><body><img class=\"radarImage\" src=\"" + responseString + "\"  style=\"width: 100%; height: 100%; object-fit: contain; position: absolute;\" /></body></html>");

                        // Construct a response. 

                        if (buffer != null && buffer.Length > 0)
                        {
                            // Get a response stream and write the response to it.
                            response.ContentLength64 = buffer.Length;
                            System.IO.Stream output = response.OutputStream;
                            output.Write(buffer, 0, buffer.Length);
                            output.Close();
                            //Console.WriteLine("Output " + DateTime.Now.ToString() + " zoom: " + request.QueryString.Get("zoom"));
                        }
                        else
                        {
                            //Console.WriteLine("Empty " + DateTime.Now.ToString());
                        }
                    }
                    catch (Exception ex)
                    {
                        httpServerActive = false;

                        if (assistantSettings.ContainsKey("panelServer") && assistantSettings["panelServer"] != 0)
                        {
                            startHTTP();
                        }

                        addLogMessage(ex.Message, 2);
                        break;
                    }
                }
            });
        }

        private async Task<byte[]> GetRadarImage(string zoom)
        {
            if (bitmapdata != null && bitmapdata.Length > 0 && !string.IsNullOrEmpty(zoom) && double.TryParse(zoom, NumberStyles.Any, CultureInfo.InvariantCulture, out double zoomResult))
            {
                Application.Current.Dispatcher.Invoke(() => updateRadarScale(zoomResult));
            }

            lastRadarRequest = absoluteTime;
            return bitmapdata;
        }

        void updateRadarScale(double zoomResult)
        {
            if (zoomResult != 0)
            {
                RadarScale.Value = Math.Min(maxRadarScale, RadarScale.Value + zoomResult);
            }
        }
        // HTTP ENDS

        void radarZoom(object sender, MouseWheelEventArgs e)
        {
            RadarScale.Value = Math.Min(maxRadarScale, RadarScale.Value - 0.01 * e.Delta);
        }
    }
}