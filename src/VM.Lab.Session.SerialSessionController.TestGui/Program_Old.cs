using VM.Lab.Interfaces.Session;
using VM.Lab.Session.SerialSessionController;
using VM.Lab.Session.SerialSessionController.TestGui;

Console.WriteLine("This program simulated the VideometerLab Session application");
Console.WriteLine("This program is used to send commands corresponding to the actions that happen when the user clicks buttons in the session GUI.");

bool firstCapture = true;
var listener = new DummySessionControllerListener_Old();
var controller = new SerialSessionController_Old(listener);
listener.CaptureCalled += (_, _) => 
{
    CaptureCalled();
};

// Simulate that the session window have been opened
controller.StateChanged(SessionState_Old.NONE, SessionState_Old.IDLE_SINGLE_FRAME); 

while (true)
{
    Console.WriteLine("Enter command");
    var input = Console.ReadLine();
    Console.WriteLine($"Input was: {input}");
    switch (input)
    {
        case "capture":
            CaptureCalled();
            break;
        case "new":
            firstCapture = true;
            controller.StateChanged(SessionState_Old.WAIT_NEXT_SINGLE_FRAME, SessionState_Old.IDLE_SINGLE_FRAME);
            break;
        case "exit":
            return;
        default:
            Console.WriteLine($"Unknown input: {input}");
            break;
    }
}

void CaptureCalled()
{
    if (firstCapture)
    {
        firstCapture = false;
        controller.StateChanged(SessionState_Old.NONE, SessionState_Old.IDLE_SINGLE_FRAME);
    }
    controller.StateChanged(SessionState_Old.IDLE_SINGLE_FRAME, SessionState_Old.CAPTURE_SINGLE_FRAME);
    controller.StateChanged(SessionState_Old.CAPTURE_SINGLE_FRAME, SessionState_Old.WAIT_NEXT_SINGLE_FRAME);
}

