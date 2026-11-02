namespace SmartSchool.Contracts;

public sealed record ChatMemberResponse(Guid UserId, string Name, string Role, bool Online);

public sealed record ChatRoomResponse(
    Guid ConversationId,
    string Kind,
    string Title,
    Guid? ClassSectionId,
    Guid? PeerUserId,
    string Preview,
    DateTime? LastMessageAtUtc,
    int UnreadCount,
    bool Pinned,
    IReadOnlyList<ChatMemberResponse> Members);

public sealed record ChatInboxResponse(Guid UserId, IReadOnlyList<ChatRoomResponse> Rooms);

public sealed record ChatMessageResponse(
    Guid Id,
    Guid ConversationId,
    Guid SenderUserId,
    string SenderName,
    string Kind,
    string Body,
    string? FileName,
    bool HasFile,
    DateTime CreatedAtUtc,
    Guid? ReplyToMessageId = null,
    string? ReplySenderName = null,
    string? ReplyBody = null);

public sealed record ChatMessagesResponse(IReadOnlyList<ChatMessageResponse> Messages, bool HasMore);

public sealed record SendChatMessageRequest(
    string? Kind,
    string? Body,
    Guid? ReplyToMessageId = null,
    string? ReplySenderName = null,
    string? ReplyBody = null);

public sealed record EditChatMessageRequest(string? Body);

public sealed record CreateVoteRequest(string? Question, IReadOnlyList<string>? Options);

public sealed record CastVoteRequest(string? OptionId);

public sealed record OpenDirectChatRequest(Guid PeerUserId);

public sealed record OpenDirectChatResponse(Guid ConversationId, string Title);

public sealed record PinChatRequest(bool Pinned);
