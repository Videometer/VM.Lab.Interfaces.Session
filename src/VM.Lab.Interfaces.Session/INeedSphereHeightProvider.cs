namespace VM.Lab.Interfaces.Session;

/// <summary>
/// Interface implemented by external session controllers that need to know about the VideometerLab sphere height.
/// </summary>
public interface INeedSphereHeightProvider
{
    /// <summary>
    /// Method used to provide the concrete implementation of <see cref="ISphereHeightProvider"/>.
    /// </summary>
    /// <param name="provider">The concrete implementation of <see cref="ISphereHeightProvider"/>.</param>
    void ProvideSphereHeightProvider(ISphereHeightProvider provider);
}