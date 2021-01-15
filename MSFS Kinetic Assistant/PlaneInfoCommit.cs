using System;
using System.Runtime.InteropServices;

namespace MSFS_Kinetic_Assistant
{
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
