using System.IO;
using System.Text;

namespace Henicos;

public class TeeTextWriter : TextWriter
{
    private TextWriter _originalOut;
    private StreamWriter _fileOut;

    public TeeTextWriter(TextWriter originalOut, string filePath)
    {
        _originalOut = originalOut;
        _fileOut = new StreamWriter(new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read));
        _fileOut.AutoFlush = true;
    }

    public override Encoding Encoding => _originalOut.Encoding;

    public override void Write(char value)
    {
        _originalOut.Write(value);
        _fileOut.Write(value);
    }

    public override void Write(string? value)
    {
        _originalOut.Write(value);
        _fileOut.Write(value);
    }

    public override void WriteLine(string? value)
    {
        _originalOut.WriteLine(value);
        _fileOut.WriteLine(value);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fileOut.Dispose();
        }
        base.Dispose(disposing);
    }
}
