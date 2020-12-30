using Accord.Math;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSFS_Cloud_Assistant
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

            Matrix3x3 attitude = Matrix3x3.CreateFromYawPitchRoll((float)_planeInfoResponse.PlaneHeading, (float)_planeInfoResponse.PlanePitch, (float)_planeInfoResponse.PlaneBank);
            _winchDirection.localForceDirection = Matrix3x3.Multiply(attitude.Inverse(), globalToWinchNorm);
            _winchDirection.localForceDirection.Normalize();

            _winchDirection.heading = Math.Atan2(_winchDirection.localForceDirection.X, _winchDirection.localForceDirection.Z);//Math.Atan2(globalToWinchNorm.X, globalToWinchNorm.Z) - _planeInfoResponse.PlaneHeading;
            _winchDirection.pitch = Math.Asin(_winchDirection.localForceDirection.Y/* / localForceNorm*/);//Math.Asin(globalToWinchNorm.Y) + _planeInfoResponse.PlanePitch;
            _winchDirection.distance = (double)(globalToWinch.Norm);

            if (_winchDirection.heading > Math.PI) { _winchDirection.heading -= 2 * Math.PI; }
            if (_winchDirection.heading < -Math.PI) { _winchDirection.heading += 2 * Math.PI; }
            if (_winchDirection.pitch > Math.PI) { _winchDirection.pitch -= 2 * Math.PI; }
            if (_winchDirection.pitch < -Math.PI) { _winchDirection.pitch += 2 * Math.PI; }

            return _winchDirection;
        }

        public static GeoLocation FindPointAtDistanceFrom(GeoLocation startPoint, double initialBearingRadians, double distanceKilometres)
        {
            const double radiusEarthKilometres = 6371.01;
            var distRatio = distanceKilometres / radiusEarthKilometres;
            var distRatioSine = Math.Sin(distRatio);
            var distRatioCosine = Math.Cos(distRatio);

            var startLatRad = startPoint.Latitude;
            var startLonRad = startPoint.Longitude;

            var startLatCos = Math.Cos(startLatRad);
            var startLatSin = Math.Sin(startLatRad);

            var endLatRads = Math.Asin((startLatSin * distRatioCosine) + (startLatCos * distRatioSine * Math.Cos(initialBearingRadians)));

            var endLonRads = startLonRad
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
    }

    public class winchPosition
    {
        public GeoLocation location { get; set; }
        public double alt { get; set; }
    }

    public class winchDirection
    {
        public double heading { get; set; }
        public double pitch { get; set; }
        public double distance { get; set; }
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
