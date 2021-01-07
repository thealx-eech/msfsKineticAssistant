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

            // PREPARE CONTROLS DATA
            controlTimestamps = new Dictionary<string, double>();
            foreach (var field in typeof(PlaneInfoResponse).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (field.FieldType == typeof(double))
                    controlTimestamps.Add(field.Name, 0);

            InitializeComponent();

            // COMMON SIM VALUES REQUEST TIMER
            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 250);
            dispatcherTimer.Tick += new EventHandler(commonInterval);
            dispatcherTimer.Start();

            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 50);
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
                if (_winchPosition == null && _carrierPosition == null && targedCatapultVelocity == 0 && !thermalsWorking)
                    _fsConnect.RequestData(Requests.PlaneInfo, Definitions.PlaneInfo);
            }
        }

        public void launchInterval(object sender, EventArgs e)
        {
            if (_fsConnect != null)
            {
                if (_winchPosition != null || _carrierPosition != null || targedCatapultVelocity != 0 || thermalsWorking)
                {
                    _fsConnect.RequestData(Requests.PlaneInfo, Definitions.PlaneInfo);
                    _fsConnect.RequestData(Requests.PlaneCommit, Definitions.PlaneCommit);
                    _fsConnect.RequestData(Requests.PlaneRotate, Definitions.PlaneRotate);
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
                changeButtonStatus(true, connectButton, true, "DISCONNECT");
                changeButtonStatus(true, launchPrepareButton, true);
                changeButtonStatus(true, hookPrepareButton, true);
                changeButtonStatus(true, catapultlaunchButton, true);
                changeButtonStatus(true, thermalsToggleButton, true); 



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

                changeButtonStatus(false, connectButton, true, "CONNECT");
                changeButtonStatus(false, launchPrepareButton, false);
                changeButtonStatus(false, hookPrepareButton, false);
                changeButtonStatus(false, catapultlaunchButton, false);
                changeButtonStatus(false, thermalsToggleButton, false);

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
            if (winchAbortInitiated)
            {
                _winchPosition = null;
                launchTime = 0;
                cableLength = 0;
                winchAbortInitiated = false;
            }
            else
            {

                // GET ANGLE TO WINCH POSITION
                winchDirection _winchDirection = _mathClass.getForceDirection(_winchPosition, _planeInfoResponse);
                double targetVelocity = 0.514 * assistantSettings["targetSpeed"];

                // GET DRAFT CABLE TENSION
                double cableTension = _mathClass.getCableTension(cableLength, Math.Max(1, assistantSettings["elasticExtension"]), _winchDirection, lastFrameTiming);

                // SHORTEN THE STRING
                if (launchTime != 0 && launchTime - absoluteTime < 0 && cableLength > 10)
                {
                    double pitchCompensation = Math.Cos(_winchDirection.climbAngle);
                    double tensionMultiplier = Math.Pow(1 - Math.Min(cableTension / (6 * 9.81), 1), 0.75);

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

                // GET FINAL CABLE TENSION
                cableTension = _mathClass.getCableTension(cableLength, Math.Max(1, assistantSettings["elasticExtension"]), _winchDirection, lastFrameTiming);

                Console.WriteLine($"Winch: {cableTension/9.81:F2}g {cableLength:F2}m / {_winchDirection.distance:F2}m h{(_winchDirection.heading * 180 / Math.PI):F0}deg p{(_winchDirection.pitch * 180 / Math.PI):F0}deg");

                double angleLimit = 89 * Math.PI / 180;
                if (assistantSettings["enableFailure"] == 1 && cableTension >= 6 * 9.81 || _winchDirection.heading < -angleLimit || _winchDirection.heading > angleLimit || _winchDirection.pitch < -angleLimit || _winchDirection.pitch > angleLimit)
                {
                    showMessage(assistantSettings["enableFailure"] == 1 && cableTension >= 6 * 9.81 ?
                        "Whinch cable failure" :
                        "Winch cable released. Gained altitude: " + (_planeInfoResponse.Altitude - _winchPosition.alt).ToString("0.0") + " meters", _fsConnect);
                    Application.Current.Dispatcher.Invoke(() => playSound("false"));
                    winchAbortInitiated = true;

                    Application.Current.Dispatcher.Invoke(() => abortLaunch());
                }
                else if (cableTension != 0)
                {
                    _planeCommit.VelocityBodyX += _winchDirection.localForceDirection.X * cableTension * lastFrameTiming;
                    _mathClass.restrictAirspeed(_planeCommit.VelocityBodyX, targetVelocity, lastFrameTiming);
                    _planeCommit.VelocityBodyY += _winchDirection.localForceDirection.Y * cableTension * lastFrameTiming;
                    _mathClass.restrictAirspeed(_planeCommit.VelocityBodyY, targetVelocity, lastFrameTiming);
                    _planeCommit.VelocityBodyZ += _winchDirection.localForceDirection.Z * cableTension * lastFrameTiming;
                    _mathClass.restrictAirspeed(_planeCommit.VelocityBodyZ, targetVelocity, lastFrameTiming);
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
                    // ARREST IN PROCESS - FIND OUT TENSION
                    cableTension = 1.2 * (_planeCommit.VelocityBodyZ * lastFrameTiming / (timeLeft / 1.5) + (accel > 0 ? accel : 0));
                    cableTension = Math.Min(9.81 * 5 * lastFrameTiming, cableTension);

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
                            if (double.TryParse(data[3], out double lat) &&
                                double.TryParse(data[4], out double lng) &&
                                double.TryParse(data[5], out double alt) &&
                                double.TryParse(data[7], out double radius))
                            {
                                winchPosition _thermalPosition = new winchPosition(new GeoLocation(lat / 180 * Math.PI, lng / 180 * Math.PI), 0.305 * alt, 1852 * radius);

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
            foreach (winchPosition thermal in thermalsList)
            {
                //Console.WriteLine(_planeInfoResponse.AltitudeAboveGround + " " + thermal.alt);
                if (_planeInfoResponse.AltitudeAboveGround < thermal.alt && thermal.radius > 0)
                {
                    winchPosition thermalTemp = new winchPosition(thermal.location, thermal.alt, thermal.radius);
                    thermalTemp.alt = _planeInfoResponse.Altitude - 10 * thermalTemp.radius;
                    winchDirection _thermalDirection = _mathClass.getForceDirection(thermalTemp, _planeInfoResponse);

                    if (_thermalDirection.groundDistance < thermalTemp.radius)
                    {
                        double force = - Math.Pow(1 - _thermalDirection.groundDistance / thermalTemp.radius, 0.5) * Math.Abs(Math.Cos(_planeInfoResponse.PlaneBank)) * Math.Abs(Math.Cos(_planeInfoResponse.PlanePitch));
                        force *= 3;

                        Console.WriteLine("Thermal: " + _thermalDirection.localForceDirection.X * force + " " + _thermalDirection.localForceDirection.Y * force + " " + _thermalDirection.localForceDirection.Z * force);

                        _planeCommit.VelocityBodyX += _thermalDirection.localForceDirection.X * force * lastFrameTiming;
                        _planeCommit.VelocityBodyY += _thermalDirection.localForceDirection.Y * force * lastFrameTiming;
                        _planeCommit.VelocityBodyZ += _thermalDirection.localForceDirection.Z * force * lastFrameTiming;

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

                id++;
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
                    _planeInfoResponse = (PlaneInfoResponse)e.Data;

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
            cDefinition.Add(new SimProperty(FsSimVar.Title, FsUnit.None, SIMCONNECT_DATATYPE.STRING256));
            cDefinition.Add(new SimProperty(FsSimVar.VelocityBodyX, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.VelocityBodyY, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.VelocityBodyZ, FsUnit.Radians, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.AbsoluteTime, FsUnit.Second, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.TailhookPosition, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));
            cDefinition.Add(new SimProperty(FsSimVar.LaunchbarPosition, FsUnit.Percent, SIMCONNECT_DATATYPE.FLOAT64));

            fsConnect.RegisterDataDefinition<PlaneInfoCommit>(Definitions.PlaneCommit, cDefinition);

            List<SimProperty> rDefinition = new List<SimProperty>();
            rDefinition.Add(new SimProperty(FsSimVar.Title, FsUnit.None, SIMCONNECT_DATATYPE.STRING256));
            rDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyX, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            rDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyY, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            rDefinition.Add(new SimProperty(FsSimVar.RotationVelocityBodyZ, FsUnit.RadianPerSecond, SIMCONNECT_DATATYPE.FLOAT64));

            fsConnect.RegisterDataDefinition<PlaneInfoRotate>(Definitions.PlaneRotate, rDefinition);
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
    }
}