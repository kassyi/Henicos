using System.Text;

namespace Kassyi.GSharp.Stdlib.GpuMath.IO;

/// <summary>
/// A memory-efficient <see cref="TextWriter"/> that records only the first <c>N</c> (head) and last <c>N</c> (tail) characters of streaming text.
/// </summary>
/// <param name="headCapacity">The maximum number of leading characters to retain (defaults to 50).</param>
/// <param name="tailCapacity">The maximum number of trailing characters to retain in a circular buffer (defaults to 50).</param>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Discards intermediate characters to prevent memory exhaustion when formatting and logging massive numbers (e.g. 100M+ digits of Pi/Fermat).
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> Uses fixed internal ring buffers and string builders without unbounded array resizing.
/// </para>
/// </remarks>
public class HeadTailTrackerWriter(int headCapacity = 50, int tailCapacity = 50) : TextWriter
{
    private readonly StringBuilder _head = new(headCapacity);
    private readonly char[] _tailBuffer = new char[tailCapacity];

    private int _tailIndex;
    private bool _tailFull;
    private long _totalWritten;

    /// <inheritdoc/>
    public override Encoding Encoding => Encoding.UTF8;

    /// <summary>
    /// Gets the total number of characters streamed through this writer.
    /// </summary>
    public long TotalCharactersWritten => _totalWritten;

    /// <inheritdoc/>
    public override void Write(char value)
    {
        if (_head.Length < headCapacity)
        {
            _head.Append(value);
        }

        if (tailCapacity > 0)
        {
            _tailBuffer[_tailIndex] = value;
            _tailIndex++;
            if (_tailIndex >= tailCapacity)
            {
                _tailIndex = 0;
                _tailFull = true;
            }
        }

        _totalWritten++;
    }

    /// <inheritdoc/>
    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        foreach (var c in value)
        {
            Write(c);
        }
    }

    /// <inheritdoc/>
    public override void Write(char[] buffer, int index, int count)
    {
        for (var i = 0; i < count; i++)
        {
            Write(buffer[index + i]);
        }
    }

    /// <summary>
    /// Gets the leading characters captured by the head buffer.
    /// </summary>
    /// <returns>A string containing up to <c>headCapacity</c> initial characters.</returns>
    public string GetHead() => _head.ToString();

    /// <summary>
    /// Gets the trailing characters captured by the circular tail buffer.
    /// </summary>
    /// <returns>A string containing up to <c>tailCapacity</c> most recent characters.</returns>
    public string GetTail()
    {
        if (tailCapacity == 0)
        {
            return string.Empty;
        }

        if (!_tailFull)
        {
            return new string(_tailBuffer, 0, _tailIndex);
        }

        var sb = new StringBuilder(tailCapacity);
        for (var i = 0; i < tailCapacity; i++)
        {
            var idx = (_tailIndex + i) % tailCapacity;
            sb.Append(_tailBuffer[idx]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns an abbreviated string like "Head...Tail" for preview display.
    /// </summary>
    /// <param name="separator">The ellipsis separator to insert between head and tail strings. Defaults to <c>"..."</c>.</param>
    /// <returns>An abbreviated preview string.</returns>
    public string GetPreview(string separator = "...")
    {
        if (_totalWritten <= headCapacity + tailCapacity)
        {
            // If total written is small enough, it fits entirely in head or partly in head+tail
            if (_totalWritten <= headCapacity)
            {
                return _head.ToString();
            }

            // Reconstruct exact string if it didn't overflow tail buffer
            var remaining = (int)(_totalWritten - headCapacity);
            var tailStr = GetTail();
            return _head.ToString() + tailStr.Substring(tailStr.Length - remaining);
        }

        return $"{GetHead()}{separator}{GetTail()}";
    }

    /// <inheritdoc/>
    public override string ToString() => GetPreview();
}
