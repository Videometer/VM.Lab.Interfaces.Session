// ReSharper disable InconsistentNaming
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace VM.Lab.Interfaces.Session;

/// <summary>
/// Enumerator with session states.
/// See the session state machine visualization graph.
/// </summary>
public enum SessionState
{
    Idle,
    Connecting,
    Ready,
    AdjustingLightSetup,
    AdjustingLightSetupFailed,
    Capturing,
    Processing,
    Waiting,
    Error
}

