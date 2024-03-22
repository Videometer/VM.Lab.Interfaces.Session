using System.IO.Ports;
using VM.Lab.Interfaces.Session;

namespace VM.Lab.Session.SerialSessionController;

public class SerialSessionController : SessionController
{
    private readonly SerialPort _serialPort;
    private SessionState _state;
    private const char Separator = ';';
    private const string CaptureKeyWord = "Capture";
    private const string PauseKeyWord = "Pause";
    private const string FinishKeyWord = "Finish";
    private const string NewKeyWord = "New";
    private const string CheckConnectionKeyWord = "CheckConnection";
    private const string GetStateKeyWord = "GetState";
    

    // TODO: Get "sphere position" and "Analysis OK" / Is the read line shown?
    
    private readonly string[] _keyWords =
    {
        CaptureKeyWord,
        PauseKeyWord,
        FinishKeyWord,
        NewKeyWord,
        CheckConnectionKeyWord,
        GetStateKeyWord
    };
    
    
    public SerialSessionController(ISessionControllerListener listener) : base(listener)
    {
        _serialPort = new SerialPort("COM1", 9600, Parity.None, 8, StopBits.One);
        _serialPort.DataReceived += SerialPort_DataReceived;
        _serialPort.Open();
    }

    public override void StateChanged(SessionState previousState, SessionState newState)
    {
        Console.WriteLine($"In {nameof(SerialSessionController)}.{nameof(StateChanged)} from {previousState} to {newState}");
        _state = newState;


    }

    private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        string data = _serialPort.ReadLine();
        Console.WriteLine($"{nameof(SerialSessionController)} received: " + data);

        var parts = data.Split(Separator);
        // TODO: Add handling of data that do not contains a ";"

        int expectedParts;
        switch (parts[0])
        {
            case CaptureKeyWord:
                expectedParts = 4;
                break;
            case PauseKeyWord:
            case FinishKeyWord:
            case NewKeyWord:
            case CheckConnectionKeyWord:
                expectedParts = 1;
                break;
            default:
                throw new ArgumentException(
                    $"The arguments passed to the {nameof(SerialSessionController)} are invalid. " +
                    $"The first word must be either {string.Join(", ", _keyWords)}. " +
                    $"Received: {data}");
        }

        CheckNumberOfArguments(parts.Length, expectedParts, data);
       
        switch (parts[0])
        {
            case CaptureKeyWord:
                bool suffixByTimestamp = parts[3] == "True";
                _listener.Capture(parts[0], parts[1], parts[2], suffixByTimestamp);
                break;
            case PauseKeyWord:
                _listener.Pause();
                break;
            case FinishKeyWord:
                _listener.Finish();
                break;
            case NewKeyWord:
                _listener.New();
                break;
            case CheckConnectionKeyWord:
                _serialPort.WriteLine("ConnectionOK");
                break;
            case GetStateKeyWord:
                _serialPort.WriteLine(_state.ToString());
                break;
            default:
                throw new ArgumentException(
                    $"The arguments passed to the {nameof(SerialSessionController)} are invalid. Received: {data}");
        }
    }

    private static void CheckNumberOfArguments(int partsReceived, int partsExpected, string data)
    {
        if (partsReceived != 4)
        {
            throw new ArgumentException(
                $"Expected {partsExpected} arguments seperated by {Separator}, but received {partsReceived}. Received {data}");
        }
    }

    public override bool HasBarcodeReader => false;

    public override string ReadBarcode()
    {
        throw new NotImplementedException();
    }

    public override void Dispose()
    {
        _serialPort?.Dispose();
    }
}