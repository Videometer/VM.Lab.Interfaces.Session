using System.IO.Ports;
using VM.Lab.Interfaces.Session;

namespace VM.Lab.Session.SerialSessionController;

/// <summary>
/// Concrete implementation of a external session controller that is controlled by an external device that
/// communicate with this controller using serial communication over a COM port. 
/// </summary>
public class SerialSessionController : SessionController, INeedSphereHeightProvider
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
    private const string LastImageFailedKeyWord = "LastImageFailed";
    private readonly AutoResetEvent _analysisComplete = new AutoResetEvent(false);
    private ISphereHeightProvider _sphereHeightProvider;
    private readonly object _stateLock = new object();
    private bool _lastImageFailed;
    
    private readonly string[] _keyWords =
    {
        CaptureKeyWord,
        PauseKeyWord,
        FinishKeyWord,
        NewKeyWord,
        CheckConnectionKeyWord,
        ReadyForNextSampleKeyWord,
        LastImageFailedKeyWord
    };
    
    /// <summary>
    /// Constructs the external session controller. This method is called internally by the VideometerLab software. 
    /// </summary>
    /// <param name="listener">The session listener that is use to provide commands to the session</param>
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
        _serialPort.ReadExisting();
    }

    /// <summary>Occurs when a session state has changed</summary>
    /// <param name="previousState"></param>
    /// <param name="newState"></param>
    public override void StateChanged(SessionState previousState, SessionState newState)
    {
        lock (_stateLock)
        {
            Console.WriteLine($"In {nameof(SerialSessionController)}.{nameof(StateChanged)} from {previousState} to {newState}"); 
            _state = newState;
        }
        
        if (newState == SessionState.CAPTURE_SINGLE_FRAME)
        {
            // Reset as we have begun on the next image
            _lastImageFailed = false;
        }
        if (newState == SessionState.WAIT_NEXT_SINGLE_FRAME)
        {
            _analysisComplete.Set();
        }
    }

    /// <summary>Called when data is received from the external device</summary>
    private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var data = _serialPort.ReadLine();
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
            case LastImageFailedKeyWord:
                expectedParts = 1;
                break;
            default:
                var receivedString = data.Length == 0
                    ? "Received an empty string."
                    : $"Received {data}.";
                _serialPort.WriteLine($"The arguments passed to the {nameof(SerialSessionController)} are invalid. " +
                                      $"The first word must be either {string.Join(", ", _keyWords)}. {receivedString}");
                return;
        }

        if (parts.Length != expectedParts)
        {
            _serialPort.WriteLine($"Expected {expectedParts} arguments seperated by {Separator}, but received {parts.Length}. Received {data}");
            return;
        }
       
        switch (parts[0])
        {
            case CaptureKeyWord:
                if (parts[4] is not ("True" or "False"))
                {
                    _serialPort.WriteLine($"Last parameter must be either \"True\" or \"False\", but was {parts[4]}");
                    return;
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
                var sphereHeight = _sphereHeightProvider.GetSphereHeight();
                const float minimumSafeSphereHeight = 90;
                var sphereHeightOk = sphereHeight > minimumSafeSphereHeight;
                if (!sphereHeightOk)
                {
                    Console.WriteLine($"{nameof(SerialSessionController)}: Not ready for next sample as sphere height was {sphereHeight} but must be at minimum {minimumSafeSphereHeight}.");
                }
                bool stateMachineReady;
                lock (_stateLock)
                {
                    stateMachineReady = _state is SessionState.IDLE_SINGLE_FRAME or SessionState.WAIT_NEXT_SINGLE_FRAME;
                    if (!stateMachineReady)
                    {
                        Console.WriteLine(
                            $"{nameof(SerialSessionController)}: Not ready for next sample as state machine state was " +
                            $"{_state} but must be {SessionState.IDLE_SINGLE_FRAME} or {SessionState.WAIT_NEXT_SINGLE_FRAME}.");
                    }
                }

                var ready = sphereHeightOk && stateMachineReady ? "True" : "False";
                _serialPort.WriteLine(ready);
                break;
            case LastImageFailedKeyWord:
                var answer = _lastImageFailed ? "True" : "False";
                _serialPort.WriteLine(answer);
                break;
            default:
                _serialPort.WriteLine($"The arguments passed to the {nameof(SerialSessionController)} are invalid. Received: {data}");
                return;
        }
    }
    
    private void WaitForAnalysisToComplete()
    {
        // There is not timeout here as the timeout is handled on the Python side
        _analysisComplete.WaitOne();
    }

    /// <summary>
    /// Called when something in the pipeline of capturing, analysing, or saving of results, failed for the current image.
    /// Used to inform external controllers that something went wrong with the current image. 
    /// </summary>
    public override void LastImageFailed()
    {
        _lastImageFailed = true;
    }
    
    /// <summary>
    /// Provides the concrete implementation of <see cref="ISphereHeightProvider"/>.
    /// This method is called internally by the VideometerLab software. 
    /// </summary>
    /// <param name="provider">The concrete implementation of <see cref="ISphereHeightProvider"/>.</param>
    public void ProvideSphereHeightProvider(ISphereHeightProvider provider)
    {
        _sphereHeightProvider = provider;
    }
    
    /// <summary>Clean up internally used resources</summary>
    public override void Dispose()
    {
        _serialPort.DataReceived -= SerialPort_DataReceived;
        _serialPort?.Dispose();
    }
}