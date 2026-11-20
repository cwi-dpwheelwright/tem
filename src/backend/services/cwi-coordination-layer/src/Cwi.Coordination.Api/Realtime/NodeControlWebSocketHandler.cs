using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Cwi.Coordination.Application;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Services;

namespace Cwi.Coordination.Api.Realtime;

public sealed class NodeControlWebSocketHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationOptions _applicationOptions;
    private readonly NodeSessionMembershipService _membershipService;
    private readonly NodeSessionManager _sessionManager;
    private readonly NodeWorkService _nodeWorkService;
    private readonly ILogger<NodeControlWebSocketHandler> _logger;

    public NodeControlWebSocketHandler(
        ApplicationOptions applicationOptions,
        NodeSessionMembershipService membershipService,
        NodeSessionManager sessionManager,
        NodeWorkService nodeWorkService,
        ILogger<NodeControlWebSocketHandler> logger)
    {
        _applicationOptions = applicationOptions;
        _membershipService = membershipService;
        _sessionManager = sessionManager;
        _nodeWorkService = nodeWorkService;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket upgrade required.").ConfigureAwait(false);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Coordination-Protocol", out var version)
            || !string.Equals(version.ToString(), _applicationOptions.ProtocolVersion, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Protocol version mismatch.").ConfigureAwait(false);
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);

        string? nodeId = null;
        try
        {
            var helloPayload = await ReceiveTextMessageAsync(socket, context.RequestAborted).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(helloPayload))
            {
                await CloseWithProtocolErrorAsync(socket, "hello_required", context.RequestAborted).ConfigureAwait(false);
                return;
            }

            var messageType = GetMessageType(helloPayload);
            if (!string.Equals(messageType, "node_hello", StringComparison.OrdinalIgnoreCase))
            {
                await CloseWithProtocolErrorAsync(socket, "node_hello_required", context.RequestAborted).ConfigureAwait(false);
                return;
            }

            var hello = JsonSerializer.Deserialize<NodeHelloMessage>(helloPayload, JsonOptions);
            if (hello is null || string.IsNullOrWhiteSpace(hello.NodeId))
            {
                await CloseWithProtocolErrorAsync(socket, "invalid_hello", context.RequestAborted).ConfigureAwait(false);
                return;
            }

            var validation = await ValidateHelloAsync(hello, context.RequestAborted).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                _logger.LogWarning("Node hello rejected for {NodeId}: {Reason}", hello.NodeId, validation.Reason);
                await CloseWithPolicyViolationAsync(socket, validation.Reason ?? "hello_rejected", context.RequestAborted)
                    .ConfigureAwait(false);
                return;
            }

            nodeId = hello.NodeId;
            await _sessionManager.RegisterAsync(hello, socket, context.RequestAborted).ConfigureAwait(false);
            await _membershipService.UpdateFromHelloAsync(nodeId, hello, context.RequestAborted).ConfigureAwait(false);
            await _nodeWorkService.DispatchPendingAsync(nodeId, context.RequestAborted).ConfigureAwait(false);

            while (socket.State == WebSocketState.Open)
            {
                var payload = await ReceiveTextMessageAsync(socket, context.RequestAborted).ConfigureAwait(false);
                if (payload is null)
                {
                    break;
                }

                var type = GetMessageType(payload);
                switch (type?.ToLowerInvariant())
                {
                    case "keepalive":
                        await HandleKeepaliveAsync(nodeId, payload, context.RequestAborted).ConfigureAwait(false);
                        break;
                    case "heartbeat":
                        await HandleHeartbeatAsync(nodeId, payload, context.RequestAborted).ConfigureAwait(false);
                        break;
                    case "work_ack":
                        await HandleWorkAckAsync(nodeId, payload, context.RequestAborted).ConfigureAwait(false);
                        break;
                    case "work_result":
                        await HandleWorkResultAsync(nodeId, payload, context.RequestAborted).ConfigureAwait(false);
                        break;
                    default:
                        await CloseWithProtocolErrorAsync(socket, "unsupported_message_type", context.RequestAborted)
                            .ConfigureAwait(false);
                        return;
                }
            }
        }
        catch (JsonException)
        {
            await CloseWithProtocolErrorAsync(socket, "invalid_json", CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            await CloseWithProtocolErrorAsync(socket, ex.Message, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(nodeId))
            {
                await _sessionManager.RemoveIfCurrentAsync(nodeId, socket, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleKeepaliveAsync(string connectedNodeId, string payload, CancellationToken cancellationToken)
    {
        var keepalive = JsonSerializer.Deserialize<NodeKeepaliveMessage>(payload, JsonOptions);
        if (keepalive is null || !string.Equals(keepalive.NodeId, connectedNodeId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("keepalive_node_mismatch");
        }

        _sessionManager.TryMarkKeepalive(connectedNodeId);
        await _membershipService.TouchAsync(connectedNodeId, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleHeartbeatAsync(string connectedNodeId, string payload, CancellationToken cancellationToken)
    {
        var heartbeat = JsonSerializer.Deserialize<NodeHeartbeatMessage>(payload, JsonOptions);
        if (heartbeat is null || !string.Equals(heartbeat.NodeId, connectedNodeId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("heartbeat_node_mismatch");
        }

        _sessionManager.TryUpdateHeartbeat(connectedNodeId, heartbeat);
        await _membershipService.TouchAsync(connectedNodeId, heartbeat, cancellationToken).ConfigureAwait(false);
        await _nodeWorkService.DispatchPendingAsync(connectedNodeId, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleWorkAckAsync(string connectedNodeId, string payload, CancellationToken cancellationToken)
    {
        var ack = JsonSerializer.Deserialize<NodeWorkAckMessage>(payload, JsonOptions);
        if (ack is null || !string.Equals(ack.NodeId, connectedNodeId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("work_ack_node_mismatch");
        }

        if (string.Equals(ack.Status, "ACCEPTED", StringComparison.OrdinalIgnoreCase))
        {
            var response = await _nodeWorkService.AcknowledgeAsync(
                    ack.WorkId,
                    new AckNodeWorkRequest
                    {
                        NodeId = connectedNodeId,
                        LeaseId = ack.LeaseId
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                throw new InvalidOperationException("work_ack_rejected");
            }
            return;
        }

        var denied = await _nodeWorkService.CompleteAsync(
                ack.WorkId,
                new CompleteNodeWorkRequest
                {
                    NodeId = connectedNodeId,
                    LeaseId = ack.LeaseId,
                    Status = "denied",
                    Result = new Dictionary<string, object?>
                    {
                        ["ack_status"] = ack.Status
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (!denied.IsSuccess)
        {
            throw new InvalidOperationException("work_ack_denial_rejected");
        }
    }

    private async Task HandleWorkResultAsync(string connectedNodeId, string payload, CancellationToken cancellationToken)
    {
        var result = JsonSerializer.Deserialize<NodeWorkResultMessage>(payload, JsonOptions);
        if (result is null || !string.Equals(result.NodeId, connectedNodeId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("work_result_node_mismatch");
        }

        var completion = await _nodeWorkService.CompleteAsync(
                result.WorkId,
                new CompleteNodeWorkRequest
                {
                    NodeId = connectedNodeId,
                    LeaseId = result.LeaseId,
                    Status = MapOutcomeToStatus(result.Outcome),
                    Result = result.Result
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (!completion.IsSuccess)
        {
            throw new InvalidOperationException("work_result_rejected");
        }
    }

    private async Task<NodeHelloValidationResult> ValidateHelloAsync(
        NodeHelloMessage hello,
        CancellationToken cancellationToken)
    {
        return await _membershipService.ValidateHelloAsync(hello, cancellationToken).ConfigureAwait(false);
    }

    private static string? GetMessageType(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString()
            : null;
    }

    private static async Task<string?> ReceiveTextMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        await using var stream = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new InvalidOperationException("non_text_message_not_supported");
            }

            await stream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken).ConfigureAwait(false);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    private static async Task CloseWithProtocolErrorAsync(
        WebSocket socket,
        string description,
        CancellationToken cancellationToken)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await socket.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, description, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task CloseWithPolicyViolationAsync(
        WebSocket socket,
        string description,
        CancellationToken cancellationToken)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, description, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static string MapOutcomeToStatus(string outcome)
    {
        if (string.Equals(outcome, "SUCCESS", StringComparison.OrdinalIgnoreCase))
        {
            return "completed";
        }

        if (string.Equals(outcome, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            return "denied";
        }

        return "failed";
    }
}
