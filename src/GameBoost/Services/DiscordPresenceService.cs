using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using GameBoost.Infrastructure;

namespace GameBoost.Services;

/// <summary>
/// Shows "Playing GameBoost – Fortnite · Game mode on" in Discord via its local IPC pipe (Rich Presence).
/// Needs an application ID from discord.com/developers/applications. No extra library needed.
/// </summary>
public sealed class DiscordPresenceService : IDisposable
{
    private const int OpHandshake = 0;
    private const int OpFrame = 1;

    private readonly object _gate = new();
    private NamedPipeClientStream? _pipe;
    private Timer? _timer;
    private string _clientId = "";
    private bool _enabled;
    private (string Details, string State, DateTime Start)? _activity;

    public bool IsConnected => _pipe?.IsConnected == true;

    public event EventHandler? ConnectionChanged;

    public void Configure(bool enabled, string clientId)
    {
        lock (_gate)
        {
            var changed = _clientId != clientId?.Trim();
            _enabled = enabled && !string.IsNullOrWhiteSpace(clientId);
            _clientId = clientId?.Trim() ?? "";
            if (!_enabled || changed) DisconnectInternal();
            if (_enabled) _timer ??= new Timer(_ => Tick(), null, 0, 15000);
            else
            {
                _timer?.Dispose();
                _timer = null;
            }
        }
    }

    public void SetActivity(string details, string state, DateTime start)
    {
        lock (_gate)
        {
            _activity = (details, state, start);
            SendActivityInternal();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _activity = null;
            SendActivityInternal();
        }
    }

    private void Tick()
    {
        lock (_gate)
        {
            if (!_enabled || IsConnected) return;
            ConnectInternal();
        }
    }

    private void ConnectInternal()
    {
        for (var i = 0; i < 10; i++)
        {
            NamedPipeClientStream? pipe = null;
            try
            {
                pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
                pipe.Connect(300);
                Write(pipe, OpHandshake, JsonSerializer.Serialize(new { v = 1, client_id = _clientId }));
                var (op, json) = Read(pipe);
                if (op != OpFrame || !json.Contains("\"READY\"", StringComparison.Ordinal))
                {
                    Logger.Warn($"Discord handshake failed: {json}");
                    pipe.Dispose();
                    return;
                }
                _pipe = pipe;
                Logger.Info($"Connected to Discord (discord-ipc-{i})");
                _ = Task.Run(() => ReadLoop(pipe));
                SendActivityInternal();
                RaiseConnectionChanged();
                return;
            }
            catch (TimeoutException)
            {
                pipe?.Dispose();
            }
            catch (Exception ex)
            {
                pipe?.Dispose();
                Logger.Info($"Discord not reachable on pipe {i}: {ex.Message}");
            }
        }
    }

    private void SendActivityInternal()
    {
        if (_pipe is not { IsConnected: true } pipe) return;
        try
        {
            object? activity = null;
            if (_activity is { } a)
            {
                activity = new
                {
                    details = Trim(a.Details),
                    state = Trim(a.State),
                    timestamps = new { start = new DateTimeOffset(a.Start).ToUnixTimeSeconds() },
                    assets = new { large_image = "logo", large_text = "GameBoost" },
                    buttons = new[] { new { label = "Get GameBoost", url = "https://github.com/calvinistkrass/GameBoost" } }
                };
            }
            var payload = new
            {
                cmd = "SET_ACTIVITY",
                args = new { pid = Environment.ProcessId, activity },
                nonce = Guid.NewGuid().ToString()
            };
            Write(pipe, OpFrame, JsonSerializer.Serialize(payload));
        }
        catch (Exception ex)
        {
            Logger.Warn("Discord status could not be sent", ex);
            DisconnectInternal();
        }
    }

    private void ReadLoop(NamedPipeClientStream pipe)
    {
        try
        {
            while (pipe.IsConnected) Read(pipe); // responses are not needed, but must be read
        }
        catch
        {
            // pipe closed
        }
        lock (_gate)
        {
            if (ReferenceEquals(_pipe, pipe)) DisconnectInternal();
        }
    }

    private void DisconnectInternal()
    {
        if (_pipe == null) return;
        try { _pipe.Dispose(); } catch { /* ignore */ }
        _pipe = null;
        RaiseConnectionChanged();
    }

    internal static byte[] Frame(int op, string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), body.Length);
        body.CopyTo(frame, 8);
        return frame;
    }

    private static void Write(Stream pipe, int op, string json)
    {
        var frame = Frame(op, json);
        pipe.Write(frame, 0, frame.Length);
        pipe.Flush();
    }

    private static (int Op, string Json) Read(Stream pipe)
    {
        var header = ReadExact(pipe, 8);
        var op = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        var len = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
        if (len is < 0 or > 1_000_000) throw new IOException("Invalid Discord frame");
        return (op, Encoding.UTF8.GetString(ReadExact(pipe, len)));
    }

    private static byte[] ReadExact(Stream s, int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var n = s.Read(buffer, offset, count - offset);
            if (n <= 0) throw new EndOfStreamException();
            offset += n;
        }
        return buffer;
    }

    private static string Trim(string s) => s.Length <= 128 ? s : s[..128];

    private void RaiseConnectionChanged()
    {
        try
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => ConnectionChanged?.Invoke(this, EventArgs.Empty));
        }
        catch
        {
            // ignore
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            if (IsConnected)
            {
                _activity = null;
                SendActivityInternal();
            }
            DisconnectInternal();
        }
    }
}
