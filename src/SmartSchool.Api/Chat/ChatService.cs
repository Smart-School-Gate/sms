using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartSchool.Api.Auth;
using SmartSchool.Api.Hosting;
using SmartSchool.Contracts;
using SmartSchool.Core;
using SmartSchool.Infrastructure.Persistence;

namespace SmartSchool.Api.Chat;

public sealed record ChatFileContent(byte[] Bytes, string ContentType, string FileName);

public sealed class ChatService(AppDbContext db, RequestActor actor, TimeProvider time, ChatRealtime realtime, IServiceScopeFactory scopes)
{
    public const int MaxFileBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(90);

    public async Task<ServiceResult<ChatInboxResponse>> InboxAsync(CancellationToken cancellationToken)
    {
        if (!Ready(out var userId, out var denied))
            return denied.As<ChatInboxResponse>();

        var schoolId = actor.Context!.SchoolId;
        QueueSeen(userId, schoolId, null);
        var classIds = await AccessibleClassIdsAsync(userId, cancellationToken);
        await EnsureGroupsAsync(classIds, cancellationToken);

        var hidden = await db.ChatHiddens.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => row.ConversationId)
            .ToListAsync(cancellationToken);

        var rooms = await db.ChatConversations.AsNoTracking()
            .Where(row =>
                !hidden.Contains(row.Id)
                && ((row.Kind == "group" && row.ClassSectionId != null && classIds.Contains(row.ClassSectionId.Value))
                    || (row.Kind == "direct" && (row.UserLowId == userId || row.UserHighId == userId))))
            .ToListAsync(cancellationToken);

        var ids = rooms.Select(row => row.Id).ToArray();
        var classSections = rooms
            .Where(row => row.ClassSectionId != null)
            .Select(row => row.ClassSectionId!.Value)
            .Distinct()
            .ToArray();
        var peerIds = PeerIds(rooms, userId).Distinct().ToArray();
        var titles = await ClassTitlesAsync(db, classSections, cancellationToken);
        var roster = (await StudentsOfAsync(db, classSections, cancellationToken))
            .Concat(await ParentsOfAsync(db, classSections, cancellationToken))
            .Concat(await TeachersOfAsync(db, classSections, cancellationToken));
        var members = roster
            .GroupBy(row => row.ClassId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.Person).GroupBy(person => person.UserId).Select(person => person.First()).OrderBy(person => person.Name).ToList());
        var names = await NamesAsync(db, peerIds, cancellationToken);
        var pins = await db.ChatPins.AsNoTracking()
            .Where(row => row.UserId == userId && ids.Contains(row.ConversationId))
            .ToListAsync(cancellationToken);
        var unread = await UnreadAsync(db, userId, ids, cancellationToken);
        var previews = await PreviewsAsync(db, ids, cancellationToken);
        var presenceIds = names.Keys
            .Concat(members.Values.SelectMany(list => list.Select(person => person.UserId)))
            .Append(userId)
            .Distinct()
            .ToArray();
        var presence = await PresenceAsync(db, presenceIds, cancellationToken);

        var result = rooms.Select(room =>
        {
            var peer = room.Kind == "direct" ? Other(room, userId) : (Guid?)null;
            var roomMembers = room.ClassSectionId is Guid classId && members.TryGetValue(classId, out var list)
                ? list
                : peer is Guid peerId
                    ? new List<ChatPerson>
                    {
                        new(userId, actor.FullName, RoleNames.ToName(actor.Context!.Role)),
                        new(peerId, names.GetValueOrDefault(peerId) ?? "", "direct")
                    }
                    : [];
            previews.TryGetValue(room.Id, out var preview);
            var pin = pins.FirstOrDefault(row => row.ConversationId == room.Id);
            var pinned = pin?.Pinned == true;
            return new ChatRoomResponse(
                room.Id,
                room.Kind,
                room.Kind == "group"
                    ? titles.GetValueOrDefault(room.ClassSectionId ?? Guid.Empty) ?? ""
                    : names.GetValueOrDefault(peer ?? Guid.Empty) ?? "",
                room.ClassSectionId,
                peer,
                preview.Text ?? "",
                preview.At == default ? null : preview.At,
                unread.GetValueOrDefault(room.Id),
                pinned,
                roomMembers
                    .Select(person => new ChatMemberResponse(
                        person.UserId,
                        person.Name,
                        person.Role,
                        IsOnline(presence, person.UserId)))
                    .ToArray());
        })
        .OrderByDescending(room => room.Pinned)
        .ThenByDescending(room => room.LastMessageAtUtc ?? DateTime.MinValue)
        .ToArray();

        return new ServiceResult<ChatInboxResponse>.Success(new ChatInboxResponse(userId, result));
    }

    public const int PageSize = 20;

    public async Task<ServiceResult<ChatMessagesResponse>> MessagesAsync(
        Guid conversationId,
        DateTime? before,
        Guid? beforeId,
        CancellationToken cancellationToken)
    {
        if (!Ready(out var userId, out var denied))
            return denied.As<ChatMessagesResponse>();
        var room = await FindRoomAsync(conversationId, userId, cancellationToken);
        if (room is null)
            return NotFound<ChatMessagesResponse>();

        if (before is null)
            QueueSeen(userId, actor.Context!.SchoolId, conversationId);

        var cursor = before?.ToUniversalTime();
        var query = db.ChatMessages.AsNoTracking().Where(row => row.ConversationId == conversationId);
        if (cursor is DateTime stamp)
        {
            query = beforeId is Guid olderThan
                ? query.Where(row => row.CreatedAtUtc < stamp || (row.CreatedAtUtc == stamp && row.Id != olderThan))
                : query.Where(row => row.CreatedAtUtc < stamp);
        }

        var rows = await (
            from row in query
            join user in db.Users.AsNoTracking() on row.SenderUserId equals user.Id into senders
            from user in senders.DefaultIfEmpty()
            orderby row.CreatedAtUtc descending, row.Id descending
            select new
            {
                row.Id,
                row.ConversationId,
                row.SenderUserId,
                user.FullName,
                user.Username,
                row.Kind,
                row.Body,
                row.FileName,
                HasFile = row.Payload != null,
                row.ReplyToMessageId,
                row.ReplySenderName,
                row.ReplyBody,
                row.CreatedAtUtc
            })
            .Take(PageSize + 1)
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > PageSize;
        var page = rows.Take(PageSize).OrderBy(row => row.CreatedAtUtc).ThenBy(row => row.Id).ToList();
        var messages = page.Select(row => new ChatMessageResponse(
            row.Id,
            row.ConversationId,
            row.SenderUserId,
            string.IsNullOrWhiteSpace(row.FullName) ? row.Username ?? "" : row.FullName,
            row.Kind,
            row.Body,
            row.FileName,
            row.HasFile,
            row.CreatedAtUtc,
            row.ReplyToMessageId,
            row.ReplySenderName,
            row.ReplyBody)).ToArray();
        return new ServiceResult<ChatMessagesResponse>.Success(new ChatMessagesResponse(messages, hasMore));
    }

    public async Task<ServiceResult<ChatMessageResponse>> SendAsync(
        Guid conversationId,
        string? kind,
        string? body,
        CancellationToken cancellationToken,
        Guid? replyToMessageId = null,
        string? replySenderName = null,
        string? replyBody = null)
    {
        if (!Ready(out var userId, out var denied))
            return denied.As<ChatMessageResponse>();
        var room = await FindRoomAsync(conversationId, userId, cancellationToken);
        if (room is null)
            return NotFound<ChatMessageResponse>();

        var messageKind = kind is "voice" ? "voice" : "text";
        var text = (body ?? "").Trim();
        if (messageKind == "text" && text.Length == 0)
            return Fail<ChatMessageResponse>(StatusCodes.Status400BadRequest, "The message is empty.");
        if (text.Length > 4000)
            text = text[..4000];
        var reply = ReplyOf(replyToMessageId, replySenderName, replyBody);

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            SchoolId = actor.Context!.SchoolId,
            ConversationId = conversationId,
            SenderUserId = userId,
            Kind = messageKind,
            Body = text,
            ReplyToMessageId = reply.Id,
            ReplySenderName = reply.Name,
            ReplyBody = reply.Text,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime
        };
        db.ChatMessages.Add(message);
        await RevealAsync(conversationId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        QueueSeen(userId, actor.Context!.SchoolId, null);
        await PublishAsync(room, ToResponse(message, actor.FullName));
        return new ServiceResult<ChatMessageResponse>.Success(ToResponse(message, actor.FullName));
    }

    public async Task<ServiceResult<ChatMessageResponse>> CreateVoteAsync(
        Guid conversationId,
        string? question,
        IReadOnlyList<string>? options,
        CancellationToken cancellationToken)
    {
        if (!Ready(out var userId, out var denied))
            return denied.As<ChatMessageResponse>();
        var room = await FindRoomAsync(conversationId, userId, cancellationToken);
        if (room is null)
            return NotFound<ChatMessageResponse>();
        if (room.Kind != "group")
            return Fail<ChatMessageResponse>(StatusCodes.Status400BadRequest, "Votes belong to the class chat.");
        var title = (question ?? "").Trim();
        if (title.Length == 0 || title.Length > 180)
            return Fail<ChatMessageResponse>(StatusCodes.Status400BadRequest, "The vote question is invalid.");
        var choices = (options ?? [])
            .Select(option => (option ?? "").Trim())
            .Where(option => option.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .Select(option => option.Length > 60 ? option[..60] : option)
            .ToList();
        if (choices.Count < 2)
            return Fail<ChatMessageResponse>(StatusCodes.Status400BadRequest, "A vote needs at least two choices.");
        var document = new VoteDocument
        {
            Question = title,
            Options = choices.Select((text, index) => new VoteChoice { Id = (index + 1).ToString(), Text = text }).ToList()
        };
        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            SchoolId = actor.Context!.SchoolId,
            ConversationId = conversationId,
            SenderUserId = userId,
            Kind = "vote",
            Body = JsonSerializer.Serialize(document, VoteJson),
            CreatedAtUtc = time.GetUtcNow().UtcDateTime
        };
        db.ChatMessages.Add(message);
        await RevealAsync(conversationId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        QueueSeen(userId, actor.Context!.SchoolId, null);
        await PublishAsync(room, ToResponse(message, actor.FullName));
        return new ServiceResult<ChatMessageResponse>.Success(ToResponse(message, actor.FullName));
    }

    public async Task<ServiceResult<ChatMessageResponse>> CastVoteAsync(
        Guid messageId,
        string? optionId,
        CancellationToken cancellationToken)
    {
        if (!Ready(out var userId, out var denied))
            return denied.As<ChatMessageResponse>();
        var choice = (optionId ?? "").Trim();
        var message = await db.ChatMessages.FirstOrDefaultAsync(row => row.Id == messageId, cancellationToken);
        if (message is null || message.Kind != "vote")
            return NotFound<ChatMessageResponse>();
        var room = await FindRoomAsync(message.ConversationId, userId, cancellationToken);
        if (room is null || room.Kind != "group")
            return NotFound<ChatMessageResponse>();
        var document = JsonSerializer.Deserialize<VoteDocument>(message.Body, VoteJson);
        if (document is null || document.Options.All(option => option.Id != choice))
            return Fail<ChatMessageResponse>(StatusCodes.Status400BadRequest, "The vote choice is invalid.");
        document.Votes[userId.ToString("D")] = choice;
        message.Body = JsonSerializer.Serialize(document, VoteJson);
        await db.SaveChangesAsync(cancellationToken);
        var senderName = await db.Users.AsNoTracking()
            .Where(row => row.Id == message.SenderUserId)
            .Select(row => row.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? "";
        var response = ToResponse(message, senderName);
        await PublishAsync(room, response);
        return new ServiceResult<ChatMessageResponse>.Success(response);
    }

    public async Task<ServiceResult<ChatMessageResponse>> SendFileAsync(
        Guid conversationId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!Ready(out var userId, out var denied))
            return denied.As<ChatMessageResponse>();
        var room = await FindRoomAsync(conversationId, userId, cancellationToken);
        if (room is null)
            return NotFound<ChatMessageResponse>();
        if (file.Length <= 0 || file.Length > MaxFileBytes)
            return Fail<ChatMessageResponse>(StatusCodes.Status400BadRequest, "The file could not be sent.");

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaxFileBytes)
            return Fail<ChatMessageResponse>(StatusCodes.Status400BadRequest, "The file could not be sent.");

        var name = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(name))
            name = "file";
        if (name.Length > 260)
            name = name[..260];
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        if (contentType.Length > 120)
            contentType = contentType[..120];

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            SchoolId = actor.Context!.SchoolId,
            ConversationId = conversationId,
            SenderUserId = userId,
            Kind = IsVoice(contentType, name) ? "voice" : IsImage(contentType, name) ? "image" : "file",
            Body = "",
            FileName = name,
            ContentType = contentType,
            Payload = buffer.ToArray(),
            CreatedAtUtc = time.GetUtcNow().UtcDateTime
        };
        db.ChatMessages.Add(message);
        await RevealAsync(conversationId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        QueueSeen(userId, actor.Context!.SchoolId, null);
        await PublishAsync(room, ToResponse(message, actor.FullName));
        return new ServiceResult<ChatMessageResponse>.Success(ToResponse(message, actor.FullName));
    }

    public async Task<ServiceResult<ChatFileContent>> FileAsync(Guid messageId, CancellationToken cancellationToken)
    {
        if (!Ready(out var userId, out var denied))
            return denied.As<ChatFileContent>();
        var message = await db.ChatMessages.AsNoTracking().FirstOrDefaultAsync(row => row.Id == messageId, cancellationToken);
        if (message?.Payload is null)
            return NotFound<ChatFileContent>();
        if (await FindRoomAsync(message.ConversationId, userId, cancellationToken) is null)
            return NotFound<ChatFileContent>();
        return new ServiceResult<ChatFileContent>.Success(new ChatFileContent(
            message.Payload,
            message.ContentType ?? "application/octet-stream",
            message.FileName ?? "file"));
    }

    public async Task<ServiceResult<OpenDirectChatResponse>> OpenDirectAsync(Guid peerUserId, CancellationToken cancellationToken)
    {
        if (!Ready(out var userId, out var denied))
            return denied.As<OpenDirectChatResponse>();
        if (peerUserId == userId)
            return Fail<OpenDirectChatResponse>(StatusCodes.Status400BadRequest, "Choose another person.");

        var classIds = await AccessibleClassIdsAsync(userId, cancellationToken);
        var allowed = false;
        foreach (var classId in classIds)
        {
            if (await IsClassMemberAsync(classId, peerUserId, cancellationToken))
            {
                allowed = true;
                break;
            }
        }
        if (!allowed)
            return Fail<OpenDirectChatResponse>(StatusCodes.Status403Forbidden, "This person is outside the class chat.");

        var low = userId.CompareTo(peerUserId) < 0 ? userId : peerUserId;
        var high = low == userId ? peerUserId : userId;
        var room = await db.ChatConversations.FirstOrDefaultAsync(
            row => row.Kind == "direct" && row.UserLowId == low && row.UserHighId == high,
            cancellationToken);
        if (room is null)
        {
            room = new ChatConversation
            {
                Id = Guid.NewGuid(),
                SchoolId = actor.Context!.SchoolId,
                Kind = "direct",
                UserLowId = low,
                UserHighId = high,
                CreatedAtUtc = time.GetUtcNow().UtcDateTime
            };
            db.ChatConversations.Add(room);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                db.Entry(room).State = EntityState.Detached;
                room = await db.ChatConversations.FirstAsync(
                    row => row.Kind == "direct" && row.UserLowId == low && row.UserHighId == high,
                    cancellationToken);
            }
        }

        await db.ChatHiddens.Where(row => row.ConversationId == room.Id && row.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        var names = await NamesAsync(db, [peerUserId], cancellationToken);
        await PublishAsync(room);
        return new ServiceResult<OpenDirectChatResponse>.Success(
            new OpenDirectChatResponse(room.Id, names.GetValueOrDefault(peerUserId) ?? ""));
    }

    public async Task<ServiceResult<bool>> ReadAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        if (!Ready(out var userId, out var denied))
            return denied.As<bool>();
        if (await FindRoomAsync(conversationId, userId, cancellationToken) is null)
            return NotFound<bool>();
        await MarkReadAsync(conversationId, userId, cancellationToken);
        return new ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<bool>> PinAsync(Guid conversationId, bool pinned, CancellationToken cancellationToken)
    {
        if (!Ready(out var userId, out var denied))
            return denied.As<bool>();
        if (await FindRoomAsync(conversationId, userId, cancellationToken) is null)
            return NotFound<bool>();
        var row = await db.ChatPins.FirstOrDefaultAsync(
            item => item.ConversationId == conversationId && item.UserId == userId,
            cancellationToken);
        if (row is null)
        {
            db.ChatPins.Add(new ChatPin
            {
                Id = Guid.NewGuid(),
                SchoolId = actor.Context!.SchoolId,
                ConversationId = conversationId,
                UserId = userId,
                Pinned = pinned
            });
        }
        else
        {
            row.Pinned = pinned;
        }
        await db.SaveChangesAsync(cancellationToken);
        await realtime.Notify([userId], conversationId);
        return new ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<bool>> HideAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        if (!Ready(out var userId, out var denied))
            return denied.As<bool>();
        if (await FindRoomAsync(conversationId, userId, cancellationToken) is null)
            return NotFound<bool>();
        var exists = await db.ChatHiddens.AnyAsync(
            row => row.ConversationId == conversationId && row.UserId == userId,
            cancellationToken);
        if (!exists)
        {
            db.ChatHiddens.Add(new ChatHidden
            {
                Id = Guid.NewGuid(),
                SchoolId = actor.Context!.SchoolId,
                ConversationId = conversationId,
                UserId = userId
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        await realtime.Notify([userId], conversationId);
        return new ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<ChatMessageResponse>> EditAsync(Guid messageId, string? body, CancellationToken cancellationToken)
    {
        if (!Ready(out var userId, out var denied))
            return denied.As<ChatMessageResponse>();
        var message = await db.ChatMessages.FirstOrDefaultAsync(row => row.Id == messageId, cancellationToken);
        if (message is null || message.SenderUserId != userId || message.Kind != "text")
            return NotFound<ChatMessageResponse>();
        var room = await FindRoomAsync(message.ConversationId, userId, cancellationToken);
        if (room is null)
            return NotFound<ChatMessageResponse>();
        var text = (body ?? "").Trim();
        if (text.Length == 0)
            return Fail<ChatMessageResponse>(StatusCodes.Status400BadRequest, "The message is empty.");
        message.Body = text.Length > 4000 ? text[..4000] : text;
        await db.SaveChangesAsync(cancellationToken);
        await PublishAsync(room, ToResponse(message, actor.FullName));
        return new ServiceResult<ChatMessageResponse>.Success(ToResponse(message, actor.FullName));
    }

    public async Task<ServiceResult<bool>> DeleteAsync(Guid messageId, CancellationToken cancellationToken)
    {
        if (!Ready(out var userId, out var denied))
            return denied.As<bool>();
        var message = await db.ChatMessages.FirstOrDefaultAsync(row => row.Id == messageId, cancellationToken);
        if (message is null || message.SenderUserId != userId)
            return NotFound<bool>();
        var room = await FindRoomAsync(message.ConversationId, userId, cancellationToken);
        if (room is null)
            return NotFound<bool>();
        db.ChatMessages.Remove(message);
        await db.SaveChangesAsync(cancellationToken);
        await PublishAsync(room, removedId: message.Id);
        return new ServiceResult<bool>.Success(true);
    }

    private async Task PublishAsync(ChatConversation room, ChatMessageResponse? message = null, Guid? removedId = null)
    {
        try
        {
            Guid[] ids;
            if (room.Kind == "direct" && room.UserLowId is Guid low && room.UserHighId is Guid high)
                ids = [low, high];
            else if (room.Kind == "group" && room.ClassSectionId is Guid classId)
                ids = (await MembersAsync(classId, CancellationToken.None)).Select(person => person.UserId).ToArray();
            else
                return;
            if (message != null)
                await realtime.Message(ids, message);
            if (removedId is Guid removed)
                await realtime.Removed(ids, room.Id, removed);
            await realtime.Notify(ids, room.Id);
        }
        catch (Exception)
        {
            // A failed live push still leaves the saved message available on the next open.
        }
    }

    private async Task<ChatConversation?> FindRoomAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken)
    {
        var room = await db.ChatConversations.AsNoTracking().FirstOrDefaultAsync(row => row.Id == conversationId, cancellationToken);
        if (room is null)
            return null;
        if (room.Kind == "direct")
            return room.UserLowId == userId || room.UserHighId == userId ? room : null;
        if (room.Kind == "group" && room.ClassSectionId is Guid classId && await IsClassMemberAsync(classId, userId, cancellationToken))
            return room;
        return null;
    }

    private async Task EnsureGroupsAsync(IReadOnlyList<Guid> classIds, CancellationToken cancellationToken)
    {
        if (classIds.Count == 0)
            return;
        var existing = await db.ChatConversations.AsNoTracking()
            .Where(row => row.Kind == "group" && row.ClassSectionId != null && classIds.Contains(row.ClassSectionId.Value))
            .Select(row => row.ClassSectionId!.Value)
            .ToListAsync(cancellationToken);
        var missing = classIds.Except(existing).ToArray();
        if (missing.Length == 0)
            return;
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var classId in missing)
        {
            db.ChatConversations.Add(new ChatConversation
            {
                Id = Guid.NewGuid(),
                SchoolId = actor.Context!.SchoolId,
                Kind = "group",
                ClassSectionId = classId,
                CreatedAtUtc = now
            });
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            foreach (var entry in db.ChangeTracker.Entries<ChatConversation>().Where(entry => entry.State == EntityState.Added).ToArray())
                entry.State = EntityState.Detached;
        }
    }

    private async Task<List<Guid>> AccessibleClassIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var role = actor.Context!.Role;
        if (role == AppRole.Student && actor.Context.StudentId is Guid studentId)
        {
            return await db.Enrollments.AsNoTracking()
                .Where(row => row.StudentId == studentId)
                .Select(row => row.ClassSectionId)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        if (role == AppRole.Parent)
        {
            return await (
                from guardian in db.Guardians.AsNoTracking()
                join link in db.GuardianStudents.AsNoTracking() on guardian.Id equals link.GuardianId
                join enrollment in db.Enrollments.AsNoTracking() on link.StudentId equals enrollment.StudentId
                where guardian.UserId == userId
                select enrollment.ClassSectionId
            ).Distinct().ToListAsync(cancellationToken);
        }

        if (role == AppRole.Teacher)
        {
            return await db.TeacherClassAssignments.AsNoTracking()
                .Where(row => row.UserId == userId)
                .Select(row => row.ClassSectionId)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        return [];
    }

    private async Task<bool> IsClassMemberAsync(Guid classId, Guid userId, CancellationToken cancellationToken)
    {
        var role = actor.Context!.Role;
        if (role == AppRole.Student && actor.Context.StudentId is Guid studentId)
        {
            return await db.Enrollments.AsNoTracking().AnyAsync(
                enrollment => enrollment.StudentId == studentId && enrollment.ClassSectionId == classId,
                cancellationToken);
        }

        if (role == AppRole.Parent)
        {
            return await (
                from guardian in db.Guardians.AsNoTracking()
                join link in db.GuardianStudents.AsNoTracking() on guardian.Id equals link.GuardianId
                join enrollment in db.Enrollments.AsNoTracking() on link.StudentId equals enrollment.StudentId
                where guardian.UserId == userId && enrollment.ClassSectionId == classId
                select guardian.Id
            ).AnyAsync(cancellationToken);
        }

        if (role == AppRole.Teacher)
        {
            return await db.TeacherClassAssignments.AsNoTracking().AnyAsync(
                row => row.UserId == userId && row.ClassSectionId == classId,
                cancellationToken);
        }

        return false;
    }

    private async Task<Dictionary<Guid, string>> ClassTitlesAsync(AppDbContext database, IReadOnlyCollection<Guid> classIds, CancellationToken cancellationToken)
    {
        if (classIds.Count == 0)
            return [];
        var rows = await (
            from section in database.ClassSections.AsNoTracking()
            join grade in database.GradeLevels.AsNoTracking() on section.GradeLevelId equals grade.Id
            where classIds.Contains(section.Id)
            select new { section.Id, Grade = grade.Name, Section = section.Name }
        ).ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Id, row => $"{row.Grade} - {row.Section}");
    }

    private async Task<List<ChatPerson>> MembersAsync(Guid classId, CancellationToken cancellationToken)
    {
        var students = await (
            from enrollment in db.Enrollments.AsNoTracking()
            join student in db.Students.AsNoTracking() on enrollment.StudentId equals student.Id
            where enrollment.ClassSectionId == classId && student.UserId != null
            select new ChatPerson(student.UserId!.Value, student.FullName, "student")
        ).ToListAsync(cancellationToken);

        var parents = await (
            from enrollment in db.Enrollments.AsNoTracking()
            join link in db.GuardianStudents.AsNoTracking() on enrollment.StudentId equals link.StudentId
            join guardian in db.Guardians.AsNoTracking() on link.GuardianId equals guardian.Id
            where enrollment.ClassSectionId == classId
            select new ChatPerson(guardian.UserId, guardian.FullName, "parent")
        ).ToListAsync(cancellationToken);

        var teachers = await (
            from assignment in db.TeacherClassAssignments.AsNoTracking()
            join user in db.Users.AsNoTracking() on assignment.UserId equals user.Id
            where assignment.ClassSectionId == classId
            select new ChatPerson(user.Id, user.FullName ?? user.Username, "teacher")
        ).ToListAsync(cancellationToken);

        return students.Concat(parents).Concat(teachers)
            .GroupBy(person => person.UserId)
            .Select(group => group.First())
            .OrderBy(person => person.Name)
            .ToList();
    }

    private async Task<List<(Guid ClassId, ChatPerson Person)>> StudentsOfAsync(AppDbContext database, Guid[] classIds, CancellationToken cancellationToken)
    {
        if (classIds.Length == 0)
            return [];
        var rows = await (
            from enrollment in database.Enrollments.AsNoTracking()
            join student in database.Students.AsNoTracking() on enrollment.StudentId equals student.Id
            where classIds.Contains(enrollment.ClassSectionId) && student.UserId != null
            select new { enrollment.ClassSectionId, student.UserId, student.FullName }
        ).ToListAsync(cancellationToken);
        return rows.Select(row => (row.ClassSectionId, new ChatPerson(row.UserId!.Value, row.FullName, "student"))).ToList();
    }

    private async Task<List<(Guid ClassId, ChatPerson Person)>> ParentsOfAsync(AppDbContext database, Guid[] classIds, CancellationToken cancellationToken)
    {
        if (classIds.Length == 0)
            return [];
        var rows = await (
            from enrollment in database.Enrollments.AsNoTracking()
            join link in database.GuardianStudents.AsNoTracking() on enrollment.StudentId equals link.StudentId
            join guardian in database.Guardians.AsNoTracking() on link.GuardianId equals guardian.Id
            where classIds.Contains(enrollment.ClassSectionId)
            select new { enrollment.ClassSectionId, guardian.UserId, guardian.FullName }
        ).ToListAsync(cancellationToken);
        return rows.Select(row => (row.ClassSectionId, new ChatPerson(row.UserId, row.FullName, "parent"))).ToList();
    }

    private async Task<List<(Guid ClassId, ChatPerson Person)>> TeachersOfAsync(AppDbContext database, Guid[] classIds, CancellationToken cancellationToken)
    {
        if (classIds.Length == 0)
            return [];
        var rows = await (
            from assignment in database.TeacherClassAssignments.AsNoTracking()
            join user in database.Users.AsNoTracking() on assignment.UserId equals user.Id
            where classIds.Contains(assignment.ClassSectionId)
            select new { assignment.ClassSectionId, user.Id, Name = user.FullName ?? user.Username }
        ).ToListAsync(cancellationToken);
        return rows.Select(row => (row.ClassSectionId, new ChatPerson(row.Id, row.Name, "teacher"))).ToList();
    }

    private void QueueSeen(Guid userId, Guid schoolId, Guid? conversationId)
    {
        _ = SeenAsync(userId, schoolId, conversationId);
    }

    private async Task SeenAsync(Guid userId, Guid schoolId, Guid? conversationId)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var session = scope.ServiceProvider.GetRequiredService<IDbSession>();
            session.SetUser(userId);
            session.SetSchool(schoolId);
            using var ambient = DbSessionAmbient.Push(session);
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
            var now = clock.GetUtcNow().UtcDateTime;
            var presence = await database.ChatPresences.FirstOrDefaultAsync(row => row.UserId == userId);
            if (presence is null)
            {
                database.ChatPresences.Add(new ChatPresence
                {
                    Id = Guid.NewGuid(),
                    SchoolId = schoolId,
                    UserId = userId,
                    LastSeenAtUtc = now
                });
            }
            else
            {
                presence.LastSeenAtUtc = now;
            }

            if (conversationId is Guid roomId)
            {
                var read = await database.ChatReads.FirstOrDefaultAsync(row => row.ConversationId == roomId && row.UserId == userId);
                if (read is null)
                {
                    database.ChatReads.Add(new ChatRead
                    {
                        Id = Guid.NewGuid(),
                        SchoolId = schoolId,
                        ConversationId = roomId,
                        UserId = userId,
                        LastReadAtUtc = now
                    });
                }
                else
                {
                    read.LastReadAtUtc = now;
                }
            }

            await database.SaveChangesAsync();
        }
        catch (Exception)
        {
            // Presence and read state must not delay opening the conversation.
        }
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(AppDbContext database, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
            return [];
        var users = await database.Users.AsNoTracking()
            .Where(row => userIds.Contains(row.Id))
            .Select(row => new { row.Id, row.FullName, row.Username })
            .ToListAsync(cancellationToken);
        return users.ToDictionary(row => row.Id, row => string.IsNullOrWhiteSpace(row.FullName) ? row.Username : row.FullName);
    }

    private async Task<Dictionary<Guid, DateTime>> PresenceAsync(AppDbContext database, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
            return [];
        var rows = await database.ChatPresences.AsNoTracking()
            .Where(row => userIds.Contains(row.UserId))
            .Select(row => new { row.UserId, row.LastSeenAtUtc })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.UserId, row => row.LastSeenAtUtc);
    }

    private async Task<Dictionary<Guid, int>> UnreadAsync(AppDbContext database, Guid userId, Guid[] ids, CancellationToken cancellationToken)
    {
        if (ids.Length == 0)
            return [];
        var rows = await (
            from message in database.ChatMessages.AsNoTracking()
            join read in database.ChatReads.AsNoTracking().Where(row => row.UserId == userId)
                on message.ConversationId equals read.ConversationId into reads
            from read in reads.DefaultIfEmpty()
            where ids.Contains(message.ConversationId)
                && message.SenderUserId != userId
                && (read == null || message.CreatedAtUtc > read.LastReadAtUtc)
            group message by message.ConversationId
            into grouped
            select new { grouped.Key, Count = grouped.Count() }
        ).ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Key, row => row.Count);
    }

    private async Task<Dictionary<Guid, (string Text, DateTime At)>> PreviewsAsync(AppDbContext database, Guid[] ids, CancellationToken cancellationToken)
    {
        if (ids.Length == 0)
            return [];
        var latest = await database.ChatMessages.AsNoTracking()
            .Where(row => ids.Contains(row.ConversationId))
            .GroupBy(row => row.ConversationId)
            .Select(group => new { ConversationId = group.Key, At = group.Max(row => row.CreatedAtUtc) })
            .ToListAsync(cancellationToken);
        if (latest.Count == 0)
            return [];
        var stamps = latest.Select(row => row.At).ToArray();
        var messages = await database.ChatMessages.AsNoTracking()
            .Where(row => ids.Contains(row.ConversationId) && stamps.Contains(row.CreatedAtUtc))
            .Select(row => new { row.ConversationId, row.Kind, row.Body, row.FileName, row.CreatedAtUtc })
            .ToListAsync(cancellationToken);
        var map = new Dictionary<Guid, (string Text, DateTime At)>();
        foreach (var row in latest)
        {
            var message = messages.FirstOrDefault(item => item.ConversationId == row.ConversationId && item.CreatedAtUtc == row.At);
            if (message is null)
                continue;
            var text = message.Kind switch
            {
                "voice" => "رسالة صوتية",
                "image" => message.FileName ?? "صورة",
                "file" => message.FileName ?? "ملف",
                "vote" => VoteLabel(message.Body),
                _ => message.Body
            };
            map[row.ConversationId] = (text, row.At);
        }
        return map;
    }

    private async Task TouchAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var row = await db.ChatPresences.FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (row is null)
        {
            db.ChatPresences.Add(new ChatPresence
            {
                Id = Guid.NewGuid(),
                SchoolId = actor.Context!.SchoolId,
                UserId = userId,
                LastSeenAtUtc = now
            });
        }
        else
        {
            row.LastSeenAtUtc = now;
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            foreach (var entry in db.ChangeTracker.Entries<ChatPresence>()
                         .Where(item => item.State is EntityState.Added or EntityState.Modified)
                         .ToArray())
                entry.State = EntityState.Detached;
        }
    }

    private async Task MarkReadAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var row = await db.ChatReads.FirstOrDefaultAsync(
            item => item.ConversationId == conversationId && item.UserId == userId,
            cancellationToken);
        if (row is null)
        {
            db.ChatReads.Add(new ChatRead
            {
                Id = Guid.NewGuid(),
                SchoolId = actor.Context!.SchoolId,
                ConversationId = conversationId,
                UserId = userId,
                LastReadAtUtc = now
            });
        }
        else
        {
            row.LastReadAtUtc = now;
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            foreach (var entry in db.ChangeTracker.Entries<ChatRead>()
                         .Where(item => item.State is EntityState.Added or EntityState.Modified)
                         .ToArray())
                entry.State = EntityState.Detached;
        }
    }

    private async Task RevealAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        await db.ChatHiddens.Where(row => row.ConversationId == conversationId).ExecuteDeleteAsync(cancellationToken);
    }

    private bool IsOnline(IReadOnlyDictionary<Guid, DateTime> presence, Guid userId)
    {
        if (!presence.TryGetValue(userId, out var seen))
            return false;
        return time.GetUtcNow().UtcDateTime - seen <= OnlineWindow;
    }

    private static IEnumerable<Guid> PeerIds(IEnumerable<ChatConversation> rooms, Guid userId)
    {
        foreach (var room in rooms)
        {
            var peer = Other(room, userId);
            if (peer is Guid id)
                yield return id;
        }
    }

    private static Guid? Other(ChatConversation room, Guid userId)
    {
        if (room.Kind != "direct")
            return null;
        if (room.UserLowId == userId)
            return room.UserHighId;
        if (room.UserHighId == userId)
            return room.UserLowId;
        return null;
    }

    private static ChatMessageResponse ToResponse(ChatMessage message, string senderName) =>
        new(
            message.Id,
            message.ConversationId,
            message.SenderUserId,
            senderName,
            message.Kind,
            message.Body,
            message.FileName,
            message.Payload is { Length: > 0 },
            message.CreatedAtUtc,
            message.ReplyToMessageId,
            message.ReplySenderName,
            message.ReplyBody);

    private static bool IsVoice(string contentType, string name)
    {
        if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return true;
        var extension = Path.GetExtension(name);
        return extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".aac", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webm", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".opus", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImage(string contentType, string name)
    {
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return true;
        var extension = Path.GetExtension(name);
        return extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif";
    }

    private static readonly ConcurrentDictionary<string, LivePass> LivePasses = new();

    public async Task<LivePass?> AuthorizeLive(Guid userId, Guid contextId, Guid conversationId)
    {
        var key = $"{userId:N}:{conversationId:N}";
        if (LivePasses.TryGetValue(key, out var cached) && cached.UntilTicks > Environment.TickCount64)
            return cached;
        var loaded = await LoadLivePass(userId, contextId, conversationId);
        if (loaded is null)
            return null;
        LivePasses[key] = loaded;
        return loaded;
    }

    public void AcceptLive(
        LivePass pass,
        Guid messageId,
        string body,
        DateTime createdAtUtc,
        Guid? replyToMessageId = null,
        string? replySenderName = null,
        string? replyBody = null)
    {
        var reply = ReplyOf(replyToMessageId, replySenderName, replyBody);
        _ = SaveLive(pass, messageId, body, createdAtUtc, reply.Id, reply.Name, reply.Text);
    }

    private async Task<LivePass?> LoadLivePass(Guid userId, Guid contextId, Guid conversationId)
    {
        await using var scope = scopes.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<IDbSession>();
        session.SetUser(userId);
        using var ambient = DbSessionAmbient.Push(session);
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var context = await database.UserContexts.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == contextId && row.UserId == userId && row.RevokedAtUtc == null);
        if (context is null)
            return null;
        session.SetSchool(context.SchoolId);
        var room = await database.ChatConversations.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == conversationId);
        if (room is null)
            return null;

        Guid[] recipients;
        if (room.Kind == "direct")
        {
            if (room.UserLowId != userId && room.UserHighId != userId)
                return null;
            recipients = [room.UserLowId ?? Guid.Empty, room.UserHighId ?? Guid.Empty];
        }
        else if (room.Kind == "group" && room.ClassSectionId is Guid classId)
        {
            if (!await LiveMember(database, context, userId, classId))
                return null;
            var roster = (await StudentsOfAsync(database, [classId], CancellationToken.None))
                .Concat(await ParentsOfAsync(database, [classId], CancellationToken.None))
                .Concat(await TeachersOfAsync(database, [classId], CancellationToken.None));
            recipients = roster.Select(row => row.Person.UserId).Append(userId).Distinct().ToArray();
        }
        else
        {
            return null;
        }

        var user = await database.Users.AsNoTracking()
            .Where(row => row.Id == userId)
            .Select(row => new { row.FullName, row.Username })
            .FirstOrDefaultAsync();
        var name = user is null || string.IsNullOrWhiteSpace(user.FullName) ? user?.Username ?? "" : user.FullName;
        return new LivePass(userId, context.SchoolId, name, conversationId, recipients, Environment.TickCount64 + 60_000);
    }

    private static async Task<bool> LiveMember(AppDbContext database, UserContext context, Guid userId, Guid classId)
    {
        if (context.Role == AppRole.Student && context.StudentId is Guid studentId)
        {
            return await database.Enrollments.AsNoTracking().AnyAsync(
                row => row.StudentId == studentId && row.ClassSectionId == classId);
        }

        if (context.Role == AppRole.Parent)
        {
            return await (
                from guardian in database.Guardians.AsNoTracking()
                join link in database.GuardianStudents.AsNoTracking() on guardian.Id equals link.GuardianId
                join enrollment in database.Enrollments.AsNoTracking() on link.StudentId equals enrollment.StudentId
                where guardian.UserId == userId && enrollment.ClassSectionId == classId
                select guardian.Id
            ).AnyAsync();
        }

        if (context.Role == AppRole.Teacher)
        {
            return await database.TeacherClassAssignments.AsNoTracking().AnyAsync(
                row => row.UserId == userId && row.ClassSectionId == classId);
        }

        return false;
    }

    private async Task SaveLive(
        LivePass pass,
        Guid messageId,
        string body,
        DateTime createdAtUtc,
        Guid? replyToMessageId,
        string? replySenderName,
        string? replyBody)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var session = scope.ServiceProvider.GetRequiredService<IDbSession>();
            session.SetUser(pass.UserId);
            session.SetSchool(pass.SchoolId);
            using var ambient = DbSessionAmbient.Push(session);
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var exists = await database.ChatMessages.AsNoTracking().AnyAsync(row => row.Id == messageId);
            if (!exists)
            {
                database.ChatMessages.Add(new ChatMessage
                {
                    Id = messageId,
                    SchoolId = pass.SchoolId,
                    ConversationId = pass.ConversationId,
                    SenderUserId = pass.UserId,
                    Kind = "text",
                    Body = body,
                    ReplyToMessageId = replyToMessageId,
                    ReplySenderName = replySenderName,
                    ReplyBody = replyBody,
                    CreatedAtUtc = createdAtUtc
                });
                await database.SaveChangesAsync();
            }

            await realtime.Notify(pass.Recipients, pass.ConversationId);
        }
        catch (Exception)
        {
            await realtime.Removed(pass.Recipients, pass.ConversationId, messageId);
        }
    }

    private static (Guid? Id, string? Name, string? Text) ReplyOf(Guid? id, string? name, string? text)
    {
        if (id is null)
            return (null, null, null);
        return (id, Clip(name, 200), Clip(text, 280));
    }

    private static string? Clip(string? value, int max)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
            return null;
        return text.Length > max ? text[..max] : text;
    }

    private static string VoteLabel(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("question", out var question) && question.GetString() is { Length: > 0 } text)
                return text;
        }
        catch (JsonException)
        {
        }

        return "تصويت";
    }

    private static readonly JsonSerializerOptions VoteJson = new(JsonSerializerDefaults.Web);

    private sealed class VoteDocument
    {
        public string Question { get; set; } = "";
        public List<VoteChoice> Options { get; set; } = [];
        public Dictionary<string, string> Votes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class VoteChoice
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
    }

    private bool Ready(out Guid userId, out Denied denied)
    {
        if (actor.IsAuthenticated && actor.UserId != Guid.Empty && actor.Context is not null)
        {
            userId = actor.UserId;
            denied = default;
            return true;
        }

        userId = Guid.Empty;
        denied = Denied.Unauthorized();
        return false;
    }

    private static ServiceResult<T> NotFound<T>() =>
        new ServiceResult<T>.Fail(StatusCodes.Status404NotFound, ProblemResponses.NotFound, "Not Found", "The chat was not found.");

    private static ServiceResult<T> Fail<T>(int status, string detail) =>
        new ServiceResult<T>.Fail(
            status,
            status == StatusCodes.Status403Forbidden ? ProblemResponses.Forbidden : "urn:smartschool:invalid",
            status == StatusCodes.Status403Forbidden ? "Forbidden" : "Invalid request",
            detail);

    private sealed record ChatPerson(Guid UserId, string Name, string Role);

    private readonly struct Denied
    {
        private Denied(ServiceResult<bool>.Fail failure) => Failure = failure;
        private ServiceResult<bool>.Fail Failure { get; }
        public static Denied Unauthorized() => new(new ServiceResult<bool>.Fail(
            StatusCodes.Status401Unauthorized, ProblemResponses.Unauthorized, "Unauthorized", "Authentication is required."));
        public ServiceResult<T> As<T>() => new ServiceResult<T>.Fail(Failure.Status, Failure.Type, Failure.Title, Failure.Detail);
    }
}

public sealed record LivePass(Guid UserId, Guid SchoolId, string FullName, Guid ConversationId, Guid[] Recipients, long UntilTicks);
