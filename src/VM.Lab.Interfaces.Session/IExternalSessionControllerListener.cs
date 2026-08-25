namespace VM.Lab.Interfaces.Session;

/// <summary>Interface for controlling a session</summary>
public interface ISessionControllerListener
{
    /// <summary>Start a new measurement</summary>
    /// <param name="id">ID of the sample</param>
    /// <param name="initials">Operator initials</param>
    /// <param name="comments">Operator comments</param>
    void Capture(string id, string initials, string comments);
    
    /// <summary>Starts a new measurement</summary>
    void New();
    
    /// <summary>Loads a specified light setup configuration into the device</summary>
    /// <param name="lightSetupName">The name of the light setup file to load</param>
    void LoadLightSetup(string lightSetupName);

    /// <summary>Saves the specified light setup configuration</summary>
    /// <param name="lightSetupName">The name of the light setup file to save</param>
    void SaveLightSetup(string lightSetupName);

    /// <summary>Automatically adjusts the light setup to optimize imaging conditions</summary>
    void DoAutoLight();
}