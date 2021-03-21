﻿using CTrue.FsConnect;
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
        const uint TARGETMAX = 99999999;

        //public List<TrackPoint> trackPlaying = null;
        //public double trackPlayingTimer = 0;
        //public string trackPlayingModel = "";
        //public GeoLocation trackPlayingCoords = null;

        public KeyValuePair<uint,string> message = new KeyValuePair<uint, string>();
        public string lastMessage = "";

        public List<GhostPlane> ghostPlanes = new List<GhostPlane>();
        public winchPosition ghostTeleport = new winchPosition();

        public short normalizeAngle(double rad)
        {
            short deg = (short)((rad * 180 / Math.PI) % 360);

            if (deg < 0)
                deg += 360;
            else if (deg >= 360)
                deg -= 360;

            return deg;
        }

        public void captureTrackPoint(PlaneInfoResponse _planeInfoResponse, PlaneInfoCommit _planeCommit, double absoluteTime, double baseInterval = 0.5)
        {
            double trackCaptureInterval = Math.Abs(_planeInfoResponse.AirspeedIndicated) > baseInterval ?
                Math.Max(baseInterval, Math.Abs(_planeInfoResponse.AirspeedIndicated) / 30 - 1) :
                baseInterval * 5;
            
            if (absoluteTime - lastTrackCapture >= trackCaptureInterval)
            {
                lastTrackCapture = absoluteTime;
                trackRecording.Add(new TrackPoint(new GeoLocation(_planeInfoResponse.Latitude, _planeInfoResponse.Longitude), _planeInfoResponse.Altitude, (int)_planeInfoResponse.AltitudeAboveGround,
                    _planeCommit.VelocityBodyZ, normalizeAngle(_planeInfoResponse.PlaneHeading), normalizeAngle(_planeInfoResponse.PlanePitch), normalizeAngle(_planeInfoResponse.PlaneBank),
                    packLights(_planeInfoResponse), packAvionics(_planeInfoResponse), DateTime.UtcNow, recordingCounter));

                Console.WriteLine("Track capture: " + recordingCounter);
            }
        }

        public KeyValuePair<double, string> buildTrackFile(string appName, string nickName, PlaneInfoResponse _planeInfoResponse, MathClass _mathClass, string filename, bool timeAligned = false)
        {
            string str = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><gpx creator=\"" + appName + "\" version=\"1.0\"><trk><name>" + _planeInfoResponse.Title + " - " + nickName + "</name><desc></desc><trkseg>";
            TrackPoint prev = new TrackPoint(new GeoLocation(0, 0), 0, 0, 0, 0, 0, 0, 0, 0, DateTime.Now, 0);
            double distance = 0;

            foreach (TrackPoint trackPoint in trackRecording)
            {
                if (prev.Elevation != 0)
                {
                    double flatDistance = _mathClass.findDistanceBetweenPoints(prev.Location.Latitude, prev.Location.Longitude, trackPoint.Location.Latitude, trackPoint.Location.Longitude);
                    distance += flatDistance / Math.Cos((prev.Elevation - trackPoint.Elevation) / flatDistance);
                }

                DateTime timestamp = !timeAligned ? trackPoint.Time : (new DateTime(2000, 1, 1)).AddSeconds(trackPoint.Timer);

                str += "<trkpt lon=\"" + (trackPoint.Location.Longitude * 180 / Math.PI).ToString(CultureInfo.InvariantCulture) + "\" lat=\"" + (trackPoint.Location.Latitude * 180 / Math.PI).ToString(CultureInfo.InvariantCulture) + "\">" +
                    "<ele>" + trackPoint.Elevation.ToString(CultureInfo.InvariantCulture) + "</ele>" +
                    "<agl>" + trackPoint.AltitudeAboveGround.ToString() + "</agl>" +
                    "<velocity>" + trackPoint.Velocity.ToString(CultureInfo.InvariantCulture) + "</velocity>" +
                    "<heading>" + trackPoint.Heading + "</heading>" +
                    "<pitch>" + trackPoint.Pitch + "</pitch>" +
                    "<roll>" + trackPoint.Roll + "</roll>" +
                    "<lights>" + trackPoint.Lights.ToString() + "</lights>" +
                    "<avionics>" + trackPoint.Avionics.ToString() + "</avionics>" +
                    "<time>" + timestamp.ToString("O") + "Z" + "</time>" +
                "</trkpt>";

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
                    ghostPlane.Name = trackpointsXml.Descendants("name").First().Value.Split(new string[] { " - " }, StringSplitOptions.None)[0].Trim();
                }
                else
                {
                    ghostPlane.Name = "DA40-NG Asobo";
                }

                double counter = 0;
                DateTime lastTimer = new DateTime();

                double latitudeOffset = 0;
                double longitudeOffset = 0;
                double headingOffset = 0;
                double altitudeOffset = 0;
                GeoLocation root = new GeoLocation(0,0);

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
                        if (ghostTeleport.alt != 0 && ghostPlane.TrackPoints.Count > 0)
                        {
                            loc = _mathClass.RotatePointFrom(root, headingOffset * Math.PI / 180, loc);
                        }

                        loc.Latitude += latitudeOffset;
                        loc.Longitude += longitudeOffset;

                        heading += (short)headingOffset;
                        ele += altitudeOffset;

                        ghostPlane.TrackPoints.Add(new TrackPoint(loc, ele, agl, velocity,
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

        public int packLights(PlaneInfoResponse _planeInfoResponse)
        {
            bool[] myBools = new bool[] {
                _planeInfoResponse.LIGHTBEACON == 100,
                _planeInfoResponse.LIGHTCABIN == 100,
                _planeInfoResponse.LIGHTGLARESHIELD == 100,
                _planeInfoResponse.LIGHTLANDING == 100,
                _planeInfoResponse.LIGHTLOGO == 100,
                _planeInfoResponse.LIGHTNAV == 100,
                _planeInfoResponse.LIGHTPANEL == 100,
                _planeInfoResponse.LIGHTPEDESTRAL == 100,
                _planeInfoResponse.LIGHTPOTENTIOMETER == 100,
                _planeInfoResponse.LIGHTRECOGNITION == 100,
                _planeInfoResponse.LIGHTSTROBE == 100,
                _planeInfoResponse.LIGHTTAXI == 100,
                _planeInfoResponse.LIGHTWING == 100,
            };

            byte[] byteArray = myBools.Select(b => (byte)(b ? 1 : 0)).ToArray();
            int lights = BitConverter.ToInt32(byteArray, 0);

            return lights;
        }
        public int packAvionics(PlaneInfoResponse _planeInfoResponse)
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
        public void playRecords(double absoluteTime)
        {
            int index;
            while ((index = ghostPlanes.FindIndex(m => m.Progress == 0)) >= 0) {
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
        public TowInfoPitch updateGhostPlayer(uint ID, NearbyInfoResponse response, TowInfoPitch towCommit, MathClass _mathClass, double absoluteTime)
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

                            double distancePrev = _mathClass.findDistanceBetweenPoints(response.Latitude, response.Longitude, prev.Location.Latitude, prev.Location.Longitude);
                            double distanceCurr = _mathClass.findDistanceBetweenPoints(response.Latitude, response.Longitude, curr.Location.Latitude, curr.Location.Longitude);
                            double distanceNext = _mathClass.findDistanceBetweenPoints(response.Latitude, response.Longitude, next.Location.Latitude, next.Location.Longitude);
                            double bearing = _mathClass.findBearingToPoint(response.Latitude, response.Longitude, next.Location.Latitude, next.Location.Longitude);
                            if (bearing < 0) { bearing += 2 * Math.PI; }
                            if (towCommit.Heading < 0) { towCommit.Heading += 2 * Math.PI; }

                            bearing = (bearing - towCommit.Heading) % (2 * Math.PI);

                            if (bearing > Math.PI) { bearing -= 2 * Math.PI; }
                            else if (bearing < -Math.PI) { bearing += 2 * Math.PI; }

                            /*if (towCommit.Heading - bearing < - Math.PI)
                                bearing -= Math.PI;
                            else if (towCommit.Heading - bearing > Math.PI)
                                bearing += Math.PI;*/

                            //towCommit.Pitch = 0.9 * towCommit.Pitch + 0.1 * (((1 - progress) * prev.Pitch + progress * curr.Pitch) / 2 * Math.PI / 180);
                            towCommit.Bank = /*0.9 * towCommit.Bank + 0.1 **/ (((1 - progress) * ((double)prev.Roll) + progress * ((double)curr.Roll)) / 2 * Math.PI / 180);
                            towCommit.Pitch = /*0.9 * towCommit.Bank + 0.1 **/ (((1 - progress) * ((double)prev.Pitch) + progress * ((double)curr.Pitch)) / 2 * Math.PI / 180);
                            /*towCommit.Heading = _mathClass.findBearingToPoint(response.Latitude, response.Longitude, 
                                ((1 - progress) * prev.Location.Latitude + progress * curr.Location.Latitude) / 2,
                                ((1 - progress) * prev.Location.Longitude + progress * curr.Location.Longitude) / 2);*/

                            //towCommit.Heading -= bearing * lastFrameTiming; //Math.Pow(Math.Abs(bearing) / Math.PI, 0.5);
                            double newHeading = towCommit.Heading + (absoluteTime - ghostPlane.LastTrackPlayed) * bearing;
                            newHeading %= 2 * Math.PI;
                            if (newHeading > Math.PI) { newHeading -= 2 * Math.PI; }
                            else if (newHeading < -Math.PI) { newHeading += 2 * Math.PI; }

                            Console.WriteLine("Tracking heading " + towCommit.Heading + " bearing" + bearing + " newHeading" + newHeading);
                            towCommit.Heading = newHeading;

                            towCommit.VelocityBodyY = ((1 - progress) * (prev.Elevation - response.Altitude) + progress * (curr.Elevation - response.Altitude)) / 2;
                            //towCommit.VelocityBodyZ = Math.Min(100, distanceCurr / timeLeft) * lastFrameTiming;
                            towCommit.VelocityBodyZ = (0.8 * curr.Velocity + 0.1 * distanceCurr / Math.Max(1, timeLeft)) * (Math.Abs(distanceCurr) < 10 ? Math.Abs(distanceCurr) / 10 : 1);

                            if (towCommit.VelocityBodyZ < 0.01 && towCommit.VelocityBodyZ > -0.01)
                            {
                                towCommit.VelocityBodyZ = 0;
                            }

                            Console.WriteLine("Tracking animation " + (ghostPlane.Progress + deltaTime) + " h" + towCommit.VelocityBodyZ + " v" + towCommit.VelocityBodyY + " d" + distanceCurr);
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


            if (index < ghostPlanes.Count) {
                GhostPlane gp = ghostPlanes[index];
                gp.LastTrackPlayed = absoluteTime;
                gp.Progress += deltaTime;

                ghostPlanes[index] = gp;
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
        public GhostPlane(string name, uint id, double length, double progress, double lastTrackPlayed, List<TrackPoint> trackPoints)
        {
            Name = name;
            ID = id;
            Length = length;
            Progress = progress;
            LastTrackPlayed = lastTrackPlayed;
            TrackPoints = trackPoints;
        }
        public string Name;
        public uint ID;
        public double Length;
        public double Progress;
        public double LastTrackPlayed;
        public List<TrackPoint> TrackPoints;
    }

    public struct TrackPoint
    {
        public TrackPoint(GeoLocation location, double elevation, int altitudeAboveGround, double velocity, short heading, short pitch, short roll, int lights, int avionics, DateTime time, double timer, string message = "")
        {
            Location = location;
            Elevation = elevation;
            AltitudeAboveGround = altitudeAboveGround;
            Velocity = velocity;
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

