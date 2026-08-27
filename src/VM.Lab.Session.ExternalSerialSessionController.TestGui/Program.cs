using VM.Lab.Interfaces.Session;
using VM.Lab.Session.ExternalSerialSessionController.TestGui;
using VM.Lab.Session.SerialSessionController;

Console.WriteLine("This program is used to receive commands from the Python scripts and check that the 'External Serial Session Controller' receives and understands the commands.");

var listener = new DummyExternalSessionControllerListener();
var controller = new ExternalSerialSessionController(listener);
listener.CaptureCalled += (_, _) => 
{
    controller.StateChanged(SessionState.Capturing);
    controller.StateChanged(SessionState.Processing);
    controller.StateChanged(SessionState.Ready);
};

listener.LightSetupAdjusted += (_, _) =>
{
    controller.StateChanged(SessionState.AdjustingLightSetup);

    // Simulate a failure. ProvideErrorMessage is what ends the caller's wait — a failure state does not,
    // because signalling there would race with the reason being reported. The session reports the reason
    // from inside the light-setup work, before returning the result that moves the state, so report it
    // first here too. Leave it out and the caller waits out its whole timeout.

    Task.Run(() =>
    {
        Thread.Sleep(5000);
        controller.ProvideErrorMessage("Simulated light setup adjustment failure");
        controller.StateChanged(SessionState.AdjustingLightSetupFailed);
    });

    // Swap the two lines above for this one to simulate success instead.
    //controller.StateChanged(SessionState.Ready);
};

controller.ProvideSphereHeightProvider(new DummySphereHeightProvider());

// Simulate that the session runner window has been opened and we have connected to the device
controller.StateChanged(SessionState.Ready); 

Console.WriteLine("Press any key to exit");
Console.ReadLine();
Console.WriteLine("Im gone");