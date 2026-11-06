namespace AirPlaySender.Core.Models;

public enum SessionState
{
    Idle = 0,
    Connecting = 1,
    Authenticating = 2,
    Streaming = 3,
    Degraded = 4,
    Reconnecting = 5,
    Error = 6,
    Stopped = 7
}
