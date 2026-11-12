using Kassyi.GSharp.Core;
using System.Runtime.InteropServices;

// A [CudaImport] class in a namespace whose name is a keyword: the generated part reopens it as @event.Imports
namespace @event.Imports;

public static partial class KeywordNamespaceInvoker
{
    [CudaImport("keyword_module", EntryPoint = "Scale")]
    [SkipSnapshotConsistency("Generator case")]
    public static partial void Scale([In] float[] input, float factor);
}
