using Accord.Math;
using System;

namespace MSFS_Kinetic_Assistant
{
    class MathClass
    {
        public winchPosition getWinchPosition(PlaneInfoResponse _planeInfoResponse, double _stringLength)
        {
            winchPosition _winchPosition = new winchPosition();
            _winchPosition.location = FindPointAtDistanceFrom(new GeoLocation(_planeInfoResponse.Latitude, _planeInfoResponse.Longitude), _planeInfoResponse.PlaneHeading, _stringLength / 1000);
            _winchPosition.alt = _planeInfoResponse.Altitude + 2;

            return _winchPosition;
        }

        public winchDirection getForceDirection(winchPosition _winchPosition, PlaneInfoResponse _planeInfoResponse) {
            winchDirection _winchDirection = new winchDirection();

            double globalX = (_winchPosition.location.Longitude - _planeInfoResponse.Longitude) * Math.Cos(_winchPosition.location.Latitude) * 6378137;
            double globalY = _winchPosition.alt - _planeInfoResponse.Altitude;
            double globalZ = (_winchPosition.location.Latitude - _planeInfoResponse.Latitude) * 180 / Math.PI * 111694;
            Vector3 globalToWinch = new Vector3((float)globalX, (float)globalY, (float)globalZ);
            Vector3 globalToWinchNorm = globalToWinch;
            globalToWinchNorm.Normalize();
            
            _winchDirection.climbAngle = Math.Abs(Math.Asin(globalToWinchNorm.Y));

            Matrix3x3 attitude = Matrix3x3.CreateFromYawPitchRoll((float)_planeInfoResponse.PlaneHeading, (float)_planeInfoResponse.PlanePitch, (float)_planeInfoResponse.PlaneBank);
            _winchDirection.localForceDirection = Matrix3x3.Multiply(attitude.Inverse(), globalToWinchNorm);
            _winchDirection.localForceDirection.Normalize();

            _winchDirection.heading = Math.Atan2(_winchDirection.localForceDirection.X, _winchDirection.localForceDirection.Z);
            _winchDirection.pitch = Math.Asin(_winchDirection.localForceDirection.Y);
            _winchDirection.distance = (double)(globalToWinch.Norm);
            globalToWinch.Y = 0;
            _winchDirection.groundDistance = (double)(globalToWinch.Norm);

            if (_winchDirection.heading > Math.PI) { _winchDirection.heading -= 2 * Math.PI; }
            if (_winchDirection.heading < -Math.PI) { _winchDirection.heading += 2 * Math.PI; }
            if (_winchDirection.pitch > Math.PI) { _winchDirection.pitch -= 2 * Math.PI; }
            if (_winchDirection.pitch < -Math.PI) { _winchDirection.pitch += 2 * Math.PI; }

            return _winchDirection;
        }

        public GeoLocation FindPointAtDistanceFrom(GeoLocation startPoint, double initialBearingRadians, double distanceKilometres)
        {
            const double radiusEarthKilometres = 6371.01;
            double distRatio = distanceKilometres / radiusEarthKilometres;
            double distRatioSine = Math.Sin(distRatio);
            double distRatioCosine = Math.Cos(distRatio);

            double startLatCos = Math.Cos(startPoint.Latitude);
            double startLatSin = Math.Sin(startPoint.Latitude);

            double endLatRads = Math.Asin((startLatSin * distRatioCosine) + (startLatCos * distRatioSine * Math.Cos(initialBearingRadians)));

            double endLonRads = startPoint.Longitude
                + Math.Atan2(
                    Math.Sin(initialBearingRadians) * distRatioSine * startLatCos,
                    distRatioCosine - startLatSin * Math.Sin(endLatRads));

            return new GeoLocation(endLatRads, endLonRads);
        }
        public double restrictAirspeed(double speed, double _targetSpeed, double lastFrameTiming)
        {
            if (Math.Abs(speed) > _targetSpeed)
                speed -= speed * Math.Sign(speed) * Math.Pow((Math.Abs(speed) - _targetSpeed) / _targetSpeed, 2) * lastFrameTiming;

            return speed;
        }

        public double getCableTension(double cableLength, double elasticExtension, winchDirection _winchDirection)
        {
            double cableTension = 0;
            double dumpingLength = Math.Max(5.0, cableLength * elasticExtension / 100);

            // CALCULATION TENSION MODIFIER
            if (cableLength < _winchDirection.distance)
            {
                cableTension = (_winchDirection.distance - cableLength) / dumpingLength;
            }

            return cableTension;
        }

        public double getBodyVelocity(winchDirection _winchDirection, PlaneInfoCommit _planeCommit, double cableTension, double accelerationLimit, double cableLength, double cableLengthPrev, double cableLengthPrePrev, double lastFrameTiming)
        {
            double baseAcceleration = 5 * 9.81;

            if (Math.Abs(cableLength - cableLengthPrev) > 10) { cableLengthPrev = cableLength; }
            if (Math.Abs(cableLengthPrev - cableLengthPrePrev) > 10) { cableLengthPrePrev = cableLengthPrev; }
            double lengthDiff = (cableLength - cableLengthPrev) * 0.5 + (cableLengthPrev - cableLengthPrePrev) * 0.5;
            double cableSpeed = lengthDiff / lastFrameTiming * cableTension;
            if (cableTension > 1 && lengthDiff > 0)
                cableSpeed += baseAcceleration * Math.Pow(cableTension - 1, 2);

            if (cableSpeed < 2 * accelerationLimit && cableSpeed > accelerationLimit)
            {
                cableSpeed = Math.Min(cableSpeed, 0.99 * accelerationLimit);
            }

            Vector3 planeMotion = new Vector3((float)(_planeCommit.VelocityBodyX * Math.Cos(_winchDirection.heading) * Math.Cos(_winchDirection.pitch)),
                (float)(_planeCommit.VelocityBodyY * Math.Sin(_winchDirection.heading)),
                (float)(_planeCommit.VelocityBodyZ * Math.Cos(_winchDirection.heading) * Math.Sin(_winchDirection.pitch)));

            double appliedVelocity = cableSpeed - planeMotion.Norm;

            Console.WriteLine($"getBodyVelocity: {cableTension:F4} {appliedVelocity:F4}m/s  lengthDiff{lengthDiff:F4}m cableSpeed{cableSpeed:F4}");
            //Console.WriteLine($"cabC: {cableLength:F4}m cabP{cableLengthPrev:F4}m  cabPP{cableLengthPrePrev:F4}m");

            return appliedVelocity > 0 ? appliedVelocity : 0;
        }
    }

    public class winchPosition
    {
        public winchPosition() { }
        public winchPosition(GeoLocation Location, double Altitude, double Radius = 0, double Airspeed = 0, string Title = "", string Category = "")
        {
            location = Location;
            alt = Altitude;
            radius = Radius;
            airspeed = Airspeed;
            title = Title;
            category = Category;
        }

        public GeoLocation location { get; set; }
        public double alt { get; set; }
        public double radius { get; set; }
        public double airspeed { get; set; }
        public string title { get; set; }
        public string category { get; set; }
    }

    public class winchDirection
    {
        public double heading { get; set; }
        public double pitch { get; set; }
        public double distance { get; set; }
        public double groundDistance { get; set; }
        public double climbAngle { get; set; }
        public Vector3 localForceDirection { get; set; }
    }

    public class GeoLocation
    {
        public GeoLocation(double latitude, double longitude)
        {
            Latitude = latitude;
            Longitude = longitude;
        }

        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
}
