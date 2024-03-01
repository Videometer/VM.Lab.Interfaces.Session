namespace VM.Lab.Interfaces.Session
{
	/// <summary>Interface for controlling a session</summary>
	public interface ISessionControllerListener
	{
		/// <summary>Start a new measurement</summary>
		/// <param name="id">ID of the sample</param>
		/// <param name="initials">Operator initials</param>
		/// <param name="comments">Operator comments</param>
		/// <param name="suffixByTimestamp">When true, session should suffix the sample id with timestamp</param>
		void Capture(string id, string initials, string comments, bool suffixByTimestamp);

		/// <summary>Pause the current capture process</summary>
		void Pause();

		/// <summary>Finish the measurement</summary>
		void Finish();

		/// <summary>Starts a new measurement</summary>
		void New();
	}
}
