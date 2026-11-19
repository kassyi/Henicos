using System.Collections.Immutable;

namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>The split native module files: an optional shared header (CPU when B &gt; 1) and B unit sources.</summary>
internal sealed record NativeModuleFiles(NativeModuleFile? Header, ImmutableArray<NativeModuleFile> Units);
