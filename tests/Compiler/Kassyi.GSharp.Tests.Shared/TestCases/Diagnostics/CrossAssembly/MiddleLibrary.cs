using Kassyi.GSharp.Core;

// A library whose device function calls one of InnerLibrary.cs, another assembly
namespace XasmMiddle
{
    public static class LibMiddle
    {
        [GpuDeviceFunction]
        public static int IncTwice(int x) => XasmInner.LibInner.Inc(XasmInner.LibInner.Inc(x));
    }
}
