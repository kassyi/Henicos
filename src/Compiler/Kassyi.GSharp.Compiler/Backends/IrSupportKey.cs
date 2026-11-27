using Kassyi.GSharp.Compiler.Ir;

namespace Kassyi.GSharp.Compiler.Backends;

/// <summary>
/// A node a backend writer cannot spell on a route, counted by the route, the kind of node and its shape; written
/// <c>Route:Kind/Shape</c> (<c>NativePlain:IrCall/DeviceFunction</c>), a text the expected files of the tests keep.
/// </summary>
/// <param name="Route">The route that cannot spell the node.</param>
/// <param name="Kind">The type name of the node.</param>
/// <param name="Shape">Why the writer cannot spell it, or the shape of its decision.</param>
internal readonly record struct IrSupportKey(IrBodyRoute Route, string Kind, string Shape)
{
    /// <summary>
    /// The key of <paramref name="node"/>, which <paramref name="support"/> says <paramref name="route"/> cannot spell.
    /// </summary>
    public static IrSupportKey Of(IrBodyRoute route, IrNode node, IrSupport support) =>
        new(route, node.GetType().Name, support.Shape);

    /// <summary>The key as <c>Route:Kind/Shape</c>.</summary>
    public override string ToString() => RouteName(Route) + ":" + Kind + "/" + Shape;

    private static string RouteName(IrBodyRoute route) => route switch
    {
        IrBodyRoute.Cuda => nameof(IrBodyRoute.Cuda),
        IrBodyRoute.NativeStack => nameof(IrBodyRoute.NativeStack),
        IrBodyRoute.NativePlain => nameof(IrBodyRoute.NativePlain),
        IrBodyRoute.Managed => nameof(IrBodyRoute.Managed),
        _ => throw new InvalidOperationException("A key belongs to one route.")
    };
}
