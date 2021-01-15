using System;
using System.Runtime.InteropServices;

namespace MSFS_Kinetic_Assistant
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct NearbyInfoResponse
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public String Title;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public String Category;
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public double Airspeed;
    }
}