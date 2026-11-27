using Kassyi.GSharp.Compiler.Ir.Building;
using Kassyi.GSharp.Compiler.Ir.Classifying;

namespace Kassyi.GSharp.Compiler.Ir.Bodies;

/// <summary>
/// The dialect-independent front-end of the IR body pipeline: builds, classifies, and scans the body of a method once
/// for all routes.
/// </summary>
internal static class IrBodyFronts
{
    /// <summary>
    /// The classified IR body of <paramref name="method"/> and the facts its scan found, shared by every dialect.
    /// </summary>
    public static IrBodyFront Create(IMethodSymbol method, Compilation compilation, CancellationToken cancellationToken)
    {
        IrMethod? built;
        using (IrBodyProbe.Measure(IrBodyStage.Build))
        {
            built = IrBuilder.TryBuild(method, compilation, cancellationToken);
        }

        if (built is null)
        {
            return new IrBodyFront(method, compilation, null, null);
        }

        IrMethod classified;
        using (IrBodyProbe.Measure(IrBodyStage.Classify))
        {
            classified = IrClassifier.Classify(built, compilation);
        }

        IrBodyScanResult scan;
        using (IrBodyProbe.Measure(IrBodyStage.Scan))
        {
            scan = IrBodyScan.Scan(classified, compilation);
        }

        return new IrBodyFront(method, compilation, classified, scan);
    }
}
