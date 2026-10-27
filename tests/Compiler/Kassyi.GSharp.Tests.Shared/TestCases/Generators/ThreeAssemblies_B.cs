using Kassyi.GSharp.Core;

// The second library: device functions that use the first library's structs
namespace XasmB
{
    public static partial class LibB
    {
        [GpuDeviceFunction]
        public static float Len(float x, float y)
        {
            var v = new XasmA.Vec2();
            v.X = x;
            v.Y = y;
            return v.Sum;
        }

        [GpuDeviceFunction]
        public static int Borrow(int x)
        {
            using var lease = new XasmA.Lease();
            return x + lease.V;
        }
    }
}
