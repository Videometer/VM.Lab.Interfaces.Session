using VM.Lab.Interfaces.Session;

namespace VM.Lab.Session.ExternalSerialSessionController.TestGui;

public class DummyExternalSessionControllerListener : IExternalSessionControllerListener
{
    public event EventHandler CaptureCalled;
    
    public event EventHandler LightSetupAdjusted;
    
    public void Capture(string id, string initials, string comments)
    {
        Console.WriteLine($"{nameof(Capture)} was called.");
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
        LightSetupAdjusted?.Invoke(this, EventArgs.Empty);
    }

    public void SaveLightSetup(string lightSetupName)
    {
        Console.WriteLine($"{nameof(SaveLightSetup)} was called.");
        LightSetupAdjusted?.Invoke(this, EventArgs.Empty);
    }

    public void DoAutoLight()
    {
        Console.WriteLine($"{nameof(DoAutoLight)} was called.");
        LightSetupAdjusted?.Invoke(this, EventArgs.Empty);
    }

    public void AcknowledgeAdjustingLightSetupFailed()
    {
        Console.WriteLine($"{nameof(AcknowledgeAdjustingLightSetupFailed)} was called.");
    }
}