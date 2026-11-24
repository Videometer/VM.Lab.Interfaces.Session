using System.Diagnostics;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using VM.Lab.Interfaces.Session;

namespace VM.Lab.Session.RestSessionController;

/// <summary>
/// Concrete implementation of an external session controller that is controlled by an external device that
/// communicates with this controller using a REST API over HTTP.
/// <para>
/// The API listens on all network interfaces on port 5080, under the path /api/v1, and has no authentication.
/// Commands that take time answer when the step has finished, failed or timed out.
/// </para>
/// <para>
/// Windows only lets an administrator, or a user the address has been reserved for, listen on the network.
/// Without that the API still starts, but only answers requests from the PC itself. See the README.
/// </para>
/// </summary>
public class ExternalRestSessionController : ExternalSessionController, INeedSphereHeightProvider
{
    private const int _port = 5080;
    private const string _routePrefix = "/api/v1";
    private const int _maxTimeoutSeconds = 86400;
    private const float _minimumSafeSphereHeight = 82;
    private const int _sphereHeightPollIntervalMs = 500;
    private const int _accessDeniedErrorCode = 5;
    private const int _disposeGraceMs = 2000;
    private const string _captureWasStopped = "The capture was stopped before an image was captured.";
    private const string _get = "GET";
    private const string _post = "POST";

    /// <summary>
    /// Relaxed escaping keeps quotes in messages readable. The answers are only ever sent as JSON, never
    /// embedded in HTML, which is the case the default escaping guards against.
    /// </summary>
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly HttpListener _httpListener;

    /// <summary>
    /// True when Windows did not allow listening on the network, so only requests from this PC are answered.
    /// Registering the address localhost is not enough to ensure that: Windows still accepts a request from
    /// another PC that names localhost as the host, so every request is checked as well.
    /// </summary>
    private readonly bool _localOnly;
    private readonly Dictionary<string, Func<HttpListenerRequest, RestAnswer>> _routes;
    private readonly object _stateLock = new object();
    private readonly ManualResetEventSlim _captureComplete = new ManualResetEventSlim(false);
    private readonly ManualResetEventSlim _analysisComplete = new ManualResetEventSlim(false);
    private readonly ManualResetEventSlim _lightSetupAdjustmentComplete = new ManualResetEventSlim(false);

    /// <summary>
    /// Held by the command that is currently changing what the session does.
    /// Stop, the waits and the queries do not take it, so they can be called while such a command is in flight.
    /// </summary>
    private readonly SemaphoreSlim _commandInProgress = new SemaphoreSlim(1, 1);

    private SessionState _state;
    private ISphereHeightProvider _sphereHeightProvider;

    /// <summary>Set by <see cref="Dispose"/>. A wait that ends because of it is answered as closed, not as finished.</summary>
    private volatile bool _disposed;

    /// <summary>Requests accepted but not answered yet. <see cref="Dispose"/> gives them a moment to be answered.</summary>
    private int _requestsInFlight;

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

    /// <summary>
    /// Constructs the external session controller and starts the REST API.
    /// This method is called internally by the VideometerLab software.
    /// </summary>
    /// <param name="listener">The session listener that is use to provide commands to the session</param>
    public ExternalRestSessionController(IExternalSessionControllerListener listener) : base(listener)
    {
        _routes = new Dictionary<string, Func<HttpListenerRequest, RestAnswer>>(StringComparer.OrdinalIgnoreCase)
        {
            [RouteKey(_get, "/connection")] = _ => Succeeded("ConnectionOK"),
            [RouteKey(_get, "/state")] = _ => GetState(),
            [RouteKey(_get, "/last-image")] = _ => GetLastImage(),
            [RouteKey(_post, "/capture")] = request => RunOneCommandAtATime(() => Capture(ReadBody<CaptureRequest>(request))),
            [RouteKey(_post, "/new")] = _ => RunOneCommandAtATime(New),
            [RouteKey(_post, "/stop")] = _ => Stop(),
            [RouteKey(_post, "/wait-for-analysis-complete")] = request => WaitForAnalysisComplete(ReadBody<TimeoutRequest>(request)),
            [RouteKey(_post, "/wait-for-sphere-up")] = request => WaitForSphereUp(ReadBody<TimeoutRequest>(request)),
            [RouteKey(_post, "/light-setup/load")] = request => RunOneCommandAtATime(() => LoadLightSetup(ReadBody<LightSetupRequest>(request))),
            [RouteKey(_post, "/light-setup/save")] = request => RunOneCommandAtATime(() => SaveLightSetup(ReadBody<LightSetupRequest>(request))),
            [RouteKey(_post, "/light-setup/auto")] = request => RunOneCommandAtATime(() => DoAutoLight(ReadBody<TimeoutRequest>(request))),
            [RouteKey(_post, "/light-setup/acknowledge-failed")] = _ => RunOneCommandAtATime(AcknowledgeAdjustingLightSetupFailed)
        };

        _httpListener = StartListening(out _localOnly);

        var acceptThread = new Thread(AcceptRequests)
        {
            IsBackground = true,
            Name = nameof(ExternalRestSessionController)
        };
        acceptThread.Start();
    }

    private static string RouteKey(string method, string path) => $"{method} {_routePrefix}{path}";

    /// <summary>
    /// Starts listening on all network interfaces, or only on the PC itself if Windows does not allow the former.
    /// </summary>
    /// <param name="localOnly">True if only requests from this PC are to be answered</param>
    private static HttpListener StartListening(out bool localOnly)
    {
        var networkPrefix = $"http://+:{_port}{_routePrefix}/";
        var localPrefix = $"http://localhost:{_port}{_routePrefix}/";

        try
        {
            var networkListener = Listen(networkPrefix);
            localOnly = false;
            Console.WriteLine($"{nameof(ExternalRestSessionController)} is listening on {networkPrefix}");
            return networkListener;
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == _accessDeniedErrorCode)
        {
            Console.WriteLine($"{nameof(ExternalRestSessionController)}: Windows denied listening on {networkPrefix}, " +
                              "so the REST API can only be reached from this PC. To allow access from the network, " +
                              "run this once as administrator and restart Session: " +
                              $"netsh http add urlacl url={networkPrefix} sddl=\"D:(A;;GX;;;WD)\"");
        }

        var localListener = Listen(localPrefix);
        localOnly = true;
        Console.WriteLine($"{nameof(ExternalRestSessionController)} is listening on {localPrefix}");
        return localListener;
    }

    private static HttpListener Listen(string prefix)
    {
        var httpListener = new HttpListener();
        httpListener.Prefixes.Add(prefix);
        httpListener.Start();
        return httpListener;
    }

    /// <summary>Hands every incoming request to its own thread, so a blocking command does not hold up the others.</summary>
    private void AcceptRequests()
    {
        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = _httpListener.GetContext();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                // The listener was stopped by Dispose
                return;
            }

            Interlocked.Increment(ref _requestsInFlight);
            Task.Run(() => HandleRequest(context));
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        try
        {
            var answer = GetAnswer(context.Request);
            var body = JsonSerializer.SerializeToUtf8Bytes(answer.Body, answer.Body.GetType(), _jsonOptions);
            context.Response.StatusCode = (int)answer.StatusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            if (answer.Allow != null)
            {
                context.Response.AddHeader("Allow", answer.Allow);
            }
            context.Response.ContentLength64 = body.Length;
            context.Response.OutputStream.Write(body, 0, body.Length);
            context.Response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException)
        {
            // The caller has gone away, or the listener was stopped, so there is no one left to answer.
            Console.WriteLine($"{nameof(ExternalRestSessionController)}: Could not send the answer: {ex.Message}");
        }
        finally
        {
            Interlocked.Decrement(ref _requestsInFlight);
        }
    }

    private RestAnswer GetAnswer(HttpListenerRequest request)
    {
        var path = request.Url.AbsolutePath.TrimEnd('/');
        var routeKey = $"{request.HttpMethod} {path}";
        Console.WriteLine($"{nameof(ExternalRestSessionController)} received: {routeKey}");

        if (_disposed)
        {
            return SessionClosed("The Session is closing.");
        }

        if (_localOnly && !request.IsLocal)
        {
            return Answer(HttpStatusCode.Forbidden,
                $"Requests from other PCs are not allowed. Request came from {request.RemoteEndPoint?.Address}.");
        }

        if (!_routes.TryGetValue(routeKey, out var route))
        {
            var allowedMethods = GetAllowedMethods(path);
            if (allowedMethods.Length == 0)
            {
                return Answer(HttpStatusCode.NotFound, $"Unknown command: {routeKey}");
            }

            var allow = string.Join(", ", allowedMethods);
            var wrongMethod = Answer(HttpStatusCode.MethodNotAllowed,
                $"{path} must be called with {allow}, not {request.HttpMethod}.");
            return wrongMethod with { Allow = allow };
        }

        try
        {
            return route(request);
        }
        catch (JsonException ex)
        {
            return Answer(HttpStatusCode.BadRequest, $"The body is not valid JSON for this command: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Answer(HttpStatusCode.InternalServerError, $"Unexpected error: {ex.Message}");
        }
    }

    /// <returns>The HTTP methods the path can be called with. Empty if the path is not a command.</returns>
    private string[] GetAllowedMethods(string path)
    {
        var pathSuffix = " " + path;
        return _routes.Keys
            .Where(routeKey => routeKey.EndsWith(pathSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(routeKey => routeKey.Substring(0, routeKey.Length - pathSuffix.Length))
            .ToArray();
    }

    /// <returns>The body of the request, or null if it has none.</returns>
    private static T ReadBody<T>(HttpListenerRequest request) where T : class
    {
        if (!request.HasEntityBody)
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(request.InputStream, _jsonOptions);
    }

    /// <summary>Occurs when a session state has changed</summary>
    /// <param name="newState"></param>
    public override void StateChanged(SessionState newState)
    {
        lock (_stateLock)
        {
            Console.WriteLine($"In {nameof(ExternalRestSessionController)}.{nameof(StateChanged)} to {newState}.");
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

    /// <summary>
    /// Called if an error happens, for example, something in the pipeline of capturing, analyzing, or saving of results, failed for the current image.
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

    private RestAnswer GetState()
    {
        lock (_stateLock)
        {
            return new RestAnswer(HttpStatusCode.OK, new StateResponse(_state.ToString()));
        }
    }

    private RestAnswer GetLastImage()
    {
        lock (_stateLock)
        {
            return new RestAnswer(HttpStatusCode.OK, new LastImageResponse(_imageFailure != null, _imageFailure));
        }
    }

    private RestAnswer Capture(CaptureRequest request)
    {
        var invalid = ValidateTimeout(request, request?.TimeoutSeconds);
        if (invalid != null)
        {
            return invalid;
        }

        var id = request.Id ?? string.Empty;
        var initials = request.Initials ?? string.Empty;
        var comments = request.Comments ?? string.Empty;

        var before = CaptureIsAboutToBeCalled();
        try
        {
            var refused = TryIssueCommand("capture", () => _listener.Capture(id, initials, comments));
            if (refused != null)
            {
                return refused;
            }

            return WaitFor(_captureComplete, request.TimeoutSeconds, "capture to complete", "CaptureFinish",
                () => _imageFailure);
        }
        finally
        {
            CaptureCommandEnded(before);
        }
    }

    private RestAnswer New()
    {
        var refused = TryIssueCommand("new measurement", _listener.New);
        return refused ?? Succeeded("NewMeasurementStarted");
    }

    private RestAnswer Stop()
    {
        var refused = TryIssueCommand("stop", _listener.Stop);
        return refused ?? Succeeded("Stopped");
    }

    private RestAnswer AcknowledgeAdjustingLightSetupFailed()
    {
        var refused = TryIssueCommand(
            "acknowledgement of the failed light setup adjustment",
            _listener.AcknowledgeAdjustingLightSetupFailed);
        if (refused != null)
        {
            return refused;
        }

        lock (_stateLock)
        {
            _lightSetupFailure = null;
        }

        return Succeeded("AdjustingLightSetupFailedAcknowledged");
    }

    private RestAnswer WaitForAnalysisComplete(TimeoutRequest request)
    {
        var invalid = ValidateTimeout(request, request?.TimeoutSeconds);
        if (invalid != null)
        {
            return invalid;
        }

        return WaitFor(_analysisComplete, request.TimeoutSeconds, "analysis to complete", "AnalysisComplete",
            () => _imageFailure);
    }

    private RestAnswer WaitForSphereUp(TimeoutRequest request)
    {
        var invalid = ValidateTimeout(request, request?.TimeoutSeconds);
        if (invalid != null)
        {
            return invalid;
        }

        if (_sphereHeightProvider is null)
        {
            return Answer(HttpStatusCode.ServiceUnavailable, "Sphere height provider was not provided.");
        }

        var timeout = TimeSpan.FromSeconds(request.TimeoutSeconds);
        var stopwatch = Stopwatch.StartNew();
        var loggedOnce = false;
        while (true)
        {
            if (_disposed)
            {
                return SessionClosed("The Session was closed while waiting for the sphere to move up.");
            }

            var sphereHeight = _sphereHeightProvider.GetSphereHeight();
            if (sphereHeight > _minimumSafeSphereHeight)
            {
                return Succeeded("SphereIsUp");
            }

            if (!loggedOnce)
            {
                loggedOnce = true;
                Console.WriteLine($"{nameof(ExternalRestSessionController)}:{nameof(WaitForSphereUp)}: " +
                                  $"Sphere height was {sphereHeight} but must be at minimum {_minimumSafeSphereHeight}. " +
                                  "Waiting for sphere to move up.");
            }

            if (stopwatch.Elapsed >= timeout)
            {
                return TimedOut($"Failed waiting for sphere to move up. Waited {request.TimeoutSeconds}s.");
            }

            Thread.Sleep(_sphereHeightPollIntervalMs);
        }
    }

    private RestAnswer LoadLightSetup(LightSetupRequest request)
    {
        var invalid = ValidateLightSetupRequest(request);
        if (invalid != null)
        {
            return invalid;
        }

        var lightSetupName = request.LightSetupName;
        return AdjustLightSetup($"loading of light setup '{lightSetupName}'",
            () => _listener.LoadLightSetup(lightSetupName), request.TimeoutSeconds, "LightSetupLoaded");
    }

    private RestAnswer SaveLightSetup(LightSetupRequest request)
    {
        var invalid = ValidateLightSetupRequest(request);
        if (invalid != null)
        {
            return invalid;
        }

        var lightSetupName = request.LightSetupName;
        return AdjustLightSetup($"saving of light setup '{lightSetupName}'",
            () => _listener.SaveLightSetup(lightSetupName), request.TimeoutSeconds, "LightSetupSaved");
    }

    private RestAnswer DoAutoLight(TimeoutRequest request)
    {
        var invalid = ValidateTimeout(request, request?.TimeoutSeconds);
        if (invalid != null)
        {
            return invalid;
        }

        return AdjustLightSetup("auto light", _listener.DoAutoLight, request.TimeoutSeconds, "AutoLightComplete");
    }

    /// <summary>Issues one light-setup command and answers once the session has finished with it.</summary>
    private RestAnswer AdjustLightSetup(string what, Action command, int timeoutSeconds, string successMessage)
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
            var refused = TryIssueCommand(what, command);
            if (refused != null)
            {
                return refused;
            }

            return WaitFor(_lightSetupAdjustmentComplete, timeoutSeconds, what, successMessage,
                () => _lightSetupFailure);
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
                    _lightSetupFailure ??= failureBefore;
                    if (completeBefore)
                    {
                        _lightSetupAdjustmentComplete.Set();
                    }

                    ReleaseWaitsFinishedIn(_state);
                }
            }
        }
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
            _imageFailure ??= before.ImageFailure;
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
    /// <param name="ImageFailure">Why the previous image failed. Null if it did not.</param>
    /// <param name="CaptureComplete">True if the wait for the capture was released</param>
    /// <param name="AnalysisComplete">True if the wait for the analysis was released</param>
    private sealed record CaptureWaits(string ImageFailure, bool CaptureComplete, bool AnalysisComplete);

    /// <summary>
    /// Runs a command that changes what the session does, refusing it if another one is still in flight.
    /// </summary>
    private RestAnswer RunOneCommandAtATime(Func<RestAnswer> command)
    {
        if (!_commandInProgress.Wait(0))
        {
            return Answer(HttpStatusCode.Locked, "Already processing previous command. New command ignored.");
        }

        try
        {
            return command();
        }
        finally
        {
            _commandInProgress.Release();
        }
    }

    /// <summary>Passes one command to the session.</summary>
    /// <returns>Null if the session accepted the command, otherwise the answer saying why it did not.</returns>
    private static RestAnswer TryIssueCommand(string what, Action command)
    {
        try
        {
            command();
            return null;
        }
        catch (InvalidOperationException ex)
        {
            // How the session refuses a command that is not valid in its current state
            return Answer(HttpStatusCode.Conflict, $"Could not start {what}: {ex.Message}");
        }
        catch (NotImplementedException ex)
        {
            return Answer(HttpStatusCode.NotImplemented, $"The Session does not support {what}: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Answer(HttpStatusCode.InternalServerError, $"Could not start {what}: {ex.Message}");
        }
    }

    /// <summary>
    /// Blocks until <paramref name="completed"/> is signaled — by <see cref="StateChanged"/> on success, or by
    /// <see cref="ProvideErrorMessage"/> on failure.
    /// <para>
    /// Waiting on the event rather than polling the state matters for correctness, not just for efficiency:
    /// the session can pass through a state faster than a poll loop can observe it, whereas a signaled event
    /// is still signaled whenever we get round to waiting on it.
    /// </para>
    /// </summary>
    /// <param name="completed">Signaled when the step is over</param>
    /// <param name="timeoutSeconds">How long to wait</param>
    /// <param name="what">What is waited for, for the messages</param>
    /// <param name="successMessage">Message to answer with if the step succeeded</param>
    /// <param name="getFailure">Reads why this kind of step failed, null if it did not. Called under the state lock.</param>
    /// <returns>The answer to the caller. Timing out and failing are reported separately.</returns>
    private RestAnswer WaitFor(ManualResetEventSlim completed, int timeoutSeconds, string what, string successMessage,
        Func<string> getFailure)
    {
        var signaled = completed.Wait(TimeSpan.FromSeconds(timeoutSeconds));

        // Dispose releases the waits so they end. That is not the step finishing.
        if (_disposed)
        {
            return SessionClosed($"The Session was closed while waiting for {what}.");
        }

        if (!signaled)
        {
            return TimedOut($"Timed out after {timeoutSeconds}s waiting for {what}. Session state is {_state}.");
        }

        lock (_stateLock)
        {
            var failure = getFailure();
            if (failure == null)
            {
                return Succeeded(successMessage);
            }

            return Answer(HttpStatusCode.InternalServerError, $"Waited for {what}, but it failed: {failure}");
        }
    }

    private static RestAnswer ValidateLightSetupRequest(LightSetupRequest request)
    {
        var invalid = ValidateTimeout(request, request?.TimeoutSeconds);
        if (invalid != null)
        {
            return invalid;
        }

        if (string.IsNullOrWhiteSpace(request.LightSetupName))
        {
            return Answer(HttpStatusCode.BadRequest, "lightSetupName is required.");
        }

        return null;
    }

    /// <returns>Null if the request is usable, otherwise the answer saying why it is not.</returns>
    private static RestAnswer ValidateTimeout(object request, int? timeoutSeconds)
    {
        if (request is null)
        {
            return Answer(HttpStatusCode.BadRequest, "A JSON body is required.");
        }

        if (timeoutSeconds is null or < 1 or > _maxTimeoutSeconds)
        {
            return Answer(HttpStatusCode.BadRequest,
                $"timeoutSeconds must be between 1 and {_maxTimeoutSeconds}. Parameter was: {timeoutSeconds}.");
        }

        return null;
    }

    private static RestAnswer Succeeded(string message) =>
        new RestAnswer(HttpStatusCode.OK, new CommandResponse(true, message));

    private static RestAnswer TimedOut(string reason) => Answer(HttpStatusCode.GatewayTimeout, reason);

    private static RestAnswer SessionClosed(string reason) => Answer(HttpStatusCode.ServiceUnavailable, reason);

    private static RestAnswer Answer(HttpStatusCode statusCode, string reason)
    {
        Console.WriteLine($"{nameof(ExternalRestSessionController)}: {reason}");
        return new RestAnswer(statusCode, new CommandResponse(false, reason));
    }

    /// <summary>Clean up internally used resources</summary>
    public override void Dispose()
    {
        // Release anyone still blocked, so the wait ends rather than throwing. The flag is set first, so the
        // released requests are answered as "the Session was closed" and not as a finished step.
        _disposed = true;
        _captureComplete.Set();
        _analysisComplete.Set();
        _lightSetupAdjustmentComplete.Set();

        // Closing the listener drops the requests in flight unanswered, so give them a moment to be answered.
        SpinWait.SpinUntil(() => Volatile.Read(ref _requestsInFlight) == 0, _disposeGraceMs);
        _httpListener.Close();

        _captureComplete.Dispose();
        _analysisComplete.Dispose();
        _lightSetupAdjustmentComplete.Dispose();
        _commandInProgress.Dispose();
    }
}
