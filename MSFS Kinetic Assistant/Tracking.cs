using CTrue.FsConnect;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;

namespace MSFS_Kinetic_Assistant
{
    class Tracking
    {
        public double lastTrackCapture = 0;
        public List<TrackPoint> trackRecording = null;
        public List<TrackPoint> trackRecorded = null;
        public double recordingCounter = 0;
        public double lastRecordingCounter = 0;
        const uint TARGETMAX = 99999999;

        //public List<TrackPoint> trackPlaying = null;
        //public double trackPlayingTimer = 0;
        //public string trackPlayingModel = "";
        //public GeoLocation trackPlayingCoords = null;

        public KeyValuePair<uint, string> message = new KeyValuePair<uint, string>();
        public string lastMessage = "";

        public List<GhostPlane> ghostPlanes = new List<GhostPlane>();
        public winchPosition ghostTeleport = new winchPosition();
        public List<string> competitiorsList = new List<string>();

        public short normalizeAngle(double rad)
        {
            short deg = (short)((rad * 180 / Math.PI) % 360);

            if (deg < 0)
                deg += 360;
            else if (deg >= 360)
                deg -= 360;

            return deg;
        }
        public double normalizeRadAngle(double angle)
        {
            if (angle < 0) { angle += 2 * Math.PI; }
            return angle;
        }

        public double zeroeRadAngle(double angle)
        {
            angle %= 2 * Math.PI;

            if (angle > Math.PI) { angle -= 2 * Math.PI; }
            else if (angle < -Math.PI) { angle += 2 * Math.PI; }

            return angle;
        }


        public void captureTrackPoint(PlaneInfoResponse _planeInfoResponse, PlaneAvionicsResponse _planeAvionicsResponse, double absoluteTime, double baseInterval = 0.5)
        {
            double trackCaptureInterval = Math.Abs(_planeInfoResponse.AirspeedIndicated) > baseInterval ?
                Math.Max(baseInterval, Math.Abs(_planeInfoResponse.AirspeedIndicated) / 30 - 1) :
                baseInterval * 5;

            if (absoluteTime - lastTrackCapture >= trackCaptureInterval)
            {
                lastTrackCapture = absoluteTime;
                trackRecording.Add(new TrackPoint(new GeoLocation(_planeInfoResponse.Latitude, _planeInfoResponse.Longitude), _planeInfoResponse.Altitude, (int)_planeInfoResponse.AltitudeAboveGround,
                    _planeInfoResponse.VelocityBodyZ, _planeInfoResponse.AirspeedIndicated, normalizeAngle(_planeInfoResponse.PlaneHeading), normalizeAngle(_planeInfoResponse.PlanePitch), normalizeAngle(_planeInfoResponse.PlaneBank),
                    packLights(_planeAvionicsResponse), packAvionics(_planeAvionicsResponse), DateTime.UtcNow, recordingCounter));

                Console.WriteLine("Track capture: " + recordingCounter);
            }
        }

        public KeyValuePair<double, string> buildTrackFile(string appName, string nickName, PlaneInfoResponse _planeInfoResponse, PlaneAvionicsResponse _planeAvionicsResponse, MathClass _mathClass, string filename, bool timeAligned = false)
        {
            string str = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><gpx creator=\"" + appName + "\" version=\"1.0\"><trk><name>" + _planeAvionicsResponse.Title + " - " + nickName + "</name><desc></desc><trkseg>";
            TrackPoint prev = new TrackPoint(new GeoLocation(0, 0), 0, 0, 0, 0, 0, 0, 0, 0, 0, DateTime.Now, 0);
            double distance = 0;

            foreach (TrackPoint trackPoint in trackRecording)
            {
                if (prev.Elevation != 0)
                {
                    double flatDistance = _mathClass.findDistanceBetweenPoints(prev.Location.Latitude, prev.Location.Longitude, trackPoint.Location.Latitude, trackPoint.Location.Longitude);
                    distance += flatDistance / Math.Cos((prev.Elevation - trackPoint.Elevation) / flatDistance);
                }

                DateTime timestamp = !timeAligned ? trackPoint.Time : (new DateTime(2000, 1, 1)).AddSeconds(trackPoint.Timer);

                str += Environment.NewLine + "<trkpt lon=\"" + (trackPoint.Location.Longitude * 180 / Math.PI).ToString(CultureInfo.InvariantCulture) + "\" lat=\"" + (trackPoint.Location.Latitude * 180 / Math.PI).ToString(CultureInfo.InvariantCulture) + "\">" +
                    Environment.NewLine + " <ele>" + trackPoint.Elevation.ToString(CultureInfo.InvariantCulture) + "</ele>" +
                    Environment.NewLine + " <agl>" + trackPoint.AltitudeAboveGround.ToString() + "</agl>" +
                    Environment.NewLine + " <velocity>" + trackPoint.Velocity.ToString(CultureInfo.InvariantCulture) + "</velocity>" +
                    Environment.NewLine + " <airspeed>" + trackPoint.Airspeed.ToString(CultureInfo.InvariantCulture) + "</airspeed>" +
                    Environment.NewLine + " <heading>" + trackPoint.Heading + "</heading>" +
                    Environment.NewLine + " <pitch>" + trackPoint.Pitch + "</pitch>" +
                    Environment.NewLine + " <roll>" + trackPoint.Roll + "</roll>" +
                    Environment.NewLine + " <lights>" + trackPoint.Lights.ToString() + "</lights>" +
                    Environment.NewLine + " <avionics>" + trackPoint.Avionics.ToString() + "</avionics>" +
                    Environment.NewLine + " <time>" + timestamp.ToString("O") + "Z" + "</time>" +
                Environment.NewLine + "</trkpt>";

                prev = trackPoint;
            }

            str += "</trkseg></trk></gpx>";

            if (trackRecording.Count <= 1)
            {
                //addLogMessage("Track data damaged!", 2);
            }


            return new KeyValuePair<double, string>(distance, str);
        }

        public GhostPlane parseTrackFile(string file, MathClass _mathClass, double allowedRecordLength)
        {
            GhostPlane ghostPlane = new GhostPlane();

            Console.WriteLine("Parsing " + file);
            XElement trackpointsXml = XElement.Load(file);
            if (trackpointsXml != null)
            {
                ghostPlane.TrackPoints = new List<TrackPoint>();
                ghostPlane.ID = TARGETMAX;

                if (trackpointsXml.Descendants("name").First() != null)
                {
                    string[] title = trackpointsXml.Descendants("name").First().Value.Split(new string[] { " - " }, StringSplitOptions.None);
                    ghostPlane.Name = title[0].Trim();
                    ghostPlane.Username = title.Length > 1 ? title[1] : "";
                }
                else
                {
                    ghostPlane.Name = "DA40-NG Asobo";
                    ghostPlane.Username = "";
                }

                double counter = 0;
                DateTime lastTimer = new DateTime();

                double latitudeOffset = 0;
                double longitudeOffset = 0;
                double headingOffset = 0;
                double altitudeOffset = 0;
                GeoLocation root = new GeoLocation(0, 0);

                foreach (var trackPoint in trackpointsXml.Descendants("trkseg").First().Elements("trkpt"))
                {
                    try
                    {
                        // FREE VERSION
                        if (counter > allowedRecordLength)
                            break;

                        double.TryParse(trackPoint.Attribute("lat").Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double lat);
                        double.TryParse(trackPoint.Attribute("lon").Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double lon);
                        lat *= Math.PI / 180;
                        lon *= Math.PI / 180;

                        if (counter == 0)
                            root = new GeoLocation(lat, lon);

                        double ele = 1000;
                        double.TryParse(trackPoint.Element("ele").Value, NumberStyles.Any, CultureInfo.InvariantCulture, out ele);
                        DateTime time = DateTime.Parse(trackPoint.Element("time").Value);

                        int agl = 10;
                        if (trackPoint.Element("agl") != null)
                            int.TryParse(trackPoint.Element("agl").Value, NumberStyles.Any, CultureInfo.InvariantCulture, out agl);

                        double velocity = 0;
                        if (trackPoint.Element("velocity") != null)
                            double.TryParse(trackPoint.Element("velocity").Value, NumberStyles.Any, CultureInfo.InvariantCulture, out velocity);

                        double airspeed = 0;
                        if (trackPoint.Element("airspeed") != null)
                            double.TryParse(trackPoint.Element("airspeed").Value, NumberStyles.Any, CultureInfo.InvariantCulture, out velocity);

                        short heading = 0;
                        if (trackPoint.Element("heading") != null)
                            short.TryParse(trackPoint.Element("heading").Value, NumberStyles.Any, CultureInfo.InvariantCulture, out heading);

                        short pitch = 0;
                        if (trackPoint.Element("pitch") != null)
                            short.TryParse(trackPoint.Element("pitch").Value, NumberStyles.Any, CultureInfo.InvariantCulture, out pitch);

                        short roll = 0;
                        if (trackPoint.Element("roll") != null)
                            short.TryParse(trackPoint.Element("roll").Value, NumberStyles.Any, CultureInfo.InvariantCulture, out roll);

                        int lights = 0;
                        if (trackPoint.Element("lights") != null)
                            int.TryParse(trackPoint.Element("lights").Value, NumberStyles.Any, CultureInfo.InvariantCulture, out lights);

                        int avionics = 0;
                        if (trackPoint.Element("avionics") != null)
                            int.TryParse(trackPoint.Element("avionics").Value, NumberStyles.Any, CultureInfo.InvariantCulture, out avionics);

                        if (counter != 0)
                            counter += (time - lastTimer).TotalSeconds;

                        GeoLocation loc = new GeoLocation(lat, lon);

                        // START OFFSETS
                        if (ghostTeleport.alt != 0 && altitudeOffset == 0)
                        {
                            //Console.WriteLine(JsonConvert.SerializeObject(ghostTeleport, Formatting.Indented));
                            latitudeOffset = ghostTeleport.location.Latitude - loc.Latitude;
                            longitudeOffset = ghostTeleport.location.Longitude - loc.Longitude;
                            headingOffset = (ghostTeleport.radius * 180 / Math.PI) - heading;
                            altitudeOffset = ghostTeleport.alt - ele;
                            Console.WriteLine("Offsets: lat" + latitudeOffset + " lon" + longitudeOffset + " head" + headingOffset + " alt" + altitudeOffset);
                        }
                        // CONTINUE OFFSETS
                        if (ghostTeleport.alt != 0 && ghostPlane.TrackPoints.Count > 0 && _mathClass.findDistanceBetweenPoints(ghostTeleport.location.Latitude, ghostTeleport.location.Longitude, root.Latitude, root.Longitude) > 100)
                        {
                            loc = _mathClass.RotatePointFrom(root, headingOffset * Math.PI / 180, loc);
                        }

                        loc.Latitude += latitudeOffset;
                        loc.Longitude += longitudeOffset;

                        heading += (short)headingOffset;
                        ele += altitudeOffset;

                        ghostPlane.TrackPoints.Add(new TrackPoint(loc, ele, agl, velocity, airspeed,
                            (short)(heading > 180 ? heading - 360 : heading), (short)(pitch > 180 ? pitch - 360 : pitch), (short)(roll > 180 ? roll - 360 : roll),
                            lights, avionics, time, counter, trackPoint.Element("message") != null ? trackPoint.Element("message").Value : ""));

                        if (counter == 0)
                        {
                            counter += 0.0001;
                        }

                        lastTimer = time;
                    }
                    catch { }
                }

                ghostPlane.Length = counter;

                if (ghostPlane.TrackPoints.Count > 0)
                {
                    Console.WriteLine(ghostPlane.TrackPoints.Count + " track points loaded");
                    ghostPlanes.Add(ghostPlane);
                }
            }
            else
            {
                MessageBox.Show("GPX file is invalid");
            }

            //trackPlaying = null;
            return ghostPlane;
        }

        public int packLights(PlaneAvionicsResponse _planeAvionicsResponse)
        {
            bool[] myBools = new bool[] {
                _planeAvionicsResponse.LIGHTBEACON == 100,
                _planeAvionicsResponse.LIGHTCABIN == 100,
                _planeAvionicsResponse.LIGHTGLARESHIELD == 100,
                _planeAvionicsResponse.LIGHTLANDING == 100,
                _planeAvionicsResponse.LIGHTLOGO == 100,
                _planeAvionicsResponse.LIGHTNAV == 100,
                _planeAvionicsResponse.LIGHTPANEL == 100,
                _planeAvionicsResponse.LIGHTPEDESTRAL == 100,
                _planeAvionicsResponse.LIGHTPOTENTIOMETER == 100,
                _planeAvionicsResponse.LIGHTRECOGNITION == 100,
                _planeAvionicsResponse.LIGHTSTROBE == 100,
                _planeAvionicsResponse.LIGHTTAXI == 100,
                _planeAvionicsResponse.LIGHTWING == 100,
            };

            byte[] byteArray = myBools.Select(b => (byte)(b ? 1 : 0)).ToArray();
            int lights = BitConverter.ToInt32(byteArray, 0);

            return lights;
        }
        public int packAvionics(PlaneAvionicsResponse _planeAvionicsResponse)
        {
            return 0;
        }
        public GhostPlane tryCaptureGhostPlane(uint ID, double absoluteTime)
        {
            int index = ghostPlanes.FindIndex(m => m.ID == TARGETMAX);
            if (index >= 0)
            {
                GhostPlane gp = ghostPlanes[index];
                gp.ID = ID;
                //gp.Progress = 0.00001;
                //gp.LastTrackPlayed = absoluteTime;

                ghostPlanes[index] = gp;

                return ghostPlanes[index];
            }

            return new GhostPlane();
        }
        public GhostPlane getGhostPlane(uint ID)
        {
            int index = ghostPlanes.FindIndex(m => m.ID == ID);
            if (index >= 0)
            {
                return ghostPlanes[index];
            }

            return new GhostPlane();
        }
        public string possiblyGetPlaneName(uint ID, string name)
        {
            if (ghostPlanes.Count > 0 && ID > 1)
            {
                GhostPlane gp = getGhostPlane(ID);
                if (!string.IsNullOrEmpty(gp.Username))
                {
                    return gp.Username;
                }
            }

            return name;
        }
        public void playRecords(double absoluteTime)
        {
            int index;
            while ((index = ghostPlanes.FindIndex(m => m.Progress == 0)) >= 0)
            {
                GhostPlane gp = ghostPlanes[index];
                gp.Progress = 0.00001;
                gp.LastTrackPlayed = absoluteTime;

                ghostPlanes[index] = gp;
            }
        }

        public void clearRecords(FsConnect _fsConnect)
        {
            foreach (GhostPlane ghostPlane in ghostPlanes)
            {
                if (ghostPlane.ID != TARGETMAX)
                {
                    _fsConnect.RemoveObject(ghostPlane.ID, Requests.TowPlane);
                }
            }
            ghostPlanes = new List<GhostPlane>();
        }

        public void clearRecord(FsConnect _fsConnect, uint ID)
        {
            for (int i = ghostPlanes.Count - 1; i >= 0; i--)
            {
                if (ID == ghostPlanes[i].ID)
                {
                    _fsConnect.RemoveObject(ghostPlanes[i].ID, Requests.TowPlane);
                    ghostPlanes.RemoveAt(i);
                }
            }
        }

        public void saveTrackfile(string str, string zipDirectory, string filename, string log = "")
        {
            try
            {
                string path = zipDirectory + filename;
                Console.WriteLine("Saving to " + path);

                if (!Directory.Exists(zipDirectory))
                {
                    try
                    {
                        Directory.CreateDirectory(zipDirectory);
                    }
                    catch
                    {
                        MessageBox.Show("Can't create task directory");
                    }
                }

                File.WriteAllText(path, str);

                if (!string.IsNullOrEmpty(log))
                {
                    File.WriteAllText(path.Replace(".gpx", ".log"), log);
                }

                Console.WriteLine("Track file saved");
            }
            catch
            {
            }
        }
        public GhostCommit updateGhostPlayer(uint ID, NearbyInfoResponse response, GhostCommit towCommit, FsConnect _fsConnect, MathClass _mathClass, double absoluteTime)
        {
            int index = 0;
            double deltaTime = 0;

            foreach (GhostPlane ghostPlane in ghostPlanes)
            {
                if (ghostPlane.ID == ID && ghostPlane.Progress > 0 && ghostPlane.TrackPoints.Count > 0)
                {
                    deltaTime = absoluteTime - ghostPlane.LastTrackPlayed;

                    TrackPoint prev = new TrackPoint();
                    TrackPoint curr = new TrackPoint();
                    TrackPoint next = new TrackPoint();

                    bool found = false;
                    foreach (var point in ghostPlane.TrackPoints)
                    {
                        if (!found)
                            curr = point;
                        else
                        {
                            next = point;
                            break;
                        }

                        if (prev.Timer < (ghostPlane.Progress + deltaTime) && point.Timer >= (ghostPlane.Progress + deltaTime))
                        {
                            found = true;

                            if (!string.IsNullOrEmpty(curr.Message) && lastMessage != curr.Message)
                            {
                                message = new KeyValuePair<uint, string>(ghostPlane.ID, point.Message);
                                lastMessage = point.Message;
                            }
                        }
                        else
                            prev = point;
                    }

                    if (found && prev.Location != null && curr.Location != null && next.Location != null)
                    {
                        try
                        {
                            double progress = ((ghostPlane.Progress + deltaTime) - prev.Timer) / (curr.Timer - prev.Timer);
                            double timeLeft = curr.Timer - (ghostPlane.Progress + deltaTime);

                            //double distancePrev = _mathClass.findDistanceBetweenPoints(response.Latitude, response.Longitude, prev.Location.Latitude, prev.Location.Longitude);
                            double distanceCurr = _mathClass.findDistanceBetweenPoints(response.Latitude, response.Longitude, curr.Location.Latitude, curr.Location.Longitude);
                            //double distanceNext = _mathClass.findDistanceBetweenPoints(response.Latitude, response.Longitude, next.Location.Latitude, next.Location.Longitude);

                            /*if (distanceCurr / timeLeft > 1.25 * curr.Velocity)
                            {
                                distanceCurr = 1.25 * curr.Velocity * timeLeft;
                                Console.WriteLine($"TOO FAR FROM NEXT POINT: {distanceCurr:F6}");
                            }*/


                            double bearing = normalizeRadAngle(_mathClass.findBearingToPoint(response.Latitude, response.Longitude, next.Location.Latitude, next.Location.Longitude));
                            response.Heading = normalizeRadAngle(response.Heading);
                            bearing = zeroeRadAngle(bearing - response.Heading);
                            double newHeading = zeroeRadAngle(response.Heading + (absoluteTime - ghostPlane.LastTrackPlayed) * bearing);
                            //towCommit.RotationVelocityBodyY = Math.Sin(newHeading - response.Heading);
                            towCommit.planeHeading = newHeading;
                            //Console.WriteLine($"Tracking heading: {response.Heading:F4} bearing: {bearing:F4} newHeading: {newHeading:F4}");

                            double requiredBank = ((1 - progress) * (prev.Roll * Math.PI / 180 - response.Bank) + progress * (curr.Roll * Math.PI / 180 - response.Bank)) / 2;
                            //towCommit.RotationVelocityBodyZ = Math.Sin(requiredBank - response.Bank);
                            towCommit.planeRoll = requiredBank;
                            double requiredPitch = ((1 - progress) * (prev.Pitch * Math.PI / 180 - response.Pitch) + progress * (prev.Pitch * Math.PI / 180 - response.Pitch)) / 2;
                            //towCommit.RotationVelocityBodyX = Math.Sin(requiredPitch - response.Pitch);
                            towCommit.planePitch = requiredPitch;

                            //Console.WriteLine($"RotationVelocityBodyX: {towCommit.RotationVelocityBodyX:F4} RotationVelocityBodyY: {towCommit.RotationVelocityBodyY:F4} RotationVelocityBodyZ: {towCommit.RotationVelocityBodyZ:F4}");
                            //towCommit.Heading = newHeading;

                            towCommit.VelocityBodyX = 0;
                            towCommit.VelocityBodyY = ((1 - progress) * (prev.Elevation - response.Altitude) + progress * (curr.Elevation - response.Altitude)) / 2;
                            towCommit.VelocityBodyZ = (0.9 * curr.Velocity + 0.1 * distanceCurr / Math.Max(1, timeLeft)) * (distanceCurr < 10 ? distanceCurr / 10 : 1);
                            
                            // TAXIING
                            if (towCommit.VelocityBodyZ < 1 && towCommit.VelocityBodyZ > -1)
                            {
                                towCommit.VelocityBodyZ = Math.Sign(towCommit.VelocityBodyZ) * Math.Pow(Math.Abs(towCommit.VelocityBodyZ), 4);
                            }


                            if (towCommit.VelocityBodyY > 10)
                            {
                                TowInfoResponse towInfo = new TowInfoResponse();
                                towInfo.Altitude = response.Altitude + towCommit.VelocityBodyY;
                                towInfo.Latitude = response.Latitude;
                                towInfo.Longitude = response.Longitude;
                                towInfo.Heading = response.Heading;
                                towInfo.Bank = response.Bank;

                                if (!double.IsNaN(towInfo.Altitude) && !double.IsNaN(towInfo.Latitude) && !double.IsNaN(towInfo.Longitude) && !double.IsNaN(towInfo.Heading) && !double.IsNaN(towInfo.Bank))
                                {
                                    try
                                    {
                                        Console.WriteLine("Sinking, TELEPORT!");
                                        _fsConnect.UpdateData(Definitions.TowPlane, towInfo, ID);
                                    }
                                    catch (Exception ex)
                                    {
                                    }
                                }
                            }

                            //Console.WriteLine("Tracking animation " + (ghostPlane.Progress + deltaTime) + " h" + towCommit.VelocityBodyZ + " v" + towCommit.VelocityBodyY + " d" + distanceCurr);
                        }
                        catch { }
                    }
                    else if (curr.Location != null && next.Location == null)
                    {
                        message = new KeyValuePair<uint, string>(ghostPlane.ID, "REMOVE");
                    }

                    break;
                }

                index++;
            }


            if (index < ghostPlanes.Count)
            {
                GhostPlane gp = ghostPlanes[index];
                gp.LastTrackPlayed = absoluteTime;
                gp.Progress += deltaTime;

                ghostPlanes[index] = gp;
            }
            else
            {

            }


            return towCommit;
        }

        public void stopGhostPlayer(uint ID)
        {
            GhostPlane gp = ghostPlanes.Find(x => x.ID == ID);
            gp.Progress = 0;
        }

        public bool ghostPlayerActive(uint ID = TARGETMAX)
        {
            foreach (GhostPlane ghostPlane in ghostPlanes)
            {
                if (ghostPlane.Progress != 0 && (ID == TARGETMAX || ghostPlane.ID == ID))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public struct GhostPlane
    {
        public GhostPlane(string name, string username, uint id, double length, double progress, double lastTrackPlayed, List<TrackPoint> trackPoints)
        {
            Name = name;
            Username = username;
            ID = id;
            Length = length;
            Progress = progress;
            LastTrackPlayed = lastTrackPlayed;
            TrackPoints = trackPoints;
        }
        public string Name;
        public string Username;
        public uint ID;
        public double Length;
        public double Progress;
        public double LastTrackPlayed;
        public List<TrackPoint> TrackPoints;
    }

    public struct TrackPoint
    {
        public TrackPoint(GeoLocation location, double elevation, int altitudeAboveGround, double velocity, double airspeed, short heading, short pitch, short roll, int lights, int avionics, DateTime time, double timer, string message = "")
        {
            Location = location;
            Elevation = elevation;
            AltitudeAboveGround = altitudeAboveGround;
            Velocity = velocity;
            Airspeed = airspeed;
            Heading = heading;
            Pitch = pitch;
            Roll = roll;
            Lights = lights;
            Avionics = avionics;
            Time = time;
            Timer = timer;
            Message = message;
        }
        public GeoLocation Location { get; set; }
        public double Elevation { get; set; }
        public double AltitudeAboveGround { get; set; }
        public double Velocity { get; set; }
        public double Airspeed { get; set; }
        public short Heading { get; set; }
        public short Pitch { get; set; }
        public short Roll { get; set; }
        public int Lights { get; set; }
        public int Avionics { get; set; }
        public DateTime Time { get; set; }
        public double Timer { get; set; }
        public string Message { get; set; }
    }

}

