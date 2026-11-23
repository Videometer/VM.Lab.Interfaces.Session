using VM.Lab.Interfaces.Session;

namespace VM.Lab.Session.ExternalRestSessionController.TestGui;

public class DummySphereHeightProvider : ISphereHeightProvider
{
    public int GetSphereHeight() => 85;
}
