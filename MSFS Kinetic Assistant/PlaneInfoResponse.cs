using System;
using System.Runtime.InteropServices;

namespace MSFS_Kinetic_Assistant
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
        public double OnAnyRunway;

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct PlaneInfoRotate
    {
        public double RotationVelocityBodyX;
        public double RotationVelocityBodyY;
        public double RotationVelocityBodyZ;
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct NearbyInfoResponse
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public String Title;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public String Category;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
        public String FlightNumber;

        public double Latitude;
        public double Longitude;
        public double Altitude;
        public double Airspeed;
        public double Verticalspeed;
        public double Heading;
        public double Bank;
        public double SimOnGround;
        public double OnAnyRunway;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct TowInfoResponse
    {
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public double Heading;
        public double Bank;
        public double VelocityBodyX;
        public double VelocityBodyY;
        public double VelocityBodyZ;
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct TowInfoPitch
    {
        public double Bank;
        public double VelocityBodyY;
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct PlaneInfoCommit
    {
        public double VelocityBodyX;
        public double VelocityBodyY;
        public double VelocityBodyZ;
        public double AbsoluteTime;
        public double TailhookPosition;
        public double LaunchbarPosition;
        public double WaterRudderHandlePosition;
    };

}
