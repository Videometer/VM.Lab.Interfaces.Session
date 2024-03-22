using VM.Lab.Interfaces.Session;
using VM.Lab.Session.SerialSessionController;
using VM.Lab.Session.SerialSessionController.TestGui;

Console.WriteLine("This program simulated the VideometerLab Session application");
Console.WriteLine("This program is used to send commands corresponding to the actions that happen when the user clicks buttons in the session GUI.");

var controller = new SerialSessionController(new DummySessionControllerListener());
bool _firstCapture = true;

while (true)
{
    Console.WriteLine("Enter command");
    var input = Console.ReadLine();
    Console.WriteLine("Input was: {input}");
    switch (input)
    {
        case "capture":
            if (_firstCapture)
            {
                _firstCapture = false;
                controller.StateChanged(SessionState.NONE, SessionState.IDLE_SINGLE_FRAME);
            }

            controller.StateChanged(SessionState.IDLE_SINGLE_FRAME, SessionState.CAPTURE_SINGLE_FRAME);
            controller.StateChanged(SessionState.CAPTURE_SINGLE_FRAME, SessionState.WAIT_NEXT_SINGLE_FRAME);
            break;
        case "new":
            _firstCapture = true;
            controller.StateChanged(SessionState.WAIT_NEXT_SINGLE_FRAME, SessionState.IDLE_SINGLE_FRAME);
            break;
        case "exit":
            return;
        default:
            Console.WriteLine($"Unknown input: {input}");
            break;
    }
}