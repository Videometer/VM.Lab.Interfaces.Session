using VM.Lab.Interfaces.Session;

namespace VM.Lab.Session.SerialSessionController.TestGui;

public class DummySessionControllerListener : ISessionControllerListener
{
    public void Capture(string id, string initials, string comments, bool suffixByTimestamp)
    {
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