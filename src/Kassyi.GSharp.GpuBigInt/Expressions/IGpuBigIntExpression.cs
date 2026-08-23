
namespace Kassyi.GSharp.GpuBigInt.Expressions;
using Kassyi.GSharp.GpuBigInt.Kernels;
using Kassyi.GSharp.GpuBigInt.Contexts;
using Kassyi.GSharp.GpuBigInt.Interop;

public interface IGpuBigIntExpression
{
    int NumWords
    {
        get;
    }

    // Evaluates the AST node. 
    // If targetResult is provided, it tries to write directly to it.
    // If it's a leaf node (GpuBigInteger), it might just return itself if targetResult is null.
    GpuBigInteger Evaluate(GpuContext context, GpuBigInteger? targetResult = null);
}

