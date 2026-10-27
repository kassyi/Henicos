using Kassyi.GSharp.Core;

namespace OverloadLibrary
{
    public static partial class LibPick
    {
        [GpuDeviceFunction]
        public static float Pick(float x) => x * 2f;
    }
}
