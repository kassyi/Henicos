using System.Text;

namespace Kassyi.GSharp.Stdlib.Math.IO;

/// <summary>
/// A memory-efficient TextWriter that only keeps track of the first N (Head) and last N (Tail) characters.
/// Intermediate characters are discarded to prevent OutOfMemory exceptions on massive outputs (e.g. 100M+ digits).
/// </summary>
public class HeadTailTrackerWriter(int headCapacity = 50, int tailCapacity = 50) : TextWriter
{
    private readonly StringBuilder _head = new(headCapacity);
    private readonly char[] _tailBuffer = new char[tailCapacity];

    private int _tailIndex;
    private bool _tailFull;
    private long _totalWritten;

    public override Encoding Encoding => Encoding.UTF8;

    public long TotalCharactersWritten => _totalWritten;

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

    public override void Write(char[] buffer, int index, int count)
    {
        for (var i = 0; i < count; i++)
        {
            Write(buffer[index + i]);
        }
    }

    public string GetHead() => _head.ToString();

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

    public override string ToString() => GetPreview();
}
