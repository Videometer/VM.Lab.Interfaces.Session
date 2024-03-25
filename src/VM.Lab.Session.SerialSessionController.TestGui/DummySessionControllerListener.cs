using VM.Lab.Interfaces.Session;

namespace VM.Lab.Session.SerialSessionController.TestGui;

public class DummySessionControllerListener : ISessionControllerListener
{
    public event EventHandler CaptureCalled;
    
    public void Capture(string id, string initials, string comments, bool suffixByTimestamp)
    {
        CaptureCalled?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
    }

    public void Finish()
    {
    }

    public void New()
    {
    }
}