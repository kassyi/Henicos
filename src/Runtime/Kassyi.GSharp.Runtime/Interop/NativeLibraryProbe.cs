using System.Reflection;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// Probes native library candidates on behalf of the <c>DllImportResolver</c> emitted for YAML native API
/// definitions that declare <c>libraryNames</c> or a path-qualified <c>libraryName</c>.
/// </summary>
public static class NativeLibraryProbe
{
    /// <summary>
    /// Loads the first candidate that resolves to a loadable library.
    /// </summary>
    /// <param name="assembly">The assembly issuing the P/Invoke.</param>
    /// <param name="searchPath">The search path requested by the P/Invoke, if any.</param>
    /// <param name="candidates">Candidates in priority order; see <see cref="ResolveCandidatePath"/> for their syntax.</param>
    /// <returns>The library handle, or <see cref="IntPtr.Zero"/> to fall back to the default runtime probing.</returns>
    public static IntPtr Load(Assembly assembly, DllImportSearchPath? searchPath, ReadOnlySpan<string> candidates)
    {
        var baseDirectory = AppContext.BaseDirectory;
        foreach (var candidate in candidates)
        {
            var path = ResolveCandidatePath(candidate, baseDirectory);
            if (Path.IsPathRooted(path)
                    ? NativeLibrary.TryLoad(path, out var handle)
                    : NativeLibrary.TryLoad(path, assembly, searchPath, out handle))
            {
                return handle;
            }
        }

        // Returning zero lets the runtime apply its default probing and raise DllNotFoundException.
        return IntPtr.Zero;
    }

    /// <summary>
    /// Expands <c>%VARIABLE%</c> references and anchors relative paths to <paramref name="baseDirectory"/>.
    /// </summary>
    /// <param name="candidate">
    /// A bare file name (left to the OS search), a relative path such as <c>./native/lib.dll</c>, or an absolute path.
    /// </param>
    /// <param name="baseDirectory">The directory relative paths are resolved against, normally the application base.</param>
    /// <returns>The path to pass to <see cref="NativeLibrary"/>.</returns>
    public static string ResolveCandidatePath(string candidate, string baseDirectory)
    {
        var expanded = Environment.ExpandEnvironmentVariables(candidate);
        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        if (expanded.IndexOfAny(['/', '\\']) < 0)
        {
            return expanded;
        }

        var normalized = string.Create(expanded.Length, expanded, static (span, source) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = source[i] == '\\' ? '/' : source[i];
            }
        });
        return Path.GetFullPath(Path.Combine(baseDirectory, normalized));
    }
}
