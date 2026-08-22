// ReSharper disable UnusedMember.Global
#pragma warning disable RS2008 // Enable analyzer release tracking

using Microsoft.CodeAnalysis;

namespace Kassyi.Generators.GSharp.Diagnostics;

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

    public static readonly DiagnosticDescriptor ExcessiveSharedMemoryWarning = new(
        id: "GSH1001",
        title: "Excessive static shared memory usage",
        messageFormat: "The kernel allocates {0} bytes of static shared memory, which exceeds the recommended limit of {1} bytes. This may cause kernel launch failure or reduce occupancy.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
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
}
