namespace VM.Lab.Interfaces.Session;

/// <summary>
/// Interface implemented by an external controller of session used to adjust light setup fo the currently used imaging device
/// </summary>
public interface ILightSetupAdjuster
{
    /// <summary>Loads a specified light setup configuration into the device</summary>
    /// <param name="lightSetupName">The name of the light setup file to load</param>
    void LoadLightSetup(string lightSetupName);

    /// <summary>Saves the specified light setup configuration</summary>
    /// <param name="lightSetupName">The name of the light setup file to save</param>
    void SaveLightSetup(string lightSetupName);

    /// <summary>Automatically adjusts the light setup to optimize imaging conditions</summary>
    void DoAutoLight();
}