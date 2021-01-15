using System;
using System.Runtime.InteropServices;

namespace MSFS_Kinetic_Assistant
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct PlaneInfoRotate
    {
        public double RotationVelocityBodyX;
        public double RotationVelocityBodyY;
        public double RotationVelocityBodyZ;
    };
}
