namespace VM.Lab.Interfaces.Session
{
	/// <summary>Implement this to be an agent that controls sessions</summary>
	public abstract class SessionController : IDisposable
	{
		/// <summary>Implementer of session control commands</summary>
		protected readonly ISessionControllerListener _listener;

		/// <summary>An agent that controls sessions</summary>
		/// <param name="listener"></param>
		public SessionController(ISessionControllerListener listener)
		{
			_listener = listener;
		}

		/// <summary>
		/// Occurs when the state has changed
		/// Use to enable buttons / filter what signals are allowed.
		/// </summary>
		/// <param name="previousState"></param>
		/// <param name="newState"></param>
		public abstract void StateChanged(SessionState previousState, SessionState newState);

		/// <summary>
		/// Called when something in the pipeline of capturing, analysing, or saving of results, failed for the current image.
		/// Used to inform external controllers that something went wrong with the current image. 
		/// </summary>
		public abstract void LastImageFailed();

		/// <summary>Clean up internally used resources</summary>
		public virtual void Dispose()
		{
		}
	}
}
