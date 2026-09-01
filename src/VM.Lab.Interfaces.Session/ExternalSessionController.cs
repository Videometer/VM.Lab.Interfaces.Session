namespace VM.Lab.Interfaces.Session;

/// <summary>Implement this to be an agent that controls sessions</summary>
public abstract class ExternalSessionController : IDisposable
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
    /// Called if an error happens, for example, something in the pipeline of capturing, analyzing, or saving of results, failed for the current image.
    /// Used to inform external controllers that something went wrong. 
    /// </summary>
    /// <param name="errorMessage">Error message explaining what failed</param>
    public abstract void ProvideErrorMessage(string errorMessage);

    /// <summary>Clean up internally used resources</summary>
    public virtual void Dispose()
    {
    }
}