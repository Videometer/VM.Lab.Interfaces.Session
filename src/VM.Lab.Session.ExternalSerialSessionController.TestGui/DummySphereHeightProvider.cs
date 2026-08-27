using VM.Lab.Interfaces.Session;

namespace VM.Lab.Session.ExternalSerialSessionController.TestGui;

public class DummySphereHeightProvider : ISphereHeightProvider
{
    public int GetSphereHeight() => 85;
}