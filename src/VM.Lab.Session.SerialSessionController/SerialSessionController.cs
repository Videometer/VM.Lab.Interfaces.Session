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
    private const string ReadyForNextSampleKeyWord = "ReadyForNextSample";
    private readonly AutoResetEvent _analysisComplete = new AutoResetEvent(false); 
    
    private readonly string[] _keyWords =
    {
        CaptureKeyWord,
        PauseKeyWord,
        FinishKeyWord,
        NewKeyWord,
        CheckConnectionKeyWord,
        ReadyForNextSampleKeyWord,
    };
    
    public SerialSessionController(ISessionControllerListener listener) : base(listener)
    {
        const string port = "COM2";
        _serialPort = new SerialPort(port, 9600, Parity.None, 8, StopBits.One);
        if (_serialPort.IsOpen)
        {
            throw new InvalidOperationException($"The COM Port {port} is already open.");
        }
        _serialPort.DataReceived += SerialPort_DataReceived;
        _serialPort.Open();
        
        _serialPort.DiscardInBuffer();
        _serialPort.DiscardOutBuffer();
    }

    public override void StateChanged(SessionState previousState, SessionState newState)
    {
        Console.WriteLine($"In {nameof(SerialSessionController)}.{nameof(StateChanged)} from {previousState} to {newState}");
        _state = newState;
        
        if (newState == SessionState.WAIT_NEXT_SINGLE_FRAME)
        {
            _analysisComplete.Set();
        }
    }

    private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        string data = _serialPort.ReadLine();
        Console.WriteLine($"{nameof(SerialSessionController)} received: " + data);
        var parts = data.Split(Separator);
        int expectedParts;
        switch (parts[0])
        {
            case CaptureKeyWord:
                expectedParts = 5;
                break;
            case PauseKeyWord:
            case FinishKeyWord:
            case NewKeyWord:
            case CheckConnectionKeyWord:
            case ReadyForNextSampleKeyWord:
                expectedParts = 1;
                break;
            default:
                var receivedString = data.Length == 0
                    ? "Received an empty string."
                    : $"Received {data}.";
                throw new ArgumentException(
                    $"The arguments passed to the {nameof(SerialSessionController)} are invalid. " +
                    $"The first word must be either {string.Join(", ", _keyWords)}. {receivedString}");
        }

        if (parts.Length != expectedParts)
        {
            throw new ArgumentException(
                $"Expected {expectedParts} arguments seperated by {Separator}, but received {parts.Length}. Received {data}");
        }
       
        switch (parts[0])
        {
            case CaptureKeyWord:
                if (parts[4] is not ("True" or "False"))
                {
                    throw new ArgumentException(
                        $"Last parameter must be either \"True\" or \"False\", but was {parts[4]}");
                }
                bool suffixByTimestamp = parts[4] == "True";
                _listener.Capture(parts[0], parts[1], parts[2], suffixByTimestamp);
                _serialPort.WriteLine("CaptureOK");
                WaitForAnalysisToComplete();
                _serialPort.WriteLine("AnalysisDone");
                Console.WriteLine($"{nameof(SerialSessionController)}: AnalysisDone");
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
            case ReadyForNextSampleKeyWord:
                // TODO: Added check for sphere height
                // TODO: Get "Analysis OK" / Is the read line shown?
                // TODO: Added check for exception window open
                var ready = _state == SessionState.IDLE_SINGLE_FRAME ? "True" : "False";
                _serialPort.WriteLine(ready);
                break;
            default:
                throw new ArgumentException(
                    $"The arguments passed to the {nameof(SerialSessionController)} are invalid. Received: {data}");
        }
    }

    private void WaitForAnalysisToComplete()
    {
        // There is not timeout here as the timeout is handled on the Python side
        _analysisComplete.WaitOne();
    }
    
    public override bool HasBarcodeReader => false;

    public override string ReadBarcode()
    {
        throw new NotImplementedException();
    }

    public override void Dispose()
    {
        _serialPort.DataReceived -= SerialPort_DataReceived;
        _serialPort?.Dispose();
    }
}