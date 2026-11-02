using SmartSchool.Api.Hosting;
using SmartSchool.Contracts;

namespace SmartSchool.Api.Chat;

public static class ChatEndpoints
{
    public static RouteGroupBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var chat = app.MapGroup("/api/v1/chat")
            .WithTags("Chat")
            .RequireAuthorization();

        chat.MapGet("/inbox", async (ChatService service, CancellationToken cancellationToken) =>
            (await service.InboxAsync(cancellationToken)).ToHttpResult());

        chat.MapGet("/conversations/{conversationId:guid}/messages", async (
            Guid conversationId,
            DateTime? before,
            Guid? beforeId,
            ChatService service,
            CancellationToken cancellationToken) =>
            (await service.MessagesAsync(conversationId, before, beforeId, cancellationToken)).ToHttpResult());

        chat.MapPost("/conversations/{conversationId:guid}/messages", async (
            Guid conversationId,
            SendChatMessageRequest request,
            ChatService service,
            CancellationToken cancellationToken) =>
            (await service.SendAsync(
                conversationId,
                request.Kind,
                request.Body,
                cancellationToken,
                request.ReplyToMessageId,
                request.ReplySenderName,
                request.ReplyBody)).ToHttpResult());

        chat.MapPost("/conversations/{conversationId:guid}/votes", async (
            Guid conversationId,
            CreateVoteRequest request,
            ChatService service,
            CancellationToken cancellationToken) =>
            (await service.CreateVoteAsync(conversationId, request.Question, request.Options, cancellationToken)).ToHttpResult());

        chat.MapPost("/messages/{messageId:guid}/votes", async (
            Guid messageId,
            CastVoteRequest request,
            ChatService service,
            CancellationToken cancellationToken) =>
            (await service.CastVoteAsync(messageId, request.OptionId, cancellationToken)).ToHttpResult());

        chat.MapPost("/conversations/{conversationId:guid}/files", async (
            Guid conversationId,
            IFormFile file,
            ChatService service,
            CancellationToken cancellationToken) =>
            (await service.SendFileAsync(conversationId, file, cancellationToken)).ToHttpResult())
            .DisableAntiforgery();

        chat.MapGet("/messages/{messageId:guid}/file", async (
            Guid messageId,
            ChatService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.FileAsync(messageId, cancellationToken);
            return result switch
            {
                ServiceResult<ChatFileContent>.Success success => Results.File(
                    success.Value.Bytes,
                    success.Value.ContentType,
                    success.Value.FileName),
                ServiceResult<ChatFileContent>.Fail fail => ProblemResponses.Result(fail.Status, fail.Type, fail.Title, fail.Detail),
                _ => ProblemResponses.Result(500, "urn:smartschool:internal", "Internal Server Error", "The request could not be completed.")
            };
        });

        chat.MapPost("/direct", async (
            OpenDirectChatRequest request,
            ChatService service,
            CancellationToken cancellationToken) =>
            (await service.OpenDirectAsync(request.PeerUserId, cancellationToken)).ToHttpResult());

        chat.MapPost("/conversations/{conversationId:guid}/read", async (
            Guid conversationId,
            ChatService service,
            CancellationToken cancellationToken) =>
            (await service.ReadAsync(conversationId, cancellationToken)).ToHttpResult());

        chat.MapPost("/conversations/{conversationId:guid}/pin", async (
            Guid conversationId,
            PinChatRequest request,
            ChatService service,
            CancellationToken cancellationToken) =>
            (await service.PinAsync(conversationId, request.Pinned, cancellationToken)).ToHttpResult());

        chat.MapPost("/conversations/{conversationId:guid}/hide", async (
            Guid conversationId,
            ChatService service,
            CancellationToken cancellationToken) =>
            (await service.HideAsync(conversationId, cancellationToken)).ToHttpResult());

        chat.MapPut("/messages/{messageId:guid}", async (
            Guid messageId,
            EditChatMessageRequest request,
            ChatService service,
            CancellationToken cancellationToken) =>
            (await service.EditAsync(messageId, request.Body, cancellationToken)).ToHttpResult());

        chat.MapDelete("/messages/{messageId:guid}", async (
            Guid messageId,
            ChatService service,
            CancellationToken cancellationToken) =>
            (await service.DeleteAsync(messageId, cancellationToken)).ToHttpResult());

        return chat;
    }
}
