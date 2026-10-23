using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>
/// Assembles the text of the native module (the AOT <c>.g.cu</c> / <c>.g.cpp</c> body and the shared device code) from
/// the transpiled methods. Pure: it reports nothing and touches no files; <see cref="AotArtifactWriter"/> writes the
/// result.
/// </summary>
internal static class NativeModuleAssembler
{
    /// <summary>The prelude that precedes the device code in the dialect's native module.</summary>
    public static string GetPreludeSource(ITranspilerDialect dialect) =>
        dialect.IsCuda ? GSharpPreludeResources.Cuda : GSharpPreludeResources.Cpp;

    /// <summary>
    /// The device functions as forward declarations followed by their bodies. On the CPU dialects a frame embeds the
    /// frames of its calls by value, so a callee's body comes before its caller's.
    /// </summary>
    public static string AssembleDeviceSource(IReadOnlyList<TranspiledDeviceMethodResult> deviceMethods, bool isCuda)
    {
        var deviceSourceBuilder = new StringBuilder();

        // Pass 1: Forward declarations
        foreach (var method in deviceMethods)
        {
            deviceSourceBuilder.AppendLine(method.GetSignature(isCuda));
        }

        // Pass 2: Method bodies. A frame embeds the frames of its calls by value, so callees come first (CPU dialects).
        var methodsByFrame = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < deviceMethods.Count; i++)
        {
            if (deviceMethods[i].FrameName.Length > 0)
            {
                methodsByFrame[deviceMethods[i].FrameName] = i;
            }
        }

        var emitted = new bool[deviceMethods.Count];
        for (var i = 0; i < deviceMethods.Count; i++)
        {
            appendBody(i);
        }

        return deviceSourceBuilder.ToString();

        void appendBody(int index)
        {
            if (emitted[index])
            {
                return;
            }

            emitted[index] = true;
            var method = deviceMethods[index];
            if (!isCuda)
            {
                foreach (var dependency in method.FrameDependencies)
                {
                    if (methodsByFrame.TryGetValue(dependency, out var dependencyIndex))
                    {
                        appendBody(dependencyIndex);
                    }
                }
            }

            deviceSourceBuilder.AppendLine(method.GetBody(isCuda));
        }
    }

    /// <summary>
    /// The <c>#include</c> targets that the kernels and device functions require in the module of one dialect: only what
    /// that dialect's transpilation asked for, not the other dialect's.
    /// </summary>
    public static HashSet<string> CollectIncludes(IReadOnlyList<TranspiledKernelResult> kernelMethods,
        IReadOnlyList<TranspiledDeviceMethodResult> deviceMethods, bool isCuda)
    {
        var requiredIncludes = new HashSet<string>();
        kernelMethods
            .AsStackEnumerable()
            .Do(
                (Includes: requiredIncludes, IsCuda: isCuda),
                static (method, s) => method.GetIncludes(s.IsCuda)
                    .AsStackEnumerable()
                    .AddTo(s.Includes)
            );
        deviceMethods
            .AsStackEnumerable()
            .Do(
                (Includes: requiredIncludes, IsCuda: isCuda),
                static (method, s) => method.GetIncludes(s.IsCuda)
                    .AsStackEnumerable()
                    .AddTo(s.Includes)
            );
        return requiredIncludes;
    }

    /// <summary>
    /// The source that stands for a method that a dialect could not transpile: one <c>#error</c> per reason
    /// (<see cref="TranspileFailure.Format"/>).
    /// </summary>
    public static string FormatTranspileFailures(IReadOnlyList<string> reasons)
    {
        using var writer = new SourceWriter();
        foreach (var reason in reasons)
        {
            writer.Append("#error \"");
            foreach (var c in reason)
            {
                writer.AppendIf(c is '\\' or '"', "\\");
                writer.Append(c);
            }

            writer.AppendLine("\"");
        }

        return writer.ToString();
    }

    /// <summary>The native module body in its fixed order: includes, prelude, device code, kernels.</summary>
    public static string AssembleBody(HashSet<string> requiredIncludes, string preludeSource, string deviceSource,
        string kernelsSource)
    {
        var includesBuilder = new StringBuilder();
        foreach (var inc in requiredIncludes)
        {
            includesBuilder.AppendLine($"#include {inc}");
        }

        return includesBuilder + preludeSource + deviceSource + kernelsSource;
    }
}
