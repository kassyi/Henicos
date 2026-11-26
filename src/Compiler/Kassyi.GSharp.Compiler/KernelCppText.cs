using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler;

/// <summary>The C++ text of a kernel in the native module: its state machine, or its native stack form.</summary>
internal readonly record struct KernelCppText(
    string Source,
    EquatableArray<string> Includes,
    EquatableArray<ErrorSiteEntry> ErrorSites,
    StateMachineFallback NativeFallback = default);
