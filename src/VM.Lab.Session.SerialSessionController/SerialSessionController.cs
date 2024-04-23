using System.Diagnostics;
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
    private const string WaitForAnalysisCompleteKeyWord = "WaitForAnalysisComplete";
    private const string WaitForSphereUpKeyWord = "WaitForSphereUp";
    private const string LastImageFailedKeyWord = "LastImageFailed";
    private ISphereHeightProvider _sphereHeightProvider;
    private readonly object _stateLock = new object();
    private bool _lastImageFailed;
    private string _lastErrorMessage;
    private bool _commandProcessingLockAcquired;
    private readonly object _commandProcessingLock = new object();

    private readonly string[] _keyWords =
    {
        CaptureKeyWord,
        PauseKeyWord,
        FinishKeyWord,
        NewKeyWord,
        CheckConnectionKeyWord,
        WaitForAnalysisCompleteKeyWord,
        WaitForSphereUpKeyWord,
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
            Console.WriteLine($"In {nameof(SerialSessionController)}.{nameof(StateChanged)} from {previousState} to {newState}.");
            _state = newState;
            
            if (newState == SessionState.CAPTURE_SINGLE_FRAME)
            {
                // Reset as we have begun on the next image
                _lastImageFailed = false;
                _lastErrorMessage = null;
            }
        }
    }

    private void EnforceOneCommandAtATime()
    {
        try
        {
            // Attempt to acquire the lock without blocking
            if (Monitor.TryEnter(_commandProcessingLock))
            {
                _commandProcessingLockAcquired = true;
                HandleCommand();
            }
            else
            {
                // Handle the case where the lock is not immediately available
                const string message = "Already processing previous command. New command ignored.";
                Console.WriteLine($"{nameof(SerialSessionController)}: " + message);
                _serialPort.WriteLine(message);
            }
        }
        finally
        {
            // Release the lock if acquired
            if (_commandProcessingLockAcquired)
            {
                Monitor.Exit(_commandProcessingLock);
            } 
        }
    }

    private void HandleCommand()
    {
         var data = _serialPort.ReadLine();
        Console.WriteLine($"{nameof(SerialSessionController)} received: " + data);
        var parts = data.Split(Separator);
        int expectedParts;
        switch (parts[0])
        {
            case CaptureKeyWord:
                expectedParts = 6;
                break;
            case WaitForAnalysisCompleteKeyWord:
            case WaitForSphereUpKeyWord:
                expectedParts = 2;
                break;
            case PauseKeyWord:
            case FinishKeyWord:
            case NewKeyWord:
            case CheckConnectionKeyWord:
            case LastImageFailedKeyWord:
                expectedParts = 1;
                break;
            default:
                var receivedString = data.Length == 0
                    ? "Received an empty string."
                    : $"Received {data}.";

                var message = $"The arguments passed to the {nameof(SerialSessionController)} are invalid. " +
                              $"The first word must be either {string.Join(", ", _keyWords)}. {receivedString}";
                Console.WriteLine(message);
                _serialPort.WriteLine(message);
                return;
        }

        if (parts.Length != expectedParts)
        {
            _serialPort.WriteLine(
                $"Expected {expectedParts} arguments seperated by {Separator}, but received {parts.Length}. Received {data}");
            return;
        }

        switch (parts[0])
        {
            case CaptureKeyWord:
            {
                if (parts[4] is not ("True" or "False"))
                {
                    _serialPort.WriteLine($"Last parameter must be either \"True\" or \"False\", but was {parts[4]}");
                    return;
                }
                var parseOk = ParseTimeout(parts[5], "capture", out var captureTimeoutSeconds);
                if (!parseOk)
                {
                    return;
                }
                bool suffixByTimestamp = parts[4] == "True";
                _listener.Capture(parts[0], parts[1], parts[2], suffixByTimestamp);
                
                var captureTimeoutMs = captureTimeoutSeconds * 1000;
                bool waitOk = WaitForCaptureComplete(captureTimeoutMs);
                if (waitOk)
                {
                    _serialPort.WriteLine("CaptureOK");
                }
                else
                {
                    var message = $"Failed waiting for capture to finish. Waited {captureTimeoutMs}ms.";
                    Console.WriteLine($"{nameof(SerialSessionController)}:{nameof(WaitForCaptureComplete)}: {message}");
                    _serialPort.WriteLine(message);
                }
                
                break;
            }
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
            case WaitForAnalysisCompleteKeyWord:
            {
                var parseOk = ParseTimeout(parts[1], "analysis", out var analysisTimeoutSeconds);
                if (!parseOk)
                {
                    return;
                }
                var analysisTimeoutMs = analysisTimeoutSeconds * 1000;
                bool waitOk = WaitForAnalysisToComplete(analysisTimeoutMs);
                if (waitOk)
                {
                    _serialPort.WriteLine("AnalysisComplete");
                }
                else
                {
                    var message = $"Failed waiting for analysis to finish. Waited {analysisTimeoutMs}ms.";
                    Console.WriteLine($"{nameof(SerialSessionController)}:{nameof(WaitForAnalysisToComplete)}: {message}");
                    _serialPort.WriteLine(message);
                }
                break;
            }
            case WaitForSphereUpKeyWord:
            {
                var parseOk = ParseTimeout(parts[1], "sphere up", out var sphereUpTimeoutSeconds);
                if (!parseOk)
                {
                    return;
                }
                bool sphereHeightOk = false;
                bool loggedOnce = false;
                var s = Stopwatch.StartNew();
                var sphereUpTimeoutMs = sphereUpTimeoutSeconds * 1000;
                while (!sphereHeightOk && s.ElapsedMilliseconds < sphereUpTimeoutMs)
                {
                    var sphereHeight = _sphereHeightProvider.GetSphereHeight();
                    Console.WriteLine($"{nameof(SerialSessionController)}:{WaitForSphereUpKeyWord}: {sphereHeight}");
                    const float minimumSafeSphereHeight = 90;
                    sphereHeightOk = sphereHeight > minimumSafeSphereHeight;
                    if (!sphereHeightOk && !loggedOnce)
                    {
                        loggedOnce = true;
                        Console.WriteLine($"{nameof(SerialSessionController)}:{WaitForSphereUpKeyWord}: " +
                                          $"Sphere height was {sphereHeight} but must be at minimum {minimumSafeSphereHeight}. " +
                                          $"Waiting for sphere to move up.");
                    }

                    Thread.Sleep(500);
                }

                // If sphere is still not up then we timed out
                if (!sphereHeightOk)
                {
                    var message = $"Failed waiting for sphere to move up. Waited {sphereUpTimeoutMs}ms.";
                    Console.WriteLine($"{nameof(SerialSessionController)}:{WaitForSphereUpKeyWord}: {message}");
                    _serialPort.WriteLine(message);
                }
                else
                {
                    _serialPort.WriteLine("SphereIsUp");
                }
                break;
            }
            case LastImageFailedKeyWord:
                var answer = _lastImageFailed ? $"True: {_lastErrorMessage}" : "False";
                _serialPort.WriteLine(answer);
                break;
            default:
                _serialPort.WriteLine(
                    $"The arguments passed to the {nameof(SerialSessionController)} are invalid. Received: {data}");
                return;
        }
    }
    
    private bool ParseTimeout(string timeoutToParse, string timeoutType, out int timeout)
    {
        var parseOk = int.TryParse(timeoutToParse, out timeout);
        if (!parseOk)
        {
            _serialPort.WriteLine($"Unable to parse {timeoutType} timeout parameter. Parameter was: {timeoutToParse}.");
        }
        return parseOk;
    }

    /// <summary>Called when data is received from the external device</summary>
    private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        EnforceOneCommandAtATime();
    }

    private bool WaitForAnalysisToComplete(int timeoutMs)
    {
        bool loggedOnce = false;
        bool stateMachineReady = false;
        var s = Stopwatch.StartNew();
        while (!stateMachineReady && s.ElapsedMilliseconds < timeoutMs)
        {
            lock (_stateLock)
            {
                stateMachineReady = _state is SessionState.IDLE_SINGLE_FRAME or SessionState.WAIT_NEXT_SINGLE_FRAME;
                if (!stateMachineReady && !loggedOnce)
                {
                    loggedOnce = true;
                    Console.WriteLine(
                        $"{nameof(SerialSessionController)}:{nameof(WaitForAnalysisToComplete)}: Not ready for next sample as state machine state was " +
                        $"{_state} but must be {SessionState.IDLE_SINGLE_FRAME} or {SessionState.WAIT_NEXT_SINGLE_FRAME}.");
                }
            }
        }

        return stateMachineReady;
    }
    
    private bool WaitForCaptureComplete(int timeoutMs)
    {
        bool loggedOnce = false;
        bool stateMachineReady = false;
        var s = Stopwatch.StartNew();
        while (!stateMachineReady && s.ElapsedMilliseconds < timeoutMs)
        {
            lock (_stateLock)
            {
                stateMachineReady = _state is SessionState.ANALYZING_SINGLE_FRAME or SessionState.WAIT_NEXT_SINGLE_FRAME;
                if (!stateMachineReady && !loggedOnce)
                {
                    loggedOnce = true;
                    Console.WriteLine($"{nameof(SerialSessionController)}:{nameof(WaitForCaptureComplete)}: Waiting for capture to complete.");
                }
            }
        }

        return stateMachineReady;
    }

    /// <summary>
    /// Called when something in the pipeline of capturing, analysing, or saving of results, failed for the current image.
    /// Used to inform external controllers that something went wrong with the current image. 
    /// </summary>
    public override void LastImageFailed(string errorMessage)
    {
        _lastImageFailed = true;
        _lastErrorMessage = errorMessage;
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