using Microsoft.EntityFrameworkCore;
using SmartSchool.Api.Auth;
using SmartSchool.Api.Hosting;
using SmartSchool.Contracts;
using SmartSchool.Core;
using SmartSchool.Infrastructure.Persistence;

namespace SmartSchool.Api.Supervisors;

public sealed class SupervisorService(RequestActor actor, AppDbContext db)
{
    public async Task<ServiceResult<SupervisorHomeResponse>> HomeAsync(CancellationToken cancellationToken)
    {
        if (!actor.IsAuthenticated)
        {
            return new ServiceResult<SupervisorHomeResponse>.Fail(
                StatusCodes.Status401Unauthorized,
                ProblemResponses.Unauthorized,
                "Unauthorized",
                "Authentication is required.");
        }

        if (actor.MustChangePassword)
        {
            return new ServiceResult<SupervisorHomeResponse>.Fail(
                StatusCodes.Status403Forbidden,
                ProblemResponses.PasswordChange,
                "Password change required",
                "Change the temporary password before continuing.");
        }

        if (actor.Context is null || actor.Context.Role != AppRole.Supervisor)
        {
            return new ServiceResult<SupervisorHomeResponse>.Fail(
                StatusCodes.Status403Forbidden,
                ProblemResponses.Forbidden,
                "Forbidden",
                "A supervisor context is required.");
        }

        var trips = await db.SchoolTrips.AsNoTracking()
            .OrderByDescending(trip => trip.TripOn)
            .ThenBy(trip => trip.Title)
            .Take(6)
            .ToListAsync(cancellationToken);
        var total = await db.SchoolTrips.CountAsync(cancellationToken);
        var completed = await db.SchoolTrips.CountAsync(trip => trip.Status == "completed", cancellationToken);
        return new ServiceResult<SupervisorHomeResponse>.Success(
            new SupervisorHomeResponse(
                actor.FullName,
                null,
                total,
                completed,
                trips.Select(trip => new SupervisorTripResponse(trip.Title, trip.Subtitle, trip.StudentCount, trip.SupervisorName)).ToArray()));
    }
}
