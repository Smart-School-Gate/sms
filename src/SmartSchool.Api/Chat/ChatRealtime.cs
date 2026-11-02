using Microsoft.AspNetCore.SignalR;
using SmartSchool.Contracts;

namespace SmartSchool.Api.Chat;

public sealed class ChatRealtime(IHubContext<ChatHub> hub)
{
    public static string Group(Guid userId) => $"user:{userId:D}";

    public async Task Notify(IEnumerable<Guid> userIds, Guid conversationId)
    {
        var payload = conversationId.ToString("D");
        await Send(userIds, "changed", payload);
    }

    public Task Message(IEnumerable<Guid> userIds, ChatMessageResponse message) =>
        Send(userIds, "message", message);

    public Task Removed(IEnumerable<Guid> userIds, Guid conversationId, Guid messageId) =>
        Send(userIds, "deleted", conversationId.ToString("D"), messageId.ToString("D"));

    private Task Send(IEnumerable<Guid> userIds, string method, params object?[] arguments)
    {
        var sends = userIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Select(id => hub.Clients.Group(Group(id)).SendCoreAsync(method, arguments));
        return Task.WhenAll(sends);
    }
}
