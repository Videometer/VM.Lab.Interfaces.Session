using System.Diagnostics;
using System.IO.Ports;
using VM.Lab.Interfaces.Session;

namespace VM.Lab.Session.SerialSessionController;

/// <summary>
/// Concrete implementation of an external session controller that is controlled by an external device that
/// communicates with this controller using serial communication over a COM port. 
/// </summary>
public class ExternalSerialSessionController : ExternalSessionController, INeedSphereHeightProvider, IDisposable
{
    private readonly SerialPort _serialPort;
    private SessionState _state;
    private const char Separator = ';';
    private const string CaptureKeyWord = "Capture";
    private const string StopKeyWord = "Stop";
    private const string NewKeyWord = "New";
    private const string CheckConnectionKeyWord = "CheckConnection";
    private const string WaitForAnalysisCompleteKeyWord = "WaitForAnalysisComplete";
    private const string WaitForSphereUpKeyWord = "WaitForSphereUp";
    private const string LastImageFailedKeyWord = "LastImageFailed";
    private const string GetLastErrorMessageKeyWord = "GetLastErrorMessage";
    private ISphereHeightProvider _sphereHeightProvider;
    private readonly object _stateLock = new object();
    private bool _lastImageFailed;
    private string _lastErrorMessage;
    private bool _commandProcessingLockAcquired;
    private readonly object _commandProcessingLock = new object();

    private readonly string[] _keyWords =
    {
        CaptureKeyWord,
        StopKeyWord,
        NewKeyWord,
        CheckConnectionKeyWord,
        WaitForAnalysisCompleteKeyWord,
        WaitForSphereUpKeyWord,
        LastImageFailedKeyWord,
        GetLastErrorMessageKeyWord
    };

    /// <summary>
    /// Constructs the external session controller. This method is called internally by the VideometerLab software. 
    /// </summary>
    /// <param name="listener">The session listener that is use to provide commands to the session</param>
    public ExternalSerialSessionController(IExternalSessionControllerListener listener) : base(listener)
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
    /// <param name="newState"></param>
    public override void StateChanged(SessionState newState)
    {
        lock (_stateLock)
        {
            Console.WriteLine($"In {nameof(ExternalSerialSessionController)}.{nameof(StateChanged)} to {newState}.");
            _state = newState;
            
            if (newState == SessionState.Capturing)
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
                Console.WriteLine($"{nameof(ExternalSerialSessionController)}: " + message);
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
        Console.WriteLine($"{nameof(ExternalSerialSessionController)} received: " + data);
        var parts = data.Split(Separator);
        int expectedParts;
        switch (parts[0])
        {
            case CaptureKeyWord:
                expectedParts = 5;
                break;
            case WaitForAnalysisCompleteKeyWord:
            case WaitForSphereUpKeyWord:
                expectedParts = 2;
                break;
            case StopKeyWord:
            case NewKeyWord:
            case CheckConnectionKeyWord:
            case LastImageFailedKeyWord:
            case GetLastErrorMessageKeyWord:
                expectedParts = 1;
                break;
            default:
                var receivedString = data.Length == 0
                    ? "Received an empty string."
                    : $"Received {data}.";

                var message = $"The arguments passed to the {nameof(ExternalSerialSessionController)} are invalid. " +
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
                var parseOk = ParseTimeout(parts[4], "capture", out var captureTimeoutSeconds);
                if (!parseOk)
                {
                    return;
                }
                _listener.Capture(parts[1], parts[2], parts[3]);
                
                var captureTimeoutMs = captureTimeoutSeconds * 1000;
                bool waitOk = WaitForCaptureComplete(captureTimeoutMs);
                if (waitOk)
                {
                    _serialPort.WriteLine("CaptureFinish");
                }
                else
                {
                    var message = $"Failed waiting for capture to finish. Waited {captureTimeoutMs}ms.";
                    Console.WriteLine($"{nameof(ExternalSerialSessionController)}:{nameof(WaitForCaptureComplete)}: {message}");
                    _serialPort.WriteLine(message);
                }
                
                break;
            }
            case StopKeyWord:
                _listener.Stop();
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
                    Console.WriteLine($"{nameof(ExternalSerialSessionController)}:{nameof(WaitForAnalysisToComplete)}: {message}");
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
                    Console.WriteLine($"{nameof(ExternalSerialSessionController)}:{WaitForSphereUpKeyWord}: {sphereHeight}");
                    const float minimumSafeSphereHeight = 82;
                    sphereHeightOk = sphereHeight > minimumSafeSphereHeight;
                    if (!sphereHeightOk && !loggedOnce)
                    {
                        loggedOnce = true;
                        Console.WriteLine($"{nameof(ExternalSerialSessionController)}:{WaitForSphereUpKeyWord}: " +
                                          $"Sphere height was {sphereHeight} but must be at minimum {minimumSafeSphereHeight}. " +
                                          $"Waiting for sphere to move up.");
                    }

                    Thread.Sleep(500);
                }

                // If sphere is still not up then we timed out
                if (!sphereHeightOk)
                {
                    var message = $"Failed waiting for sphere to move up. Waited {sphereUpTimeoutMs}ms.";
                    Console.WriteLine($"{nameof(ExternalSerialSessionController)}:{WaitForSphereUpKeyWord}: {message}");
                    _serialPort.WriteLine(message);
                }
                else
                {
                    _serialPort.WriteLine("SphereIsUp");
                }
                break;
            }
            case LastImageFailedKeyWord:
            {
                var answer = _lastImageFailed ? $"True: {_lastErrorMessage}" : "False";
                _serialPort.WriteLine(answer);
                break;
            }
            case GetLastErrorMessageKeyWord:
            {
                var answer = string.IsNullOrWhiteSpace(_lastErrorMessage) ? "No error" : _lastErrorMessage;
                _serialPort.WriteLine(answer);
                break;
            }
            default:
                _serialPort.WriteLine(
                    $"The arguments passed to the {nameof(ExternalSerialSessionController)} are invalid. Received: {data}");
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
                stateMachineReady = _state is SessionState.Waiting or SessionState.Ready;
                if (!stateMachineReady && !loggedOnce)
                {
                    loggedOnce = true;
                    Console.WriteLine(
                        $"{nameof(ExternalSerialSessionController)}:{nameof(WaitForAnalysisToComplete)}: Not ready for next sample as state machine state was " +
                        $"{_state} but must be {SessionState.Waiting} or {SessionState.Ready}.");
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
                stateMachineReady = _state is SessionState.Processing;
                if (!stateMachineReady && !loggedOnce)
                {
                    loggedOnce = true;
                    Console.WriteLine($"{nameof(ExternalSerialSessionController)}:{nameof(WaitForCaptureComplete)}: Waiting for capture to complete.");
                }
            }
        }

        return stateMachineReady;
    }
    
    /// <summary>
    /// Called id an error happens, for example, something in the pipeline of capturing, analyzing, or saving of results, failed for the current image.
    /// Used to inform external controllers that something went wrong. 
    /// Used to inform external controllers that something went wrong. 
    /// </summary>
    /// <param name="errorMessage">Error message explaining what failed</param>
    public override void ProvideErrorMessage(string errorMessage)
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
    public void Dispose()
    {
        _serialPort.DataReceived -= SerialPort_DataReceived;
        _serialPort?.Dispose();
    }
}