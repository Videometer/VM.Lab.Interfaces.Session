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

controller.ProvideSphereHeightProvider(new DummySphereHeightProvider());

// Simulate that the session runner window has been opened and we have connected to the device
controller.StateChanged(SessionState.Ready); 

Console.WriteLine("Press any key to exit");
Console.ReadLine();
Console.WriteLine("Im gone");