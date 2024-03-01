namespace VM.Lab.Interfaces.Session
{
	/// <summary>Enumerator with session states</summary>
	public enum SessionState
	{
		/// <summary>
		/// Undefined / No recipe loaded
		/// All commands disabled
		/// </summary>
		NONE = 0x00,

		/// <summary>
		/// Idle, ready to start new job in single frame mode
		/// ready to capture first image in single capture mode (When received, output folders should be generated)
		/// </summary>
		IDLE_SINGLE_FRAME = 0x01,

		/// <summary>Capturing, single frame mode. Capturing next frame</summary>
		CAPTURE_SINGLE_FRAME = 0x02,

		/// <summary>Analyzing the current frame in single capture mode, Objects can be moved now as the image is acquired</summary>
		ANALYZING_SINGLE_FRAME = 0x03,

		/// <summary>in single frame mode, waiting for signal for next image</summary>
		WAIT_NEXT_SINGLE_FRAME = 0x04,

		/// <summary>Idle, Continuously capturing mode</summary>
		IDLE_CONTINUOUSLY = 0x10,

		/// <summary>Capturing, Continuously capturing mode</summary>
		CAPTURE_CONTINUOUSLY = 0x20,

		/// <summary>
		/// Analyzing the current frame in continuously capture mode.
		/// Objects could be moved now as the image is acquired, 
		/// It is probably better to use single capture mode if objects are moved and let the external controller control the flow</summary>
		ANALYZING_CONTINUOUSLY = 0x30,

		/// <summary>
		/// About to pause (Pause signal send)
		/// Continuously capturing mode
		/// </summary>
		ABOUT_TO_PAUSE_CONTINUOUSLY = 0x40,

		/// <summary>
		/// Paused, Continuously capturing mode
		/// </summary>
		PAUSE_CONTINUOUSLY = 0x50,

		/// <summary>
		/// Done, Continuously capturing mode
		/// </summary>
		DONE_CONTINUOUSLY = 0x60
	}
}
