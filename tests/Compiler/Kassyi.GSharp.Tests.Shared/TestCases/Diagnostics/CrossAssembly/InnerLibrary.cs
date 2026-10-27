using Kassyi.GSharp.Core;

// A library that another library calls
namespace XasmInner
{
    public static class LibInner
    {
        [GpuDeviceFunction]
        public static int Inc(int x) => x + 1;
    }
}
