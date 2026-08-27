using VM.Lab.Interfaces.Session;

namespace VM.Lab.Session.ExternalSerialSessionController.TestGui;

public class DummyExternalSessionControllerListener : IExternalSessionControllerListener
{
    public event EventHandler CaptureCalled;
    
    public void Capture(string id, string initials, string comments)
    {
        CaptureCalled?.Invoke(this, EventArgs.Empty);
    }

    public void New()
    {
        Console.WriteLine($"{nameof(New)} was called.");
    }

    public void Stop()
    {
        Console.WriteLine($"{nameof(Stop)} was called.");
    }

    public void LoadLightSetup(string lightSetupName)
    {
        Console.WriteLine($"{nameof(LoadLightSetup)} was called.");
    }

    public void SaveLightSetup(string lightSetupName)
    {
        Console.WriteLine($"{nameof(SaveLightSetup)} was called.");
    }

    public void DoAutoLight()
    {
        Console.WriteLine($"{nameof(DoAutoLight)} was called.");
    }

    public void AcknowledgeAdjustingLightSetupFailed()
    {
        Console.WriteLine($"{nameof(AcknowledgeAdjustingLightSetupFailed)} was called.");
    }
}