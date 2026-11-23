using System.Net;

namespace VM.Lab.Session.RestSessionController;

/// <summary>What to send back to the caller</summary>
/// <param name="StatusCode">HTTP status code</param>
/// <param name="Body">Object to send as the JSON body</param>
internal sealed record RestAnswer(HttpStatusCode StatusCode, object Body);

/// <summary>Body of POST capture</summary>
/// <param name="Id">ID of the sample</param>
/// <param name="Initials">Operator initials</param>
/// <param name="Comments">Operator comments</param>
/// <param name="TimeoutSeconds">How long to wait for the image to be captured</param>
internal sealed record CaptureRequest(string Id, string Initials, string Comments, int TimeoutSeconds);

/// <summary>Body of the commands that only carry a timeout</summary>
/// <param name="TimeoutSeconds">How long to wait for the step to finish</param>
internal sealed record TimeoutRequest(int TimeoutSeconds);

/// <summary>Body of POST light-setup/load and POST light-setup/save</summary>
/// <param name="LightSetupName">The name of the light setup file</param>
/// <param name="TimeoutSeconds">How long to wait for the light setup adjustment to finish</param>
internal sealed record LightSetupRequest(string LightSetupName, int TimeoutSeconds);

/// <summary>Answer to every command</summary>
/// <param name="Success">True if the command did what it was asked to</param>
/// <param name="Message">What happened, or why it did not</param>
internal sealed record CommandResponse(bool Success, string Message);

/// <summary>Answer to GET state</summary>
/// <param name="State">Name of the current session state</param>
internal sealed record StateResponse(string State);

/// <summary>Answer to GET last-image</summary>
/// <param name="Failed">True if the last image, or the last light setup adjustment, failed</param>
/// <param name="ErrorMessage">Why it failed. Null if it did not.</param>
internal sealed record LastImageResponse(bool Failed, string ErrorMessage);
