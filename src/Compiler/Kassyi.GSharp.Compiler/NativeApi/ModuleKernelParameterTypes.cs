namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Decides which parameter types a module (kernel) launcher of a YAML <c>moduleName</c> binding can pass.
/// </summary>
/// <remarks>
/// A module launcher passes a kernel argument by the address of a local copy, and stages a one-dimensional array
/// through <c>GpuBuffer&lt;T&gt;</c>. A reference type would reach the kernel as the host address of the reference,
/// and a <c>ref</c> or <c>out</c> argument would never be written back, so the types outside that set are rejected.
/// The check reads the type as written; a class named by the user (or a BCL class other than <c>String</c>,
/// <c>Object</c> and <c>Array</c>) cannot be told from a struct without the compilation.
/// </remarks>
internal static class ModuleKernelParameterTypes
{
    private const string GlobalPrefix = "global::";

    /// <summary>
    /// Gets why the launcher cannot pass a parameter of type <paramref name="type"/>, or <see langword="null"/> when
    /// it can.
    /// </summary>
    /// <param name="type">The C# type as written in the YAML definition, trimmed.</param>
    public static string? GetUnsupportedReason(string type)
    {
        var space = type.IndexOf(' ');
        if (space > 0 && type.AsSpan(0, space) is "ref" or "out" or "in" or "params" or "scoped")
        {
            return "'ref', 'out', 'in', 'scoped' and 'params' parameters are not supported; " +
                   "the launcher passes a copy and never writes the kernel's value back. " +
                   "Pass a one-element array instead.";
        }

        if (type.IndexOf('?') >= 0)
        {
            return "nullable types are not supported. Use the non-nullable type.";
        }

        var elementType = type;
        var bracket = type.IndexOf('[');
        if (bracket >= 0)
        {
            if (!IsOneDimensionalRank(type.AsSpan(bracket)))
            {
                return "only one-dimensional arrays are supported, not multidimensional or jagged arrays. " +
                       "Flatten the data into a one-dimensional array.";
            }

            elementType = type.AsSpan(0, bracket).TrimEnd().ToString();
        }

        if (elementType.StartsWith(GlobalPrefix, StringComparison.Ordinal))
        {
            elementType = elementType[GlobalPrefix.Length..];
        }

        // [WHY] The launcher file starts with 'using System;', so the names without 'System.' are the same types
        return elementType is "string" or "object" or "dynamic" or "String" or "Object" or "Array" or "System.String"
            or "System.Object" or "System.Array"
            ? "reference types are not supported; " +
              "a kernel takes unmanaged values, pointers and one-dimensional arrays of unmanaged elements."
            : null;
    }

    // Whether <rank> is "[]" once its spaces are removed
    private static bool IsOneDimensionalRank(ReadOnlySpan<char> rank)
    {
        const string OneDimensional = "[]";
        var matched = 0;
        foreach (var character in rank)
        {
            if (character == ' ')
            {
                continue;
            }

            if (matched == OneDimensional.Length || character != OneDimensional[matched])
            {
                return false;
            }

            matched++;
        }

        return matched == OneDimensional.Length;
    }
}
