using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

// Module names that differ only in a character that is not a letter or a digit, in the class that a YAML binding of
// a.gfatbin also extends (NativeApiSharedModuleClassTests): each module gets a field of its own
namespace My.Native;

public static partial class Dotted
{
    [CudaImport("a.gfatbin")]
    public static partial void FromDotted([In] float[] input);

    [CudaImport("a_gfatbin")]
    public static partial void FromUnderscored([In] float[] input);
}
