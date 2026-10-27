using Kassyi.GSharp.Core;

// A library whose function uses the fields of a struct of HandWrittenLease.cs (whose record failed for C++)
namespace XasmB
{
    public static partial class LibB
    {
        [GpuDeviceFunction]
        public static int Hold(int x)
        {
            var lease = new XasmLib.Lease();
            lease.V = x;
            return lease.V + 1;
        }
    }
}
