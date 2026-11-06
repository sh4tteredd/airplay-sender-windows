using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AirPlaySender.Core.Models;
using Makaretu.Dns;

namespace AirPlaySender.Core.Services;

/// <summary>
/// Small DNS-SD browser for classic AirPlay/RAOP receivers.
///
/// Makaretu's MulticastService is convenient but on multi-homed Windows
/// machines it can bind/send on the wrong adapter (VMware/VPN). This browser
/// uses a raw UDP socket, joins the mDNS group on every active IPv4 adapter,
/// and parses responses with Makaretu's DNS wire reader.
/// </summary>
public sealed class DiscoveryService : IDisposable
{
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");
    private const int MdnsPort = 5353;

    private readonly ConcurrentDictionary<string, ReceiverDevice> _devices = new();
    private readonly object _gate = new();
    private Socket? _socket;
    private Thread? _thread;
    private volatile bool _running;
    private DateTime _nextQuery = DateTime.MinValue;
    private int _queryBurst = 3;

    public event EventHandler<ReceiverDevice>? DeviceFound;
    public event EventHandler<string>? DiscoveryError;

    public IReadOnlyCollection<ReceiverDevice> Devices => _devices.Values.ToList();

    public void Start()
    {
        lock (_gate)
        {
            if (_running) return;
            try
            {
                _socket = CreateSocket();
                _running = true;
                _thread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "AirPlay mDNS discovery"
                };
                _thread.Start();
                SendQueries();
                Logger.Instance.Info("mDNS discovery started for _raop._tcp and _airplay._tcp");
            }
            catch (Exception ex)
            {
                _running = false;
                _socket?.Dispose();
                _socket = null;
                Logger.Instance.Error("mDNS discovery failed to start", ex);
                DiscoveryError?.Invoke(this, ex.Message);
            }
        }
    }

    public void Refresh()
    {
        if (!_running)
        {
            Start();
            return;
        }
        _queryBurst = 3;
        _nextQuery = DateTime.MinValue;
    }

    private static Socket CreateSocket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ReceiveTimeout = 500
        };
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));
        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, false);

        foreach (var address in GetLocalIPv4Addresses())
        {
            try
            {
                socket.SetSocketOption(
                    SocketOptionLevel.IP,
                    SocketOptionName.AddMembership,
                    new MulticastOption(MulticastAddress, address));
            }
            catch (Exception ex)
            {
                Logger.Instance.Warn($"mDNS join failed on {address}: {ex.Message}");
            }
        }

        return socket;
    }

    private static List<IPAddress> GetLocalIPv4Addresses()
    {
        var result = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(unicast.Address)) continue;
                    if (!result.Contains(unicast.Address)) result.Add(unicast.Address);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Warn($"Could not enumerate network interfaces: {ex.Message}");
        }

        if (result.Count == 0) result.Add(IPAddress.Any);
        return result;
    }

    private void ReceiveLoop()
    {
        var buffer = new byte[9000];
        while (_running)
        {
            if (DateTime.UtcNow >= _nextQuery)
            {
                SendQueries();
                if (_queryBurst > 1)
                {
                    _queryBurst--;
                    _nextQuery = DateTime.UtcNow.AddSeconds(1);
                }
                else
                {
                    _nextQuery = DateTime.UtcNow.AddSeconds(15);
                }
            }

            try
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                var length = _socket!.ReceiveFrom(buffer, ref remote);
                if (length <= 0) continue;
                if (remote is IPEndPoint ep && IsLocalAddress(ep.Address)) continue;

                var message = (Message)new Message().Read(
                    new WireReader(new MemoryStream(buffer, 0, length)));
                ProcessMessage(message);
            }
            catch (SocketException)
            {
                // Receive timeout; keep polling.
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Instance.Warn($"mDNS receive error: {ex.Message}");
            }
        }
    }

    private static bool IsLocalAddress(IPAddress address)
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Any(a => a.Address.Equals(address));
        }
        catch
        {
            return false;
        }
    }

    private void SendQueries()
    {
        var socket = _socket;
        if (socket is null) return;

        foreach (var service in new[] { "_raop._tcp.local", "_airplay._tcp.local" })
        {
            try
            {
                var query = BuildQuery(service);
                foreach (var address in GetLocalIPv4Addresses())
                {
                    try
                    {
                        if (!address.Equals(IPAddress.Any))
                        {
                            socket.SetSocketOption(
                                SocketOptionLevel.IP,
                                SocketOptionName.MulticastInterface,
                                BitConverter.ToInt32(address.GetAddressBytes(), 0));
                        }
                        socket.SendTo(query, new IPEndPoint(MulticastAddress, MdnsPort));
                    }
                    catch (Exception ex)
                    {
                        Logger.Instance.Warn($"mDNS query failed on {address}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Warn($"mDNS query build failed: {ex.Message}");
            }
        }
    }

    private static byte[] BuildQuery(string service)
    {
        var message = new Message
        {
            Id = 0,
            QR = false,
            Opcode = MessageOperation.Query
        };
        message.Questions.Add(new Question
        {
            Name = service,
            Type = DnsType.PTR,
            Class = DnsClass.IN
        });

        var stream = new MemoryStream();
        var writer = new WireWriter(stream);
        message.Write(writer);
        return stream.ToArray();
    }

    private void ProcessMessage(Message message)
    {
        var records = message.Answers.Concat(message.AdditionalRecords).ToList();
        foreach (var ptr in records.OfType<PTRRecord>())
        {
            var service = ptr.Name.ToString();
            if (!service.Contains("_raop._tcp", StringComparison.OrdinalIgnoreCase)
                && !service.Contains("_airplay._tcp", StringComparison.OrdinalIgnoreCase))
                continue;

            var instance = ptr.DomainName;
            var instanceText = instance.ToString();
            var srv = records.OfType<SRVRecord>()
                .FirstOrDefault(r => NamesEqual(r.Name, instance));
            if (srv is null) continue;

            var txt = records.OfType<TXTRecord>()
                .FirstOrDefault(r => NamesEqual(r.Name, instance));
            var properties = ParseTxt(txt);
            var address = records.OfType<AddressRecord>()
                .Where(r => NamesEqual(r.Name, srv.Target))
                .Select(r => r.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (address is null)
            {
                // Some _airplay responses omit the address; the _raop response
                // for the same device normally carries it. Keep a debug breadcrumb.
                Logger.Instance.Info($"mDNS instance without IPv4 address: {instanceText}");
                continue;
            }

            var displaySource = instance.Labels.FirstOrDefault()
                                ?? properties.GetValueOrDefault("am")
                                ?? properties.GetValueOrDefault("model")
                                ?? "AirPlay";
            var name = CleanName(displaySource);
            var requiresPassword = properties.TryGetValue("pw", out var pw)
                                   && (pw.Equals("true", StringComparison.OrdinalIgnoreCase) || pw == "1");
            var model = properties.GetValueOrDefault("am")
                        ?? properties.GetValueOrDefault("model");
            var deviceId = properties.GetValueOrDefault("deviceid")
                           ?? properties.GetValueOrDefault("pk")
                           ?? $"{address}:{srv.Port}";

            var device = new ReceiverDevice
            {
                Name = name,
                Host = address.ToString(),
                Port = srv.Port,
                RequiresPassword = requiresPassword,
                Model = model,
                DeviceId = deviceId
            };

            var key = device.Endpoint;
            if (_devices.TryGetValue(key, out var existing))
            {
                if (existing.Name == device.Name
                    && existing.RequiresPassword == device.RequiresPassword
                    && (existing.Model == device.Model
                        || string.IsNullOrWhiteSpace(device.Model)
                        || string.IsNullOrWhiteSpace(existing.Model)))
                {
                    continue;
                }

                // Prefer the richer record when the same endpoint is announced
                // through both _raop and _airplay.
                if (string.IsNullOrWhiteSpace(device.Model) && !string.IsNullOrWhiteSpace(existing.Model))
                    continue;
            }

            _devices[key] = device;
            Logger.Instance.Info($"Found AirPlay receiver: {device.DisplayName} at {device.Endpoint}" +
                                 (string.IsNullOrWhiteSpace(model) ? "" : $" ({model})"));
            DeviceFound?.Invoke(this, device);
        }
    }

    private static string CleanName(string value)
    {
        // The RAOP instance is often "<MAC>@<FriendlyName>".
        var at = value.LastIndexOf('@');
        if (at >= 0 && at + 1 < value.Length) value = value[(at + 1)..];
        return value.Trim();
    }

    private static bool NamesEqual(DomainName a, DomainName b)
        => string.Equals(a.ToString().TrimEnd('.'), b.ToString().TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ParseTxt(TXTRecord? txt)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (txt is null) return result;
        foreach (var entry in txt.Strings)
        {
            var idx = entry.IndexOf('=');
            if (idx <= 0) continue;
            result[entry[..idx]] = entry[(idx + 1)..];
        }
        return result;
    }

    public void Dispose()
    {
        _running = false;
        try { _socket?.Close(); } catch { }
        try { _thread?.Join(1000); } catch { }
        _socket?.Dispose();
        _socket = null;
        _thread = null;
    }
}
