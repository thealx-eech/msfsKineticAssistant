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
        public double LightWing;
        public double LightLogo;
        public double LightRecognition;
        public double SimOnGround;
        public double BrakeParkingPosition;
    };
}
