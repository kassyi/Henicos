using System.Text;

namespace Henicos.Cli;

public class TeeTextWriter(TextWriter originalOut, string filePath) : TextWriter
{
    private StreamWriter _fileOut = new(new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read))
    {
        AutoFlush = true
    };

    public override Encoding Encoding => originalOut.Encoding;

    public override void Write(char value)
    {
        originalOut.Write(value);
        _fileOut.Write(value);
    }

    public override void Write(string? value)
    {
        originalOut.Write(value);
        _fileOut.Write(value);
    }

    public override void WriteLine(string? value)
    {
        originalOut.WriteLine(value);
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
