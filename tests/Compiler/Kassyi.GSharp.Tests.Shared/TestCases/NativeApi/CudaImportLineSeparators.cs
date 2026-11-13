using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

// A module name and an entry point with characters that a C# string literal cannot hold unescaped: U+2028, U+2029 and
// U+0085 end a line in C# source (NativeApiEscapingTests)
namespace My.Native;

public static partial class Separated
{
    [CudaImport("m\u2028odule.gfatbin", EntryPoint = "k\u2029er\u0085nel")]
    public static partial void Run([In] float[] input);
}
