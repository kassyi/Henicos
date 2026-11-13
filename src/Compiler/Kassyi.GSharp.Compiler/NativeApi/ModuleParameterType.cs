namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>A parameter type of a YAML module binding, read once for validation and for the launcher.</summary>
/// <param name="Kind">How the launcher passes the parameter; meaningful when <paramref name="Reason"/> is null.</param>
/// <param name="LaunchSetting">
/// The launch setting the parameter supplies, or <see cref="ModuleLaunchSetting.None"/>.
/// </param>
/// <param name="TypeName">
/// The type without its modifiers and without whitespace inside it (<c>float[ ]</c> becomes <c>float[]</c>).
/// </param>
/// <param name="ElementTypeName">The element type of an array.</param>
/// <param name="Reason">Why the launcher cannot pass the parameter, or <see langword="null"/> when it can.</param>
/// <param name="RefKind">
/// <see cref="RefKind.Ref"/>, <see cref="RefKind.Out"/> or <see cref="RefKind.In"/> (also for <c>ref readonly</c>)
/// for a parameter passed by reference, otherwise <see cref="RefKind.None"/>.
/// </param>
/// <param name="AllowsNull">Whether an array is written nullable (<c>float[]?</c>, <c>float[,]?</c>).</param>
internal readonly record struct ModuleParameterType(
    ModuleKernelParameterKind Kind,
    ModuleLaunchSetting LaunchSetting,
    string TypeName,
    string? ElementTypeName,
    string? Reason,
    RefKind RefKind = RefKind.None,
    bool AllowsNull = false);
