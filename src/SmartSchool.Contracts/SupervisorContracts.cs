namespace SmartSchool.Contracts;

public sealed record SupervisorTripResponse(
    string Title,
    string Subtitle,
    int StudentCount,
    string SupervisorName);

public sealed record SupervisorHomeResponse(
    string FullName,
    int? Rating,
    int TripTotal,
    int CompletedTripCount,
    IReadOnlyList<SupervisorTripResponse> Trips);
