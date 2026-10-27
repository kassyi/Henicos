using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace StructLayoutSizeDiagnosticsTest
{
    // C# accepts StructLayout.Size on an inline array, but the runtime does not load the type (TypeLoadException)
    [InlineArray(4)]
    [StructLayout(LayoutKind.Sequential, Size = 32)]
    public struct SizedInline { private int _element0; }

    [InlineArray(4)]
    public struct PlainInline { private int _element0; }

    // GSH0078 at the sized inline array; the plain one reports nothing
    public static partial class StructLayoutSizeKernels
    {
        [GpuKernel]
        public static void SizedInlineKernel(SizedInline s, int[] r) { r[0] = s[1]; }

        [GpuKernel]
        public static void PlainInlineKernel(PlainInline s, int[] r) { r[0] = s[1]; }
    }
}
