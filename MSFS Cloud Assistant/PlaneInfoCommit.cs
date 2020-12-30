using System;
using System.Runtime.InteropServices;

namespace MSFS_Cloud_Assistant
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct PlaneInfoCommit
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public String Title;
        public double VelocityBodyX;
        public double VelocityBodyY;
        public double VelocityBodyZ;
        //public double RotationVelocityBodyX;
        //public double RotationVelocityBodyY;
        //public double RotationVelocityBodyZ;
        public double AbsoluteTime;
        public double LightWing;
        public double LightLogo;
        public double LightRecognition;
        public double TailhookPosition;
        public double LaunchbarPosition;
    };
}
