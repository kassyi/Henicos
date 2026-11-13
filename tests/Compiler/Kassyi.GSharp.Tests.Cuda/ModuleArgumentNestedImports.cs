using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// A kernel of <c>Native/module_arguments.cu</c> imported with <c>[CudaImport]</c> into a class nested in a generic
/// struct, with parameters named with keywords: the generated part repeats the containing types and the escaped names
/// (<see cref="ModuleArgumentTests"/>).
/// </summary>
/// <typeparam name="T">An unused type parameter of the container.</typeparam>
public partial struct ModuleArgumentNestedImports<T>
{
    public static partial class Keywords
    {
        [CudaImport("module_arguments.gfatbin", EntryPoint = "UpdateByReference")]
        public static partial void Update(ref int @ref, in int @in, out int @out);
    }
}
