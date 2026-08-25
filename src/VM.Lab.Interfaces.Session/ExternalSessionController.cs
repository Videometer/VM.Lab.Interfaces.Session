namespace VM.Lab.Interfaces.Session;

/// <summary>Implement this to be an agent that controls sessions</summary>
public abstract class ExternalSessionController
{
    /// <summary>Implementer of session control commands</summary>
    protected readonly IExternalSessionControllerListener _listener;

    /// <summary>An agent that controls sessions</summary>
    /// <param name="listener"></param>
    protected ExternalSessionController(IExternalSessionControllerListener listener)
    {
        _listener = listener;
    }

    /// <summary>Occurs when the state has changed</summary>
    /// <param name="newState"></param>
    public abstract void StateChanged(SessionState newState);

    /// <summary>
    /// Called when something in the pipeline of capturing, analysing, or saving of results, failed for the current image.
    /// Used to inform external controllers that something went wrong with the current image. 
    /// </summary>
    /// <param name="errorMessage">Error message explaining why the analysis failed</param>
    public abstract void LastImageFailed(string errorMessage); // TODO: Rename to ProvideErrorMessage? Also called if connect fails
}