namespace SmartSchool.Contracts;

public sealed record HomeworkItemResponse(
    Guid Id,
    string Title,
    string Description,
    string Instructions,
    string SubjectName,
    string ClassName,
    string GradeName,
    string TeacherName,
    DateOnly DueOn,
    string Status,
    string? Note,
    int SubmittedCount = 0,
    int EnrolledCount = 0);

public sealed record HomeworkListResponse(IReadOnlyList<HomeworkItemResponse> Items);

public sealed record TeacherClassOptionResponse(
    Guid ClassSectionId,
    Guid SubjectId,
    string GradeName,
    string ClassName,
    string SubjectName);

public sealed record TeacherClassOptionListResponse(IReadOnlyList<TeacherClassOptionResponse> Items);

public sealed class CreateHomeworkRequest
{
    public Guid ClassSectionId { get; set; }
    public Guid? SubjectId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Instructions { get; set; } = "";
    public DateOnly DueOn { get; set; }
}

public sealed class UpdateHomeworkRequest
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Instructions { get; set; } = "";
    public DateOnly DueOn { get; set; }
}

public sealed class SubmitHomeworkRequest
{
    public string? Note { get; set; }
}

public sealed record HomeworkSubmissionResponse(
    Guid Id,
    Guid StudentId,
    string StudentName,
    DateTime SubmittedAtUtc,
    string Status,
    string? Note);

public sealed record HomeworkSubmissionListResponse(IReadOnlyList<HomeworkSubmissionResponse> Items);

public sealed class ReviewHomeworkRequest
{
    public string? Note { get; set; }
}
