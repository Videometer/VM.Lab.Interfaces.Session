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
    private const string _captureWasStopped = "The capture was stopped before an image was captured.";
    private const int _maxTimeoutSeconds = int.MaxValue / 1000;

    /// <summary>Set by <see cref="Dispose"/>. A wait that ends because of it is answered as closed, not as finished.</summary>
    private volatile bool _disposed;

    /// <summary>
    /// True from a capture command is issued until the session reports <see cref="SessionState.Capturing"/>.
    /// Until then no state counts as "the capture is finished": the session can report the state it was
    /// already in once more before it starts capturing, and that must not be mistaken for the image being done.
    /// </summary>
    private bool _awaitingCapturing;

    /// <summary>The same as <see cref="_awaitingCapturing"/>, for a light setup command and <see cref="SessionState.AdjustingLightSetup"/>.</summary>
    private bool _awaitingAdjustingLightSetup;

    /// <summary>True while a light setup command is in flight. Tells which kind of work a reported error belongs to.</summary>
    private bool _lightSetupCommandInFlight;

    /// <summary>Why the last image failed. Null if it did not.</summary>
    private string _imageFailure;

    /// <summary>
    /// Why the last light setup adjustment failed. Null if it did not. Kept apart from <see cref="_imageFailure"/>,
    /// so a failed light setup adjustment is not reported as a failed image later on, and the other way round.
    /// </summary>
    private string _lightSetupFailure;
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
            var previousState = _state;
            _state = newState;

            // A capture that produced an image always goes on to Processing, and one that failed to Error.
            // Leaving Capturing for anything else means the capture was stopped, and there is no image.
            // Recorded as the reason before the waits are released below, so the waiter is sure to see it.
            var leftCapturing = previousState == SessionState.Capturing && newState != SessionState.Capturing;
            if (leftCapturing && newState is not (SessionState.Processing or SessionState.Error))
            {
                _imageFailure = _captureWasStopped;
            }

            // The work the command asked for has started, so from here on leaving the state means it is over.
            if (newState == SessionState.Capturing)
            {
                _awaitingCapturing = false;
            }

            if (newState == SessionState.AdjustingLightSetup)
            {
                _awaitingAdjustingLightSetup = false;
            }

            ReleaseWaitsFinishedIn(newState);
        }
    }

    /// <summary>
    /// Releases the waits for the steps that are over when the session is in <paramref name="state"/>.
    /// Must be called under the state lock.
    /// </summary>
    private void ReleaseWaitsFinishedIn(SessionState state)
    {
        // A failure state never releases a waiter, even though it does end the command. Releasing here
        // would race with the reason being reported: a waiter could wake and read the failure while
        // it is still empty, and so report a failed command as a success. ProvideErrorMessage does the
        // releasing instead, after it has recorded the reason.
        // The exception is a failed light setup adjustment whose reason is already recorded: the session is now
        // where the failure can be acknowledged, so this is the moment to answer the command.
        var failedLightSetupCanBeAcknowledged = state == SessionState.AdjustingLightSetupFailed
                                                && _lightSetupFailure != null
                                                && !_awaitingAdjustingLightSetup;
        if (failedLightSetupCanBeAcknowledged)
        {
            _lightSetupAdjustmentComplete.Set();
        }

        if (state is SessionState.Error or SessionState.AdjustingLightSetupFailed)
        {
            return;
        }

        if (state != SessionState.Capturing && !_awaitingCapturing)
        {
            // The image is in hand
            _captureComplete.Set();

            // Processing is the only remaining step between a captured image and a usable result,
            // so reaching anything past it means the analysis is over.
            if (state != SessionState.Processing)
            {
                _analysisComplete.Set();
            }
        }

        // Light-setup work is a side-trip off Ready, so leaving AdjustingLightSetup ends it.
        if (state != SessionState.AdjustingLightSetup && !_awaitingAdjustingLightSetup)
        {
            _lightSetupAdjustmentComplete.Set();
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
                WriteResponse(message);
            }
        }
        catch (Exception ex)
        {
            // An exception here would escape onto the serial port's event thread, where nothing can report
            // it and it takes the process down. Answer the caller instead.
            Console.WriteLine($"{nameof(ExternalSerialSessionController)}: Unexpected error: {ex}");
            WriteCommandFailed($"Unexpected error: {ex.Message}");
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
                WriteResponse(message);
                return;
        }

        if (parts.Length != expectedParts)
        {
            WriteResponse(
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
                var before = CaptureIsAboutToBeCalled();
                try
                {
                    if (!TryIssueCommand("capture", () => _listener.Capture(parts[1], parts[2], parts[3])))
                    {
                        break;
                    }

                    var captureTimeoutMs = captureTimeoutSeconds * 1000;
                    var captureFailureReason = WaitForCaptureComplete(captureTimeoutMs);
                    if (captureFailureReason == null)
                    {
                        WriteResponse("CaptureFinish");
                    }
                    else
                    {
                        WriteCommandFailed(captureFailureReason);
                    }
                }
                finally
                {
                    CaptureCommandEnded(before);
                }
                break;
            }
            case StopKeyWord:
                TryIssueCommand("stop", _listener.Stop);
                break;
            case NewKeyWord:
                if (TryIssueCommand("new measurement", _listener.New))
                {
                    WriteResponse("NewMeasurementStarted");
                }
                break;
            case AcknowledgeAdjustingLightSetupFailedKeyWord:
                if (TryIssueCommand(
                        "acknowledgement of the failed light setup adjustment",
                        _listener.AcknowledgeAdjustingLightSetupFailed))
                {
                    lock (_stateLock)
                    {
                        _lightSetupFailure = null;
                    }
                    WriteResponse("AdjustingLightSetupFailedAcknowledged");
                }
                break;
            case CheckConnectionKeyWord:
                WriteResponse("ConnectionOK");
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
                    WriteResponse("AnalysisComplete");
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
                    const string noProvider = "Sphere height provider was not provided.";
                    Console.WriteLine($"{nameof(ExternalSerialSessionController)}:{WaitForSphereUpKeyWord}: {noProvider}");
                    WriteCommandFailed(noProvider);
                    break;
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
                while (!sphereHeightOk && !_disposed && s.ElapsedMilliseconds < sphereUpTimeoutMs)
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
                    WriteResponse("SphereIsUp");
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
                var imageFailure = _imageFailure;
                if (imageFailure != null)
                {
                    WriteCommandFailed($"The last image failed: {imageFailure}");
                }
                else
                {
                    WriteResponse("False");
                }
                break;
            }
            case GetLastErrorMessageKeyWord:
            {
                // The image failure if there is one, otherwise the light setup failure
                var lastErrorMessage = _imageFailure ?? _lightSetupFailure;
                var answer = string.IsNullOrWhiteSpace(lastErrorMessage) ? "No error" : lastErrorMessage;
                WriteResponse(answer);
                break;
            }
            default:
                WriteResponse(
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

    /// <summary>
    /// Sends one answer to the caller. The port is closed when the session is, and a command can still be
    /// on its way to answer at that moment. An exception here would escape onto the serial port's event
    /// thread, where nothing can report it and it takes the process down, so it is logged instead.
    /// </summary>
    private void WriteResponse(string response)
    {
        try
        {
            _serialPort.WriteLine(response);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
        {
            Console.WriteLine($"{nameof(ExternalSerialSessionController)}: Could not send the answer '{response}': {ex.Message}");
        }
    }

    /// <summary>Answers a command whose outcome is settled, so the caller knows not to send it again.</summary>
    private void WriteCommandFailed(string reason) => WriteResponse(CommandFailedPrefix + reason);

    private bool ParseTimeout(string timeoutToParse, string timeoutType, out int timeout)
    {
        var parseOk = int.TryParse(timeoutToParse, out timeout);
        if (!parseOk)
        {
            WriteResponse($"Unable to parse {timeoutType} timeout parameter. Parameter was: {timeoutToParse}.");
            return false;
        }

        // A negative timeout, or one that overflows when converted to milliseconds, makes the wait throw.
        var inRange = timeout >= 0 && timeout <= _maxTimeoutSeconds;
        if (!inRange)
        {
            WriteResponse($"The {timeoutType} timeout parameter must be between 0 and {_maxTimeoutSeconds} seconds. " +
                          $"Parameter was: {timeoutToParse}.");
        }
        return inRange;
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
    /// <returns>What the command replaced, for <see cref="CaptureCommandEnded"/>.</returns>
    private CaptureWaits CaptureIsAboutToBeCalled()
    {
        lock (_stateLock)
        {
            var before = new CaptureWaits(_imageFailure, _captureComplete.IsSet, _analysisComplete.IsSet);
            _imageFailure = null;
            _awaitingCapturing = true;
            _captureComplete.Reset();
            _analysisComplete.Reset();
            return before;
        }
    }

    /// <summary>
    /// Called when the capture command has been answered. If the session never started capturing, because it
    /// refused the command or the wait timed out first, the command changed nothing, so what it replaced is put
    /// back. An image from an earlier command can still be in flight, and its outcome and its waits must be
    /// left as they were: neither released as if it were finished, nor armed for an image that is not coming.
    /// </summary>
    private void CaptureCommandEnded(CaptureWaits before)
    {
        lock (_stateLock)
        {
            if (!_awaitingCapturing)
            {
                return;
            }

            _awaitingCapturing = false;

            // A failure reported in the meantime belongs to the earlier image and is the newer news, so keep it.
            _imageFailure = _imageFailure ?? before.ImageFailure;
            if (before.CaptureComplete)
            {
                _captureComplete.Set();
            }

            if (before.AnalysisComplete)
            {
                _analysisComplete.Set();
            }

            // The earlier image may have moved on while the waits were held back for this command.
            ReleaseWaitsFinishedIn(_state);
        }
    }

    /// <summary>The outcome and the waits of the previous image, as they were before a capture command</summary>
    private sealed class CaptureWaits
    {
        public CaptureWaits(string imageFailure, bool captureComplete, bool analysisComplete)
        {
            ImageFailure = imageFailure;
            CaptureComplete = captureComplete;
            AnalysisComplete = analysisComplete;
        }

        /// <summary>Why the previous image failed. Null if it did not.</summary>
        public string ImageFailure { get; }

        /// <summary>True if the wait for the capture was released</summary>
        public bool CaptureComplete { get; }

        /// <summary>True if the wait for the analysis was released</summary>
        public bool AnalysisComplete { get; }
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
        string failureBefore;
        bool completeBefore;
        lock (_stateLock)
        {
            failureBefore = _lightSetupFailure;
            completeBefore = _lightSetupAdjustmentComplete.IsSet;
            _lightSetupFailure = null;
            _lightSetupCommandInFlight = true;
            _awaitingAdjustingLightSetup = true;
            _lightSetupAdjustmentComplete.Reset();
        }

        try
        {
            if (!TryIssueCommand(what, command))
            {
                return;
            }

            var timeoutMs = timeoutSeconds * 1000;
            var failureReason = WaitFor(_lightSetupAdjustmentComplete, timeoutMs, what,
                nameof(HandleLightSetupCommand), () => _lightSetupFailure);
            if (failureReason == null)
            {
                WriteResponse(successResponse);
            }
            else
            {
                WriteCommandFailed(failureReason);
            }
        }
        finally
        {
            lock (_stateLock)
            {
                _lightSetupCommandInFlight = false;

                // The adjustment never started, so this command changed nothing: put back what it replaced.
                if (_awaitingAdjustingLightSetup)
                {
                    _awaitingAdjustingLightSetup = false;
                    _lightSetupFailure = _lightSetupFailure ?? failureBefore;
                    if (completeBefore)
                    {
                        _lightSetupAdjustmentComplete.Set();
                    }

                    ReleaseWaitsFinishedIn(_state);
                }
            }
        }
    }

    private string WaitForAnalysisToComplete(int timeoutMs) =>
        WaitFor(_analysisComplete, timeoutMs, "analysis to complete", nameof(WaitForAnalysisToComplete),
            () => _imageFailure);

    private string WaitForCaptureComplete(int timeoutMs) =>
        WaitFor(_captureComplete, timeoutMs, "capture to complete", nameof(WaitForCaptureComplete),
            () => _imageFailure);

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
    private string WaitFor(ManualResetEventSlim completed, int timeoutMs, string what, string caller,
        Func<string> getFailure)
    {
        var signaled = completed.Wait(timeoutMs);

        // Dispose releases the waits so they end. That is not the step finishing.
        if (_disposed)
        {
            var closed = $"The Session was closed while waiting for {what}.";
            Console.WriteLine($"{nameof(ExternalSerialSessionController)}:{caller}: {closed}");
            return closed;
        }

        if (!signaled)
        {
            var timedOut = $"Timed out after {timeoutMs}ms waiting for {what}. Session state is {_state}.";
            Console.WriteLine($"{nameof(ExternalSerialSessionController)}:{caller}: {timedOut}");
            return timedOut;
        }

        lock (_stateLock)
        {
            // Only the failure of the kind of step that was waited for counts. Read under the state lock.
            var failure = getFailure();
            if (failure == null)
            {
                return null;
            }

            var failed = $"Waited for {what}, but it failed: {failure}";
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
            // The session does not say what failed, so go by what is in flight. The state is checked as well,
            // for the adjustment whose caller has already timed out and gone.
            var lightSetupFailed = _lightSetupCommandInFlight || _state == SessionState.AdjustingLightSetup;

            // Whatever was in flight is finished, so release anyone waiting on it. Set after the reason is
            // recorded, so a released waiter is guaranteed to see it.
            if (lightSetupFailed)
            {
                _lightSetupFailure = errorMessage ?? string.Empty;

                // The session reports the reason while it is still adjusting, and only then moves on to
                // AdjustingLightSetupFailed. Answering already now would let the caller acknowledge the failure
                // before the session can accept that, so the wait is released when that state is reported.
                if (_state != SessionState.AdjustingLightSetup)
                {
                    _lightSetupAdjustmentComplete.Set();
                }
            }
            else
            {
                _imageFailure = errorMessage ?? string.Empty;
                _captureComplete.Set();
                _analysisComplete.Set();
            }
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
        // Set first, so a wait released below is answered as "the Session was closed" and not as a finished step.
        _disposed = true;
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