namespace Kassyi.GSharp.Compiler.Semantics;

/// <summary>
/// Decides whether a method symbol is emitted by G# as a global C++ device function rather than mapped by a dialect.
/// </summary>
internal static class DeviceFunctionTargets
{
    /// <summary>
    /// Whether <paramref name="method"/> is a <c>[GpuDeviceFunction]</c> that G# emits as a global C++ function, rather
    /// than an intrinsic that the dialect maps to a built-in.
    /// </summary>
    public static bool IsEmittedDeviceFunction(IMethodSymbol method, ITranspilerDialect dialect) =>
        method is { MethodKind: MethodKind.Ordinary } &&
        (method.IsStatic || method.ContainingType is { IsValueType: true }) &&
        method.OriginalDefinition.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
        !dialect.TryMapIntrinsic(method, out _);
}
