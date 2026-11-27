using Kassyi.GSharp.Compiler.Aot;
using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The translated native stack C++ form of one suspending device function, shared between its export record
/// (<see cref="DeviceFunctionRecords"/>) and its assembly's native module
/// (<see cref="NativeStackTranslator.ForDeviceFunction"/>).
/// </summary>
internal readonly record struct DeviceStackForm(
    bool HasForm,
    string Signature,
    string Source,
    string ModuleBody,
    HashSet<string>? Includes,
    StructSourceCollector? Structs,
    EquatableArray<ErrorSiteEntry> ErrorSites,
    bool Failed,
    StateMachineFallback NativeFallback = default);
