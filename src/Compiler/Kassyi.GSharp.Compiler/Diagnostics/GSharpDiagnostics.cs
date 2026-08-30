[assembly: SuppressMessage("ReSharper", "UnusedMember.Global", Scope = "namespaceanddescendants", Target = "Kassyi.GSharp.Compiler.Diagnostics", Justification = "Public API exposed for compiler usage")]
#pragma warning disable RS2008 // Enable analyzer release tracking

namespace Kassyi.GSharp.Compiler.Diagnostics;

/// <summary>Defines Roslyn diagnostic descriptors and error rules for GSharp GPU kernel compilation.</summary>
public static class GSharpDiagnostics
{
    private const string Category = "GSharpTranspiler";

    public static readonly DiagnosticDescriptor ObjectCreationNotSupported = new(
        id: "GSH0001",
        title: "Object creation not supported in GPU kernels",
        messageFormat: "Object creation ('new') is not supported in GPU kernels. Only struct instantiation and SharedMemory<T> are allowed.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StringNotSupported = new(
        id: "GSH0002",
        title: "String usage not supported in GPU kernels",
        messageFormat: "The string type is not supported in GPU kernels",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedSyntax = new(
        id: "GSH0003",
        title: "Unsupported syntax in GPU kernel",
        messageFormat: "The syntax '{0}' is not supported in GSharp GPU kernels",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor KernelReturnMustBeVoid = new(
        id: "GSH0004",
        title: "GpuKernel must return void",
        messageFormat: "Method '{0}' is marked with [GpuKernel] but returns '{1}'. GpuKernel methods must return void.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor LargeStructByValueWarning = new(
        id: "GSH1002",
        title: "Large struct passed by value",
        messageFormat: "The parameter '{0}' is a struct of size {1} bytes. Passing large structs by value to a GPU kernel can consume excessive constant memory. Consider passing by reference (ref) or using an array pointer.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GpuBigIntExprDirectAssignment = new(
        id: "GSH0005",
        title: "GpuBigIntExpr must be assigned via .Value",
        messageFormat: "Direct assignment or local declaration of GpuBigIntExpr is not supported. Use 'gpuResult.Value = expr' to evaluate the expression directly into the target buffer.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InlinePtxArgumentMustBeStringLiteral = new(
        id: "GSH0006",
        title: "InlinePtx argument must be a string literal",
        messageFormat: "The argument to Gpu.InlinePtx must be a string literal or an interpolated string literal",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ForeachNotSupported = new(
        id: "GSH0007",
        title: "Foreach is not allowed in GPU kernels",
        messageFormat: "The GPU method '{0}' uses a foreach loop. Use a standard for loop instead to avoid IEnumerator allocation overhead.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor LinqNotSupported = new(
        id: "GSH0008",
        title: "LINQ is not allowed in GPU kernels",
        messageFormat: "The GPU method '{0}' uses LINQ query expressions, which are not supported on the GPU",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ImplicitVarNeedsSuffix = new(
        id: "GSH0009",
        title: "Implicit var with literal needs suffix",
        messageFormat: "The variable '{0}' is implicitly typed (var) and assigned a numeric literal without a suffix. Use a suffix (like 'U' or 'UL') or explicitly type it as 'uint' or 'ulong'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PreludeMissing = new(
        id: "GSH0010",
        title: "GSharp Prelude missing",
        messageFormat: "Could not find exactly one embedded resource ending with 'Prelude.cu' in {0}. Found {1}.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MacroLambdaRequiresParameter = new(
        id: "GSH0011",
        title: "Macro lambda requires a parameter",
        messageFormat: "{0} lambda must declare a parameter for the loop variable",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MacroArgumentMustBeConstant = new(
        id: "GSH0012",
        title: "Macro arguments must be compile-time constants",
        messageFormat: "The argument '{0}' of macro '{1}' must be a compile-time constant",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FundamentalOwnershipError = new(
        id: "MEM0001",
        title: "Fundamental Ownership Error",
        messageFormat: "GpuContext memory ownership violation on '{0}': {1}",
        category: "GSharpMemorySafety",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ExcessiveSharedMemoryWarning = new(
        id: "GSH0013",
        title: "Excessive static shared memory usage",
        messageFormat: "The kernel allocates {0} bytes of static shared memory, which exceeds the recommended limit of {1} bytes. This may cause kernel launch failure or reduce occupancy.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DynamicDeviceAllocationWarning = new(
        id: "GSH0014",
        title: "Dynamic Device-side Allocation Warning",
        messageFormat: "Manual unmanaged memory allocation detected. Ensure corresponding Free is called.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
