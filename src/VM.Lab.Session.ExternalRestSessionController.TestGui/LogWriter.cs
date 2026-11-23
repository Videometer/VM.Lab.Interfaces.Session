using System.IO;
using System.Text;

namespace VM.Lab.Session.ExternalRestSessionController.TestGui;

/// <summary>Sends what is written to the console to the log shown in the window</summary>
internal sealed class LogWriter : TextWriter
{
    private readonly Action<string> _appendLine;

    public LogWriter(Action<string> appendLine)
    {
        _appendLine = appendLine;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void WriteLine(string? value)
    {
        _appendLine(value ?? string.Empty);
    }

    public override void Write(string? value)
    {
        _appendLine(value ?? string.Empty);
    }
}
