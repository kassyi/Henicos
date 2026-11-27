namespace Kassyi.GSharp.Compiler.Backends;

/// <summary>
/// Whether a method takes an IR route: a test asks for routes in its flow of execution, or a project asks for them
/// through the features <see cref="NativeFeature"/> and <see cref="CudaFeature"/> of its parse options.
/// </summary>
internal static class IrRouteSwitch
{
    /// <summary>The feature of the parse options that asks for the native C++ IR routes.</summary>
    public const string NativeFeature = "gsharp-ir-cpp";

    /// <summary>The feature of the parse options that asks for the CUDA C++ IR route.</summary>
    public const string CudaFeature = "gsharp-ir-cuda";

    private static readonly AsyncLocal<IrRouteRequest> s_requested = new();

    /// <summary>
    /// The IR routes the method of <paramref name="tree"/> takes: the routes a test asked for, else the routes the
    /// project asks for through the parse options of the tree, else none.
    /// </summary>
    public static IrRouteRequest RequestedRoutes(SyntaxTree tree)
    {
        var requested = s_requested.Value;
        if (requested != IrRouteRequest.None)
        {
            return requested;
        }

        if (tree.Options is not CSharpParseOptions options)
        {
            return IrRouteRequest.None;
        }

        var result = IrRouteRequest.None;
        if (options.Features.ContainsKey(NativeFeature))
        {
            result |= IrRouteRequest.Native;
        }

        if (options.Features.ContainsKey(CudaFeature))
        {
            result |= IrRouteRequest.Cuda;
        }

        return result;
    }

    /// <summary>
    /// Whether <paramref name="requested"/> asks for <paramref name="route"/>: the native routes need Native, the CUDA
    /// route needs Cuda.
    /// </summary>
    public static bool Includes(IrRouteRequest requested, IrBodyRoute route) => route switch
    {
        IrBodyRoute.NativePlain or IrBodyRoute.NativeStack => (requested & IrRouteRequest.Native) != 0,
        IrBodyRoute.Cuda => (requested & IrRouteRequest.Cuda) != 0,
        _ => false
    };

    /// <summary>
    /// Asks for the IR routes of <paramref name="routes"/> in this flow of execution until the result is disposed (a
    /// test). Nested requests restore the outer value.
    /// </summary>
    public static IDisposable Request(IrRouteRequest routes = IrRouteRequest.Native)
    {
        var scope = new RequestScope(s_requested.Value);
        s_requested.Value = routes;
        return scope;
    }

    private sealed class RequestScope(IrRouteRequest previous) : IDisposable
    {
        public void Dispose() => s_requested.Value = previous;
    }
}
