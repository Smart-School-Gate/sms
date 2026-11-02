using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SmartSchool.Contracts;

namespace SmartSchool.Api.Chat;

[Authorize]
public sealed class ChatHub(ChatService chats, ChatRealtime realtime) : Hub
{
    public static string Room(Guid conversationId) => $"room:{conversationId:D}";

    public override async Task OnConnectedAsync()
    {
        var sub = Context.User?.FindFirstValue("sub");
        if (Guid.TryParse(sub, out var userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, ChatRealtime.Group(userId));
        await base.OnConnectedAsync();
    }

    public async Task Join(string conversationId)
    {
        if (!TryActor(out var userId, out var contextId) || !Guid.TryParse(conversationId, out var roomId))
            return;
        if (await chats.AuthorizeLive(userId, contextId, roomId) is null)
            return;
        await Groups.AddToGroupAsync(Context.ConnectionId, Room(roomId));
    }

    public async Task Send(string conversationId, string body, string messageId, string? replyToId = null, string? replySender = null, string? replyBody = null)
    {
        var text = (body ?? "").Trim();
        if (text.Length == 0 || text.Length > 4000)
            return;
        if (!TryActor(out var userId, out var contextId) || !Guid.TryParse(conversationId, out var roomId))
            return;
        if (!Guid.TryParse(messageId, out var id))
            id = Guid.NewGuid();
        var pass = await chats.AuthorizeLive(userId, contextId, roomId);
        if (pass is null)
            return;
        var createdAt = DateTime.UtcNow;
        Guid? replyId = Guid.TryParse(replyToId, out var parsedReply) ? parsedReply : null;
        var replyName = Clip(replySender, 200);
        var replyText = Clip(replyBody, 280);
        if (replyId is null)
        {
            replyName = null;
            replyText = null;
        }
        var message = new ChatMessageResponse(
            id,
            roomId,
            userId,
            pass.FullName,
            "text",
            text,
            null,
            false,
            createdAt,
            replyId,
            replyName,
            replyText);
        await realtime.Message(pass.Recipients, message);
        chats.AcceptLive(pass, id, text, createdAt, replyId, replyName, replyText);
    }

    private static string? Clip(string? value, int max)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
            return null;
        return text.Length > max ? text[..max] : text;
    }

    private bool TryActor(out Guid userId, out Guid contextId)
    {
        userId = Guid.Empty;
        contextId = Guid.Empty;
        return Guid.TryParse(Context.User?.FindFirstValue("sub"), out userId)
            && Guid.TryParse(Context.User?.FindFirstValue("ctx"), out contextId);
    }
}
