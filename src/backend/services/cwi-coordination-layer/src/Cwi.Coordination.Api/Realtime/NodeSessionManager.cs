using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Cwi.Coordination.Application;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Transport;

namespace Cwi.Coordination.Api.Realtime;

public sealed class NodeSessionManager : INodeSessionDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, NodeSessionState> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly NodeSessionOptions _options;
    private readonly ILogger<NodeSessionManager> _logger;

    public NodeSessionManager(NodeSessionOptions options, ILogger<NodeSessionManager> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task RegisterAsync(NodeHelloMessage hello, WebSocket socket, CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var nextState = new NodeSessionState(
            socket,
            new NodeSessionSnapshot
            {
                NodeId = hello.NodeId,
                TenantId = hello.TenantId,
                OwnerUserId = hello.OwnerUserId,
                FriendlyName = hello.FriendlyName,
                RuntimeVersion = hello.RuntimeVersion,
                PolicyProfileVersion = hello.PolicyProfileVersion,
                Status = "connected",
                ConnectedUtc = nowUtc,
                LastSeenUtc = nowUtc
            });

        NodeSessionState? previous = null;
        _sessions.AddOrUpdate(
            hello.NodeId,
            _ => nextState,
            (_, existing) =>
            {
                previous = existing;
                return nextState;
            });

        if (previous is not null)
        {
            _logger.LogInformation("Replacing existing node session for {NodeId}", hello.NodeId);
            await previous.CloseAsync(WebSocketCloseStatus.NormalClosure, "session_replaced", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public bool HasActiveSession(string nodeId)
    {
        return _sessions.TryGetValue(nodeId, out var state)
            && state.IsActive(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(_options.NodeReadyStaleAfterSeconds));
    }

    public bool TryMarkKeepalive(string nodeId)
    {
        if (!_sessions.TryGetValue(nodeId, out var state))
        {
            return false;
        }

        state.MarkKeepalive(DateTimeOffset.UtcNow);
        return true;
    }

    public bool TryUpdateHeartbeat(string nodeId, NodeHeartbeatMessage heartbeat)
    {
        if (!_sessions.TryGetValue(nodeId, out var state))
        {
            return false;
        }

        state.MarkHeartbeat(heartbeat, DateTimeOffset.UtcNow);
        return true;
    }

    public async Task<bool> TryDispatchAsync(NodeWorkEnvelopeMessage envelope, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(envelope.TargetNodeId, out var state))
        {
            return false;
        }

        if (!state.IsActive(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(_options.NodeReadyStaleAfterSeconds)))
        {
            return false;
        }

        var payload = JsonSerializer.Serialize(envelope, JsonOptions);
        var success = await state.SendAsync(payload, cancellationToken).ConfigureAwait(false);
        if (!success)
        {
            _sessions.TryRemove(envelope.TargetNodeId, out _);
        }

        return success;
    }

    public async Task RemoveIfCurrentAsync(string nodeId, WebSocket socket, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(nodeId, out var state) && state.IsCurrent(socket))
        {
            _sessions.TryRemove(nodeId, out _);
        }

        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "session_closed", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private sealed class NodeSessionState
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly object _gate = new();
        private NodeSessionSnapshot _snapshot;

        public NodeSessionState(WebSocket socket, NodeSessionSnapshot snapshot)
        {
            Socket = socket;
            _snapshot = snapshot;
        }

        public WebSocket Socket { get; }

        public bool IsCurrent(WebSocket socket)
        {
            return ReferenceEquals(Socket, socket);
        }

        public bool IsActive(DateTimeOffset nowUtc, TimeSpan staleAfter)
        {
            lock (_gate)
            {
                return Socket.State == WebSocketState.Open
                    && nowUtc - _snapshot.LastSeenUtc <= staleAfter;
            }
        }

        public void MarkKeepalive(DateTimeOffset nowUtc)
        {
            lock (_gate)
            {
                _snapshot.LastSeenUtc = nowUtc;
                _snapshot.LastKeepaliveUtc = nowUtc;
            }
        }

        public void MarkHeartbeat(NodeHeartbeatMessage heartbeat, DateTimeOffset nowUtc)
        {
            lock (_gate)
            {
                _snapshot.LastSeenUtc = nowUtc;
                _snapshot.LastHeartbeatUtc = nowUtc;
                _snapshot.Status = string.IsNullOrWhiteSpace(heartbeat.Status)
                    ? "connected"
                    : heartbeat.Status.Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(heartbeat.RuntimeVersion))
                {
                    _snapshot.RuntimeVersion = heartbeat.RuntimeVersion;
                }

                if (!string.IsNullOrWhiteSpace(heartbeat.PolicyProfileVersion))
                {
                    _snapshot.PolicyProfileVersion = heartbeat.PolicyProfileVersion;
                }
            }
        }

        public async Task<bool> SendAsync(string payload, CancellationToken cancellationToken)
        {
            if (Socket.State != WebSocketState.Open)
            {
                return false;
            }

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Socket.State != WebSocketState.Open)
                {
                    return false;
                }

                var bytes = Encoding.UTF8.GetBytes(payload);
                await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken)
                    .ConfigureAwait(false);
                lock (_gate)
                {
                    _snapshot.LastSeenUtc = DateTimeOffset.UtcNow;
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async Task CloseAsync(WebSocketCloseStatus closeStatus, string description, CancellationToken cancellationToken)
        {
            try
            {
                if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await Socket.CloseAsync(closeStatus, description, cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
            }
        }
    }
}
