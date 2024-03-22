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
		/// True if the controller has a barcode reader that should be possible to read manually from session
		/// </summary>
		public abstract bool HasBarcodeReader { get; }

		/// <summary>Reads the barcode if supported</summary>
		/// <returns>Read barcode</returns>
		public abstract string ReadBarcode();
		
		public abstract void Dispose();
	}
}
