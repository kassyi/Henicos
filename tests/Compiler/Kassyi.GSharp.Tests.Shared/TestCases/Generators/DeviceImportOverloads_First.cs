using Kassyi.GSharp.Core;

// The first part, added first to the compilation but named after the second: the library orders the overloads by
// file path (a_second.cs first), metadata by the order of the parts
namespace OverloadLibrary
{
    public static partial class LibPick
    {
        [GpuDeviceFunction]
        public static int Pick(int x) => x + 1;
    }
}
