#nullable enable
using System.Globalization;
using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// Reads a hand-written <c>[assembly: GpuDeviceExport(...)]</c> test case and fills in the format version and prelude
/// hash markers (<c>FORMAT_VERSION</c>, <c>PRELUDE_HASH</c>) from <see cref="DeviceExportRecord"/> unless a test passes
/// another value to check version or prelude mismatches.
/// </summary>
internal static class HandWrittenExportRecords
{
    private const string FormatVersionMarker = "FORMAT_VERSION";
    private const string PreludeHashMarker = "PRELUDE_HASH";

    public static string Read(string testCase, string? preludeHash = null, int? formatVersion = null)
    {
        var raw = RoslynTestUtility.ReadTestCase(testCase);
        if (!raw.Contains(FormatVersionMarker, StringComparison.Ordinal) ||
            !raw.Contains(PreludeHashMarker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The hand-written export record test case '{testCase}' is missing '{FormatVersionMarker}' or " +
                $"'{PreludeHashMarker}'.");
        }

        var version = (formatVersion ?? DeviceExportRecord.CurrentFormatVersion)
            .ToString(CultureInfo.InvariantCulture);
        var hash = preludeHash ?? DeviceExportRecord.CurrentPreludeHash;
        var replaced = raw
            .Replace(FormatVersionMarker, version, StringComparison.Ordinal)
            .Replace(PreludeHashMarker, hash, StringComparison.Ordinal);
        if (replaced.Contains(FormatVersionMarker, StringComparison.Ordinal) ||
            replaced.Contains(PreludeHashMarker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The hand-written export record test case '{testCase}' still contains '{FormatVersionMarker}' or " +
                $"'{PreludeHashMarker}'.");
        }

        return replaced;
    }
}
