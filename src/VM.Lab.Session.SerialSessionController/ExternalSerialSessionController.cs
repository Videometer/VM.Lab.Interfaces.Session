using System.Diagnostics;
using System.IO.Ports;
using VM.Lab.Interfaces.Session;

namespace VM.Lab.Session.SerialSessionController;

/// <summary>
/// Concrete implementation of an external session controller that is controlled by an external device that
/// communicates with this controller using serial communication over a COM port. 
/// </summary>
public class ExternalSerialSessionController : ExternalSessionController, INeedSphereHeightProvider
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
    private const string LoadLightSetupKeyWord = "LoadLightSetup";
    private const string SaveLightSetupKeyWord = "SaveLightSetup";
    private const string DoAutoLightKeyWord = "DoAutoLight";
    private const string AcknowledgeAdjustingLightSetupFailedKeyWord = "AcknowledgeAdjustingLightSetupFailed";

    /// <summary>
    /// Prefix on every response that reports the outcome of a command we did receive and understand.
    /// It tells the caller not to send the command again, because doing so would only repeat the outcome.
    /// </summary>
    private const string CommandFailedPrefix = "FAILED: ";
    private ISphereHeightProvider _sphereHeightProvider;
    private readonly object _stateLock = new object();
    private bool _lastImageFailed;
    private string _lastErrorMessage;
    private readonly ManualResetEventSlim _captureComplete = new ManualResetEventSlim(false);
    private readonly ManualResetEventSlim _analysisComplete = new ManualResetEventSlim(false);
    private readonly ManualResetEventSlim _lightSetupAdjustmentComplete = new ManualResetEventSlim(false);
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
        GetLastErrorMessageKeyWord,
        LoadLightSetupKeyWord,
        SaveLightSetupKeyWord,
        DoAutoLightKeyWord,
        AcknowledgeAdjustingLightSetupFailedKeyWord
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

            // A failure state never releases a waiter, even though it does end the command. Releasing here
            // would race with the reason being reported: a waiter could wake and read _lastImageFailed while
            // it is still false, and so report a failed command as a success. ProvideErrorMessage does the
            // releasing instead, after it has recorded the reason.
            if (newState is SessionState.Error or SessionState.AdjustingLightSetupFailed)
            {
                return;
            }

            if (newState != SessionState.Capturing)
            {
                // The image is in hand
                _captureComplete.Set();

                // Processing is the only remaining step between a captured image and a usable result,
                // so reaching anything past it means the analysis is over.
                if (newState != SessionState.Processing)
                {
                    _analysisComplete.Set();
                }
            }

            // Light-setup work is a side-trip off Ready, so leaving AdjustingLightSetup ends it.
            if (newState != SessionState.AdjustingLightSetup)
            {
                _lightSetupAdjustmentComplete.Set();
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
            case LoadLightSetupKeyWord:
            case SaveLightSetupKeyWord:
                expectedParts = 3;
                break;
            case WaitForAnalysisCompleteKeyWord:
            case WaitForSphereUpKeyWord:
            case DoAutoLightKeyWord:
                expectedParts = 2;
                break;
            case StopKeyWord:
            case NewKeyWord:
            case CheckConnectionKeyWord:
            case LastImageFailedKeyWord:
            case GetLastErrorMessageKeyWord:
            case AcknowledgeAdjustingLightSetupFailedKeyWord:
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
                CaptureIsAboutToBeCalled();
                if (!TryIssueCommand("capture", () => _listener.Capture(parts[1], parts[2], parts[3])))
                {
                    break;
                }

                var captureTimeoutMs = captureTimeoutSeconds * 1000;
                var captureFailureReason = WaitForCaptureComplete(captureTimeoutMs);
                if (captureFailureReason == null)
                {
                    _serialPort.WriteLine("CaptureFinish");
                }
                else
                {
                    WriteCommandFailed(captureFailureReason);
                }
                break;
            }
            case StopKeyWord:
                TryIssueCommand("stop", _listener.Stop);
                break;
            case NewKeyWord:
                if (TryIssueCommand("new measurement", _listener.New))
                {
                    _serialPort.WriteLine("NewMeasurementStarted");
                }
                break;
            case AcknowledgeAdjustingLightSetupFailedKeyWord:
                if (TryIssueCommand(
                        "acknowledgement of the failed light setup adjustment",
                        _listener.AcknowledgeAdjustingLightSetupFailed))
                {
                    _serialPort.WriteLine("AdjustingLightSetupFailedAcknowledged");
                }
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
                var analysisFailureReason = WaitForAnalysisToComplete(analysisTimeoutMs);
                if (analysisFailureReason == null)
                {
                    _serialPort.WriteLine("AnalysisComplete");
                }
                else
                {
                    WriteCommandFailed(analysisFailureReason);
                }
                break;
            }
            case WaitForSphereUpKeyWord:
            {
                if (_sphereHeightProvider is null)
                {
                    throw new InvalidOperationException(
                        $"{nameof(ExternalSerialSessionController)}:{nameof(WaitForSphereUpKeyWord)}: Sphere height provider was not provided.");
                }
                
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
                    WriteCommandFailed(message);
                }
                else
                {
                    _serialPort.WriteLine("SphereIsUp");
                }
                break;
            }
            case LoadLightSetupKeyWord:
            {
                var parseOk = ParseTimeout(parts[2], "load light setup", out var loadTimeoutSeconds);
                if (!parseOk)
                {
                    return;
                }
                var lightSetupToLoad = parts[1];
                HandleLightSetupCommand($"loading of light setup '{lightSetupToLoad}'",
                    () => _listener.LoadLightSetup(lightSetupToLoad), loadTimeoutSeconds, "LightSetupLoaded");
                break;
            }
            case SaveLightSetupKeyWord:
            {
                var parseOk = ParseTimeout(parts[2], "save light setup", out var saveTimeoutSeconds);
                if (!parseOk)
                {
                    return;
                }
                var lightSetupToSave = parts[1];
                HandleLightSetupCommand($"saving of light setup '{lightSetupToSave}'",
                    () => _listener.SaveLightSetup(lightSetupToSave), saveTimeoutSeconds, "LightSetupSaved");
                break;
            }
            case DoAutoLightKeyWord:
            {
                var parseOk = ParseTimeout(parts[1], "auto light", out var autoLightTimeoutSeconds);
                if (!parseOk)
                {
                    return;
                }
                HandleLightSetupCommand("auto light", () => _listener.DoAutoLight(), autoLightTimeoutSeconds,
                    "AutoLightComplete");
                break;
            }
            case LastImageFailedKeyWord:
            {
                // "The last image failed" is a settled answer, so it carries the prefix: asking again would
                // only get the same answer.
                if (_lastImageFailed)
                {
                    WriteCommandFailed($"The last image failed: {_lastErrorMessage}");
                }
                else
                {
                    _serialPort.WriteLine("False");
                }
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
    
    /// <summary>Passes one command to the session, answering the caller over the serial port if it is refused.</summary>
    /// <returns>True if the session accepted the command.</returns>
    private bool TryIssueCommand(string what, Action command)
    {
        try
        {
            command();
            return true;
        }
        catch (Exception ex)
        {
            var refused = $"Could not start {what}: {ex.Message}";
            Console.WriteLine($"{nameof(ExternalSerialSessionController)}:{nameof(TryIssueCommand)}: {refused}");
            WriteCommandFailed(refused);
            return false;
        }
    }

    /// <summary>Answers a command whose outcome is settled, so the caller knows not to send it again.</summary>
    private void WriteCommandFailed(string reason) => _serialPort.WriteLine(CommandFailedPrefix + reason);

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
    /// <para>
    /// Driven by the command rather than by the session entering <see cref="SessionState.Capturing"/>. When
    /// capturing continuously, the session enters that state by itself, straight out of Error — which would
    /// discard the very failure being reported and re-arm an event a waiter is already blocked on.
    /// </para>
    /// </summary>
    private void CaptureIsAboutToBeCalled()
    {
        lock (_stateLock)
        {
            _lastImageFailed = false;
            _lastErrorMessage = null;
            _captureComplete.Reset();
            _analysisComplete.Reset();
        }
    }

    /// <summary>
    /// Issues one light-setup command and answers the caller once the session has finished with it.
    /// <para>
    /// The session only accepts these between captures and refuses them by throwing, so the command is
    /// guarded: an exception here would otherwise escape onto the serial port's event thread, where nothing
    /// can report it and it takes the process down.
    /// </para>
    /// </summary>
    private void HandleLightSetupCommand(string what, Action command, int timeoutSeconds, string successResponse)
    {
        // Discard the previous outcome and arm the wait. Per command rather than on entry to
        // AdjustingLightSetup, for the same reason as in CaptureIsAboutToBeCalled.
        lock (_stateLock)
        {
            _lastImageFailed = false;
            _lastErrorMessage = null;
            _lightSetupAdjustmentComplete.Reset();
        }

        if (!TryIssueCommand(what, command))
        {
            return;
        }

        var timeoutMs = timeoutSeconds * 1000;
        var failureReason = WaitFor(_lightSetupAdjustmentComplete, timeoutMs, what, nameof(HandleLightSetupCommand));
        if (failureReason == null)
        {
            _serialPort.WriteLine(successResponse);
        }
        else
        {
            WriteCommandFailed(failureReason);
        }
    }

    private string WaitForAnalysisToComplete(int timeoutMs) =>
        WaitFor(_analysisComplete, timeoutMs, "analysis to complete", nameof(WaitForAnalysisToComplete));

    private string WaitForCaptureComplete(int timeoutMs) =>
        WaitFor(_captureComplete, timeoutMs, "capture to complete", nameof(WaitForCaptureComplete));

    /// <summary>
    /// Blocks until <paramref name="completed"/> is signaled — by <see cref="StateChanged"/> on success, or by
    /// <see cref="ProvideErrorMessage"/> on failure.
    /// <para>
    /// Waiting on the event rather than polling the state matters for correctness, not just for efficiency:
    /// the session can pass through a state faster than a poll loop can observe it — a capture from a folder
    /// or a cached device can run all the way back to Ready before <c>Capture</c> even returns — whereas a
    /// signaled event is still signaled whenever we get round to waiting on it.
    /// </para>
    /// <para>
    /// A failure counts as finished, so the wait ends promptly with the reason instead of running out the whole
    /// timeout waiting for a state that is never coming. That matters most for a failed light setup
    /// adjustment: the session stays in AdjustingLightSetupFailed until it is acknowledged, so no further
    /// state change would ever arrive.
    /// </para>
    /// </summary>
    /// <returns>
    /// Null when the step completed successfully, otherwise why it did not.
    /// Timing out and failing are reported separately.
    /// </returns>
    private string WaitFor(ManualResetEventSlim completed, int timeoutMs, string what, string caller)
    {
        if (!completed.Wait(timeoutMs))
        {
            var timedOut = $"Timed out after {timeoutMs}ms waiting for {what}. Session state is {_state}.";
            Console.WriteLine($"{nameof(ExternalSerialSessionController)}:{caller}: {timedOut}");
            return timedOut;
        }

        lock (_stateLock)
        {
            if (!_lastImageFailed)
            {
                return null;
            }

            var failed = $"Waited for {what}, but it failed: {_lastErrorMessage}";
            Console.WriteLine($"{nameof(ExternalSerialSessionController)}:{caller}: {failed}");
            return failed;
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

            // Whatever was in flight is finished, so release anyone waiting on it — an image, or a light setup
            // adjustment. Set after the reason is recorded, so a released waiter is guaranteed to see it.
            _captureComplete.Set();
            _analysisComplete.Set();
            _lightSetupAdjustmentComplete.Set();
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
    public override void Dispose()
    {
        _serialPort.DataReceived -= SerialPort_DataReceived;
        _serialPort?.Dispose();

        // Release anyone still blocked before disposing, so the wait ends rather than throwing.
        _captureComplete.Set();
        _analysisComplete.Set();
        _lightSetupAdjustmentComplete.Set();
        _captureComplete.Dispose();
        _analysisComplete.Dispose();
        _lightSetupAdjustmentComplete.Dispose();
    }
}