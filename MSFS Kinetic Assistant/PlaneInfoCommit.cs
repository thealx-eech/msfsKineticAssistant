using System;
using System.Runtime.InteropServices;

namespace MSFS_Kinetic_Assistant
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct PlaneInfoCommit
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public String Title;
        public double VelocityBodyX;
        public double VelocityBodyY;
        public double VelocityBodyZ;
        public double AbsoluteTime;
        public double TailhookPosition;
        public double LaunchbarPosition;
    };
}
