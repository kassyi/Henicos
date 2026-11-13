using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

// [CudaImport] methods in the class that a YAML module binding also extends (NativeApiSharedModuleClassTests): the
// methods of the YAML module share its module field; another module, and another class of the same module, keep
// their own
namespace My.Native;

public static partial class Shared
{
    [CudaImport("shared.gfatbin")]
    public static partial void Imported([In] float[] input, float factor);

    [CudaImport("other.gfatbin")]
    public static partial void OtherModule([In] float[] input);
}

public static partial class Separate
{
    [CudaImport("shared.gfatbin")]
    public static partial void Imported([In] float[] input);
}
