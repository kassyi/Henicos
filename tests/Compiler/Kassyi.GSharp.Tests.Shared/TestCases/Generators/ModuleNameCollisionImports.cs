using Kassyi.GSharp.Core;

// Module names that differ only in characters a C# identifier cannot hold: each module gets its own fatbin field
namespace ModuleNameCollision
{
    public static partial class Imports
    {
        [CudaImport("a-b", EntryPoint = "Fill")]
        public static partial void FillDash(int[] data);

        [CudaImport("a_b", EntryPoint = "Fill")]
        public static partial void FillUnderscore(int[] data);

        [CudaImport("a.b", EntryPoint = "Fill")]
        public static partial void FillDot(int[] data);

        [CudaImport("a{b}", EntryPoint = "Fill")]
        public static partial void FillBraces(int[] data);

        [CudaImport("a_b_", EntryPoint = "Fill")]
        public static partial void FillTrailing(int[] data);
    }
}
