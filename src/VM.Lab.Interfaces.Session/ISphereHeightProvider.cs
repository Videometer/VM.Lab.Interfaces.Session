namespace VM.Lab.Interfaces.Session;

/// <summary>
/// Interface implemented by someone that is able to provide the current height of the VideometerLab sphere.
/// </summary>
public interface ISphereHeightProvider
{
    /// <summary>
    /// Returns the current height of the VideometerLab sphere.
    /// Unit is in in millimeters above the base plate where samples are placed.
    /// </summary>
    int GetSphereHeight();
}