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
    private readonly ManualResetEventSlim _captureComplete = new ManualResetEventSlim(false);
    private readonly ManualResetEventSlim _analysisComplete = new ManualResetEventSlim(false);
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

            if (newState is not (SessionState.Capturing or SessionState.Error))
            {
                // The image capture is either completed successfully or have failed
                _captureComplete.Set();

                // Processing is the only remaining step between a captured image and a usable result,
                // so reaching anything past it means the analysis is over.
                if (newState != SessionState.Processing)
                {
                    _analysisComplete.Set();
                }
            }

            // Error is deliberately not treated as finished, even though it is the end of this image. The
            // session reports the state change before it reports the reason (it raises the transition, then
            // runs the state's entry action, which is what calls ProvideErrorMessage). Releasing a waiter here
            // would let it read _lastImageFailed while it is still false and call a failed image a success.
            // Error always recovers to Ready or Capturing, and that transition — or ProvideErrorMessage
            // itself — releases the waiter once the reason is in place.
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
                BeginCapture();
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

    /// <summary>
    /// Discards the previous image's outcome and arms both waits, ready for the Capture command about to be
    /// issued. Must be called before the command reaches the session, or a wait could be satisfied by the
    /// previous image.
    /// </summary>
    private void BeginCapture()
    {
        lock (_stateLock)
        {
            _lastImageFailed = false;
            _lastErrorMessage = null;
            _captureComplete.Reset();
            _analysisComplete.Reset();
        }
    }

    private bool WaitForAnalysisToComplete(int timeoutMs) =>
        WaitFor(_analysisComplete, timeoutMs, "analysis to complete", nameof(WaitForAnalysisToComplete));

    private bool WaitForCaptureComplete(int timeoutMs) =>
        WaitFor(_captureComplete, timeoutMs, "capture to complete", nameof(WaitForCaptureComplete));

    /// <summary>
    /// Blocks until <paramref name="completed"/> is signaled by <see cref="StateChanged"/>, and reports
    /// whether the step actually succeeded.
    /// <para>
    /// Waiting on the event rather than polling the state matters for correctness, not just for efficiency:
    /// the session can pass through a state faster than a poll loop can observe it — a capture from a folder
    /// or a cached device can run all the way back to Ready before <c>Capture</c> even returns — whereas a
    /// signaled event is still signaled whenever we get round to waiting on it.
    /// </para>
    /// <para>
    /// A failed image counts as finished, so the wait ends promptly and returns false with the reason,
    /// instead of running out the whole timeout waiting for a state that is never coming.
    /// </para>
    /// </summary>
    private bool WaitFor(ManualResetEventSlim completed, int timeoutMs, string what, string caller)
    {
        if (!completed.Wait(timeoutMs))
        {
            Console.WriteLine($"{nameof(ExternalSerialSessionController)}:{caller}: " +
                              $"Timed out after {timeoutMs}ms waiting for {what}. Session state is {_state}.");
            return false;
        }

        lock (_stateLock)
        {
            if (!_lastImageFailed)
            {
                return true;
            }

            Console.WriteLine($"{nameof(ExternalSerialSessionController)}:{caller}: " +
                              $"Waited for {what}, but the image failed: {_lastErrorMessage}");
            return false;
        }
    }
    
    /// <summary>
    /// Called id an error happens, for example, something in the pipeline of capturing, analyzing, or saving of results, failed for the current image.
    /// Used to inform external controllers that something went wrong. 
    /// Used to inform external controllers that something went wrong. 
    /// </summary>
    /// <param name="errorMessage">Error message explaining what failed</param>
    public override void ProvideErrorMessage(string errorMessage)
    {
        lock (_stateLock)
        {
            _lastImageFailed = true;
            _lastErrorMessage = errorMessage;

            // This image is finished so release anyone waiting on it.
            // Set after the reason is recorded, so a released waiter is guaranteed to see it.
            _captureComplete.Set();
            _analysisComplete.Set();
        }
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

        // Release anyone still blocked before disposing, so the wait ends rather than throwing.
        _captureComplete.Set();
        _analysisComplete.Set();
        _captureComplete.Dispose();
        _analysisComplete.Dispose();
    }
}