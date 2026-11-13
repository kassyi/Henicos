using System.Collections.Immutable;

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Decides the parameters of the native launcher export <c>__gsharp_launch_*</c> as the CPU launcher calls it: the type
/// of each kernel parameter and the form of its argument, and the pointer type of the auto result.
/// </summary>
/// <remarks>
/// The C++ launcher has one parameter list, so every C# declaration of it (the delegate of <c>Launch{Kernel}</c>) is
/// written from this plan, and the call passes the arguments in the forms it chose.
/// </remarks>
internal readonly struct CpuNativeArgumentPlan
{
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

    /// <summary>Plans the native arguments of <paramref name="kernel"/>.</summary>
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
    /// The plan with the group of each converted member of a launch whose arguments overlap (the buffer that the native
    /// kernel takes instead of the member's own shadow); the same plan when no argument is such a member.
    /// </summary>
    /// <param name="aliases">The overlapping arguments of the launch.</param>
    public CpuNativeArgumentPlan WithAliases(in LaunchAliasPlan aliases)
    {
        var withGroups = Arguments.ToBuilder();
        for (var i = 0; i < withGroups.Count; i++)
        {
            withGroups[i] = withGroups[i] with { Group = AliasGroupEmitter.CpuBufferPointer(aliases, i) };
        }

        return new CpuNativeArgumentPlan(withGroups.ToImmutable(), ResultTypeName);
    }

    // [WHY] A user struct by value goes as a pointer to the host value, so that the runtime marshaller never
    // converts it (bool, char, the x64 size rules); the native launcher copies it (#511). A struct whose layouts
    // differ goes as a pointer to its native shadow struct.
    private static string GetTypeName(in KernelParameterShape shape)
    {
        switch (shape)
        {
            case { IsArray: true, IsNullableValue: true }:
                return $"global::System.Nullable<{HostTypeNames.ArrayProxy}>*";
            case { IsArray: true }:
                return HostTypeNames.ArrayProxy;
        }

        var typeName = shape is { IsCpuConvertedStruct: true, AotStructPath: { } shadow } ? shadow : shape.TypeName;
        return shape.IsByRef || shape.IsCpuValueStruct ? typeName + "*" : typeName;
    }

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

    // A returned struct whose layouts differ is written into its native shadow array (#704)
    private static string? GetResultTypeName(in KernelHostModel kernel) =>
        kernel.ReturnTypeName is { } returnType
            ? (kernel.CpuReturn.ConvertedElementAotStructPath ?? returnType) + "*"
            : null;
}
