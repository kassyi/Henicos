using System.Collections.Immutable;

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Decides the parameters of the native launcher export <c>__gsharp_launch_*</c> as a CPU launcher calls it: the type
/// of each kernel parameter and the form of its argument, and the pointer type of the auto result.
/// </summary>
/// <remarks>
/// The C++ launcher has one parameter list, so every C# declaration of it (the delegate of <c>Launch{Kernel}</c>, the
/// function pointer of <c>Launch{Kernel}Ptr</c>) is written from this plan, and the call passes the arguments in the
/// forms it chose.
/// </remarks>
internal readonly struct CpuNativeArgumentPlan
{
    private const string BooleanTypeName = "global::System.Boolean";
    private const string CharTypeName = "global::System.Char";

    private CpuNativeArgumentPlan(ImmutableArray<CpuNativeArgument> arguments, string? resultTypeName)
    {
        Arguments = arguments;
        ResultTypeName = resultTypeName;
    }

    /// <summary>One argument per kernel parameter, in parameter order.</summary>
    public ImmutableArray<CpuNativeArgument> Arguments { get; }

    /// <summary>
    /// The pointer type of the auto result parameter, which follows the kernel parameters, or <see langword="null"/>
    /// when the kernel returns nothing.
    /// </summary>
    public string? ResultTypeName { get; }

    /// <summary>Plans the native arguments of <c>Launch{Kernel}</c> of <paramref name="kernel"/>.</summary>
    public static CpuNativeArgumentPlan Create(in KernelHostModel kernel)
    {
        var arguments = ImmutableArray.CreateBuilder<CpuNativeArgument>(kernel.Parameters.Length);
        foreach (var shape in kernel.Parameters)
        {
            arguments.Add(new CpuNativeArgument(GetTypeName(shape), GetForm(shape)));
        }

        return new CpuNativeArgumentPlan(arguments.MoveToImmutable(), GetResultTypeName(kernel));
    }

    /// <summary>
    /// Plans the native arguments of the <c>Launch{Kernel}Ptr</c> launcher of <paramref name="kernel"/>, which takes
    /// each parameter in the form <see cref="PointerLaunchPlan.Classify"/> chose.
    /// </summary>
    public static CpuNativeArgumentPlan CreateForPointerLauncher(in KernelHostModel kernel)
    {
        var arguments = ImmutableArray.CreateBuilder<CpuNativeArgument>(kernel.Parameters.Length);
        foreach (var shape in kernel.Parameters)
        {
            arguments.Add(GetPointerArgument(shape));
        }

        return new CpuNativeArgumentPlan(arguments.MoveToImmutable(), GetResultTypeName(kernel));
    }

    // [WHY] A user struct by value goes as a pointer to the host value, so that the runtime marshaller never
    // converts it (bool, char, the x64 size rules); the native launcher copies it (#511). A struct whose layouts
    // differ goes as a pointer to its native shadow struct.
    private static string GetTypeName(in KernelParameterShape shape)
    {
        switch (shape)
        {
            case { IsArray: true, IsNullableValue: true }:
                return NullableProxyPointer;
            case { IsArray: true }:
                return HostTypeNames.ArrayProxy;
        }

        var typeName = shape is { IsCpuConvertedStruct: true, AotStructPath: { } shadow } ? shadow : shape.TypeName;
        return shape.IsByRef || shape.IsCpuValueStruct ? typeName + "*" : typeName;
    }

    private static string NullableProxyPointer => $"global::System.Nullable<{HostTypeNames.ArrayProxy}>*";

    // [WHY] A converted struct is tested before by-reference: ref MyStruct goes through the native shadow as well
    private static CpuNativeArgumentForm GetForm(in KernelParameterShape shape) => shape switch
    {
        { IsArray: true, IsNullableValue: true } => CpuNativeArgumentForm.NullableProxyAddress,
        { IsArray: true } => CpuNativeArgumentForm.ArrayProxy,
        { IsCpuConvertedStruct: true } => CpuNativeArgumentForm.ShadowAddress,
        { IsByRef: true } => CpuNativeArgumentForm.PinnedAddress,
        { IsCpuValueStruct: true } => CpuNativeArgumentForm.ValueAddress,
        _ => CpuNativeArgumentForm.Value
    };

    // The caller's proxies are copied without their reserved Flags; a ref / out is the caller's IntPtr cast to the
    // pointer type Launch{Kernel} passes; a by-value struct is the shadow struct parameter, passed by address (#511)
    private static CpuNativeArgument GetPointerArgument(in KernelParameterShape shape) =>
        PointerLaunchPlan.Classify(shape) switch
        {
            PointerArgumentKind.Array => new(HostTypeNames.ArrayProxy, CpuNativeArgumentForm.ProxyCopy),
            PointerArgumentKind.NullableArray => new(NullableProxyPointer, CpuNativeArgumentForm.NullableProxyAddress),
            PointerArgumentKind.Reference => new(GetTypeName(shape), CpuNativeArgumentForm.PointerCast),
            PointerArgumentKind.ShadowStruct => new(GetShadowPath(shape) + "*", CpuNativeArgumentForm.ValueAddress),
            _ => GetScalarArgument(shape)
        };

    // [WHY] An unmanaged function pointer marshals bool as a 4-byte BOOL and char as one ANSI byte, so they go in the
    // widths of the C++ bool and char16_t parameters (the delegate of Launch{Kernel} passes char as UTF-16 through
    // CharSet.Unicode)
    private static CpuNativeArgument GetScalarArgument(in KernelParameterShape shape) => shape.TypeName switch
    {
        BooleanTypeName => new("byte", CpuNativeArgumentForm.BooleanByte),
        CharTypeName => new("ushort", CpuNativeArgumentForm.CharCode),
        _ => new(shape.TypeName, CpuNativeArgumentForm.Value)
    };

    private static string GetShadowPath(in KernelParameterShape shape) =>
        shape.AotStructPath ??
        throw new InvalidOperationException($"The struct parameter '{shape.Name}' has no native shadow struct.");

    // A returned struct whose layouts differ is written into its native shadow array (#704)
    private static string? GetResultTypeName(in KernelHostModel kernel) =>
        kernel.ReturnTypeName is { } returnType
            ? (kernel.CpuReturn.ConvertedElementAotStructPath ?? returnType) + "*"
            : null;
}
