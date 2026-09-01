namespace VM.Lab.Interfaces.Session;

/// <summary>Interface for controlling a session</summary>
public interface IExternalSessionControllerListener
{
    /// <summary>Start a new measurement</summary>
    /// <param name="id">ID of the sample</param>
    /// <param name="initials">Operator initials</param>
    /// <param name="comments">Operator comments</param>
    void Capture(string id, string initials, string comments);
    
    /// <summary>
    /// Starts a new measurement.
    /// Clears the GUI and makes ready for a fresh analysis with results saved to new files.
    /// </summary>
    void New();

    /// <summary>Stops the current session or image capture</summary>
    void Stop();

    /// <summary>Loads a specified light setup into the device</summary>
    /// <param name="lightSetupName">The name of the light setup file to load</param>
    void LoadLightSetup(string lightSetupName);

    /// <summary>Saves the active light setup</summary>
    /// <param name="lightSetupName">The name of the light setup file to save</param>
    void SaveLightSetup(string lightSetupName);

    /// <summary>Automatically adjusts the light setup to optimize imaging conditions</summary>
    void DoAutoLight();

    /// <summary>
    /// Acknowledge a failed light setup adjustment.
    /// The session itself does not clear a failed light setup adjustment: it leaves the session in
    /// <see cref="SessionState.AdjustingLightSetupFailed"/>, where it refuses further commands,
    /// until the controller that issued the adjustment acknowledges the failure.
    /// That way the external controller cannot continue to capture images without being aware
    /// of the light setup adjustment failure.
    /// </summary>
    void AcknowledgeAdjustingLightSetupFailed();
}