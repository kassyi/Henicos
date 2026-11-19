namespace Kassyi.GSharp.Compiler.Models;

internal static class NativeBarrierModels
{
    public const string Property = "build_property.GSharpNativeBarrierModel";

    public static NativeBarrierModel Read(AnalyzerConfigOptions globalOptions) =>
        globalOptions.TryGetValue(Property, out var value) &&
        value.AsSpan().Trim().Equals("Stack".AsSpan(), StringComparison.OrdinalIgnoreCase)
            ? NativeBarrierModel.Stack
            : NativeBarrierModel.StateMachine;
}
