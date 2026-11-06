namespace AirPlaySender.Core.Models;

public sealed class ReceiverDevice
{
    public required string Name { get; init; }
    public required string Host { get; init; }
    public int Port { get; init; } = 7000;
    public bool RequiresPassword { get; init; }
    public string? Model { get; init; }
    public string? DeviceId { get; init; }

    public string Endpoint => $"{Host}:{Port}";
    public string DisplayName => RequiresPassword ? $"{Name} (password)" : Name;

    public override string ToString() => $"{DisplayName}  [{Endpoint}]";
}
