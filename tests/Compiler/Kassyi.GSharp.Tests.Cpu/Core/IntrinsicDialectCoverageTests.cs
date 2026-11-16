#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.NativeApi;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Every native name of the intrinsic YAML files (<c>src/NativeApi</c>) has a definition in each dialect, or the YAML
/// declares that the dialect does not implement it (<c>unsupported</c>, GSH0054): a name that neither the dialect's
/// prelude, the CUDA toolkit nor the C++ standard library defines fails the C++ build of a kernel that calls it, with
/// no location in the user's source (#469, #717).
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class IntrinsicDialectCoverageTests
{
    // The functions that the CUDA toolkit declares for device code (crt/device_functions.h, sm_20_intrinsics.h,
    // sm_32_intrinsics.h, sm_61_intrinsics.h, sm_60_atomic_functions.h, sm_80_rt.h), checked against the headers of
    // CUDA 13.4. The arity is not checked here: a name with a different signature (__sad takes three operands) is
    // wrapped by a prelude function with the signature of the C# method.
    private static readonly HashSet<string> s_cudaBuiltIns =
    [
        "__activemask", "__all_sync", "__any_sync", "__ballot_sync", "__brev", "__brevll", "__byte_perm", "__clz",
        "__clzll", "__cvta_generic_to_shared", "__dp4a", "__funnelshift_l", "__funnelshift_r", "__ldcg",
        "__match_all_sync", "__match_any_sync", "__mul64hi", "__mulhi", "__nanosleep", "__popc", "__popcll",
        "__reduce_add_sync", "__reduce_and_sync", "__reduce_max_sync", "__reduce_min_sync", "__reduce_or_sync",
        "__reduce_xor_sync", "__shfl_down_sync", "__shfl_sync", "__shfl_up_sync", "__shfl_xor_sync", "__stcg",
        "__syncwarp", "__threadfence", "__threadfence_block", "__threadfence_system", "__umul64hi", "atomicAdd",
        "atomicAnd", "atomicCAS", "atomicDec", "atomicExch", "atomicInc", "atomicMax", "atomicMin", "atomicOr",
        "atomicSub", "atomicXor", "clock", "clock64"
    ];

    // The functions of the C++ standard library (<cstdlib>) that both dialects call by their name: the device heap of
    // Gpu.Unsafe is the C heap on the CPU dialect
    private static readonly HashSet<string> s_standardLibrary = ["malloc", "free"];

#pragma warning disable RS0030 // Category 5: Parses C++ prelude source text which has no syntax tree
    private static readonly Regex s_include = new("^[ \\t]*#include[ \\t]+\"(?<path>[^\"]+)\"", RegexOptions.Multiline);
#pragma warning restore RS0030

    private static readonly Lazy<Compilation> s_core = new(static () => RoslynTestUtility.CreateCompilation(""));

    // Every native name is defined by the dialect, by the CUDA toolkit or the C++ standard library, rewritten by the
    // lowering of the dialect, or declared unsupported by it.
    // mutant: src/Compiler/Kassyi.GSharp.Compiler/Resources/Prelude/MathAndBitOps.hpp, `__clz` renamed to `__clz2`
    // input: the CountLeadingZeros row of ptx_intrinsics.yml
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void NativeNames_EveryIntrinsicRow_IsDefinedOrDeclaredUnsupported(string dialect)
    {
        var prelude = ReadPrelude(dialect);
        var missing = new List<string>();
        foreach (var row in ReadIntrinsicRows())
        {
            if (row.Method.UnsupportedDialects.Contains(dialect) ||
                s_standardLibrary.Contains(row.NativeName) ||
                (dialect == "Cuda" && s_cudaBuiltIns.Contains(row.NativeName)) ||
                (dialect == "Cpu" && IsLoweredOnCpu(row)) ||
                Defines(prelude, row.NativeName))
            {
                continue;
            }

            missing.Add($"{row.File}: {row.ClassName}.{row.Method.Name} -> {row.NativeName}");
        }

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    // The managed CPU path runs the C# stub of each intrinsic: every row the CPU dialect implements forwards to a
    // managed member (`managed:`, which the build of Kassyi.GSharp.Core checks exists), or is a warp collective that
    // the lowering replaces with the managed shuffle helpers before the stub is reached. A row without either throws
    // GpuOnlyException in a managed launch with no diagnostic.
    // mutant: `managed: ManagedIntrinsics.PopCount` removed from the __popc row of ptx_intrinsics.yml
    // input: the PopCount row of ptx_intrinsics.yml
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ManagedPath_EveryIntrinsicRow_IsForwardedLoweredOrDeclaredUnsupported()
    {
        var missing = ReadIntrinsicRows()
            .Where(static row => row.Method.ManagedName is null &&
                                 !row.Method.UnsupportedDialects.Contains(ApiMethodDefinition.CpuDialect) &&
                                 !IsLoweredOnCpu(row))
            .Select(static row => $"{row.File}: {row.ClassName}.{row.Method.Name} -> {row.NativeName}")
            .ToList();

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    // A dialect that declares a name unsupported replaces the call with a runtime failure, so its prelude keeps no
    // definition of the name that could be called by mistake.
    // mutant: the CPU stubs of __gsharp_mbarrier_init kept in ThreadAndMemory.hpp
    // input: the MBarrierInit row of ptx_intrinsics.yml
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void NativeNames_DeclaredUnsupported_AreNotDefinedByThatDialect(string dialect)
    {
        var prelude = ReadPrelude(dialect);
        var defined = ReadIntrinsicRows()
            .Where(row => row.Method.UnsupportedDialects.Contains(dialect) && Defines(prelude, row.NativeName))
            .Select(static row => $"{row.File}: {row.ClassName}.{row.Method.Name} -> {row.NativeName}")
            .ToList();

        Assert.True(defined.Count == 0, string.Join(Environment.NewLine, defined));
    }

    // The definition check finds the definition forms of the preludes and nothing that only calls the name
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("__forceinline__ int __popc(uint32_t value) { return 0; }", true)]
    [InlineData("template <typename T>\ninline void __gsharp_cp_async(T* dst,\n    const T* src) {\n}", true)]
    [InlineData("__device__ __forceinline__ unsigned long long __gsharp_x(unsigned int a) {", true)]
    [InlineData("#define __gsharp_x(a) (a)", true)]
    [InlineData("    return __gsharp_x(value) + 1;", false)]
    [InlineData("    unsigned int addr = (unsigned int)__gsharp_x(barrier);", false)]
    [InlineData("    if (__gsharp_x(a)) {", false)]
    [InlineData("inline int __gsharp_x(int a);", false)]
    public void Defines_DefinitionsAndCalls_TellThemApart(string text, bool expected)
    {
        var name = text.Contains("__popc", StringComparison.Ordinal) ? "__popc"
            : text.Contains("__gsharp_cp_async", StringComparison.Ordinal) ? "__gsharp_cp_async"
            : "__gsharp_x";

        Assert.Equal(expected, Defines(text, name));
    }

    // `unsupported` names only the dialects that exist
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Unsupported_UnknownDialect_IsASchemaError()
    {
        const string Yaml = """
            namespace: Kassyi.GSharp.Core
            className: Gpu
            methods:
              - name: Probe
                nativeName: __gsharp_probe
                unsupported: [Metal]
                returnType: void
            """;

        Assert.False(NativeApiYamlReader.TryRead(Yaml, out _, out var error));
        Assert.Contains("'unsupported'", error?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    // A managed implementation runs on the CPU dialect, which the row says does not implement it
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Unsupported_CpuWithManagedMember_IsASchemaError()
    {
        const string Yaml = """
            namespace: Kassyi.GSharp.Core
            className: Gpu
            methods:
              - name: Probe
                nativeName: __gsharp_probe
                managed: ManagedIntrinsics.Probe
                unsupported: [Cpu]
                returnType: void
            """;

        Assert.False(NativeApiYamlReader.TryRead(Yaml, out _, out var error));
        Assert.Contains("'unsupported'", error?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    private readonly record struct IntrinsicRow(string File, string ClassName, ApiMethodDefinition Method)
    {
        public string NativeName => Method.NativeName ?? Method.Name;
    }

    private static IEnumerable<IntrinsicRow> ReadIntrinsicRows()
    {
        foreach (var path in Directory.GetFiles(RepositoryPaths.Combine("src", "NativeApi"), "*.yml").Order())
        {
            Assert.True(NativeApiYamlReader.TryRead(File.ReadAllText(path), out var definition, out var error),
                error?.Message);
            if (definition.BindingKind != NativeApiBindingKind.Intrinsic)
            {
                continue;
            }

            foreach (var method in definition.Methods)
            {
                var className = string.IsNullOrEmpty(method.ClassName) ? definition.ClassName : method.ClassName;
                yield return new IntrinsicRow(Path.GetFileName(path), className, method);
            }
        }
    }

    // The warp collectives split the state machine of the CPU dialect, whose lowering replaces each call with a read of
    // the values the lanes published (StateMachineLowerer), so the CPU prelude never sees their CUDA names
    private static bool IsLoweredOnCpu(IntrinsicRow row)
    {
        if (row.ClassName.Contains('.', StringComparison.Ordinal) ||
            s_core.Value.GetTypeByMetadataName("Kassyi.GSharp.Core." + row.ClassName) is not { } type)
        {
            return false;
        }

        return type.GetMembers(row.Method.Name)
            .OfType<IMethodSymbol>()
            .Any(static method => SyncIntrinsicClassifier.Classify(method).Kind == SyncIntrinsicKind.WarpShuffle);
    }

    // The prelude of the dialect with its #include "..." directives inlined, as the prelude mode of the generator tool
    // writes it
    private static string ReadPrelude(string dialect)
    {
        var resources = RepositoryPaths.Combine("src", "Compiler", "Kassyi.GSharp.Compiler", "Resources");
        var text = new StringBuilder();
        Append(resources, dialect == "Cuda" ? "Prelude.cu" : "Prelude.cpp", text, []);
        return text.ToString();
    }

    private static void Append(string resources, string relativePath, StringBuilder text, HashSet<string> seen)
    {
        if (!seen.Add(relativePath))
        {
            return;
        }

        var content = File.ReadAllText(Path.Combine(resources, relativePath));
        text.AppendLine(content);
#pragma warning disable RS0030 // Category 5: Parses C++ prelude source text which has no syntax tree
        foreach (Match include in s_include.Matches(content))
#pragma warning restore RS0030
        {
            Append(resources, include.Groups["path"].Value, text, seen);
        }
    }

    // A definition of <name>: a function definition, whose parameter list is followed by its body, or a macro. A call,
    // a declaration without a body and a use inside an expression are not.
    private static bool Defines(string prelude, string name)
    {
#pragma warning disable RS0030 // Category 5: Parses C++ prelude source text which has no syntax tree
        var escaped = Regex.Escape(name);
#pragma warning restore RS0030
        var macro = $@"#define[ \t]+{escaped}\b";
        // <return type and specifiers> <name>(<parameters>) <qualifiers> {
        var function = $@"(?!return\b|else\b)[\w:<>,\*& \t]*[ \t\*&]{escaped}[ \t]*\([^;{{}}]*\)[^;{{}}]*\{{";
#pragma warning disable RS0030 // Category 5: Parses C++ prelude source text which has no syntax tree
        return Regex.IsMatch(prelude, $@"^[ \t]*(?:{macro}|{function})", RegexOptions.Multiline);
#pragma warning restore RS0030
    }
}
