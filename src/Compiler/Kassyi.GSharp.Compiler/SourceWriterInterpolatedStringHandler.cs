// StringBuilder is banned in the code that emits source so that it goes through this writer; this is the one writer that owns one.
#pragma warning disable RS0030

namespace Kassyi.GSharp.Compiler;

#pragma warning disable IDE0060 // Remove unused parameter
#pragma warning disable IDE0051 // Remove unused private members
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedParameter.Local
/// <summary>The handler that enables zero-allocation string interpolation using <see cref="SourceWriter"/>.</summary>
[InterpolatedStringHandler]
public readonly ref struct SourceWriterInterpolatedStringHandler
{
    private readonly StringBuilder? _builder;

    public SourceWriterInterpolatedStringHandler(int literalLength, int formattedCount, SourceWriter writer)
    {
        _builder = writer.GetBuilder();
    }

    public SourceWriterInterpolatedStringHandler(int literalLength, int formattedCount, SourceWriter writer,
        bool condition, out bool isHandlerEnabled)
    {
        if (condition)
        {
            _builder = writer.GetBuilder();
            isHandlerEnabled = true;
        }
        else
        {
            _builder = null;
            isHandlerEnabled = false;
        }
    }

    public void AppendLiteral(string s) => _builder?.Append(s);

    public void AppendFormatted(string? s)
    {
        if (s is not null)
        {
            _builder?.Append(s);
        }
    }

    // Numbers become source text, so they never follow the build host's culture (sv-SE writes U+2212 for '-', de-DE a
    // decimal comma)
    public void AppendFormatted<T>(T t)
    {
        if (t is IFormattable formattable)
        {
            _builder?.Append(formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture));
            return;
        }

        _builder?.Append(t);
    }

    public void AppendFormatted(int t) => _builder?.Append(t.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public void AppendFormatted(uint t) => _builder?.Append(t.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public void AppendFormatted(long t) => _builder?.Append(t.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public void AppendFormatted(ulong t) => _builder?.Append(t.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public void AppendFormatted(short t) => _builder?.Append(t.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public void AppendFormatted(ushort t) => _builder?.Append(t.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public void AppendFormatted(byte t) => _builder?.Append(t.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public void AppendFormatted(sbyte t) => _builder?.Append(t.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public void AppendFormatted(float t) => _builder?.Append(t.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public void AppendFormatted(double t) => _builder?.Append(t.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public void AppendFormatted(decimal t) => _builder?.Append(t.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public void AppendFormatted(bool t) => _builder?.Append(t);
    public void AppendFormatted(char t) => _builder?.Append(t);

    public void AppendFormatted<T>(T? t, string format) where T : IFormattable =>
        _builder?.Append(t?.ToString(format, System.Globalization.CultureInfo.InvariantCulture));
}
#pragma warning restore IDE0051
#pragma warning restore IDE0060
