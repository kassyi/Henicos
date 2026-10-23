using Kassyi.GSharp.Core;
using System.Runtime.InteropServices;

namespace TestNamespace;

public static partial class TestClass
{
    [CudaImport("my_module", EntryPoint = "MyKernel")]
    [SkipSnapshotConsistency("Generator case")]
    public static partial void MyMethod([In] float[] input, float scalar);
}
