using System;
using System.Runtime.InteropServices;

namespace MSFS_Cloud_Assistant
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct PlaneInfoResponse
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public String Title;
        public double Latitude;
        public double Longitude;
        public double AltitudeAboveGround;
        public double StaticCGtoGround;
        public double Altitude;
        public double AbsoluteTime;
        public double PlaneHeading;
        public double PlanePitch;
        public double PlaneBank;
        public double SimOnGround;
        public double BrakeParkingPosition;

        public double LIGHTPANEL;
        public double LIGHTSTROBE;
        public double LIGHTLANDING;
        public double LIGHTTAXI;
        public double LIGHTBEACON;
        public double LIGHTNAV;
        public double LIGHTLOGO;
        public double LIGHTWING;
        public double LIGHTRECOGNITION;
        public double LIGHTCABIN;
        public double LIGHTGLARESHIELD;
        public double LIGHTPEDESTRAL;
        public double LIGHTPOTENTIOMETER;

        // SHORTCUTS
    };
}
