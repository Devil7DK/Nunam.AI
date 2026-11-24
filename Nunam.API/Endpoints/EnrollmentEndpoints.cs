using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nunam.API.Agents;
using Nunam.API.Auth;
using Nunam.API.Contracts;
using Nunam.Core.Agents;
using Nunam.Core.Data;
using Nunam.Core.Entities;

namespace Nunam.API.Endpoints
{
    /// <summary>
    /// Adding and removing machines from the UI. An admin creates a one-time code; the command the UI
    /// shows installs the indexer and trades the code for the machine's token.
    /// </summary>
    public static class EnrollmentEndpoints
    {
        public static IEndpointRouteBuilder MapEnrollmentEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/machines/enrollments", CreateAsync);
            app.MapGet("/api/machines/enrollments/{id:guid}", GetAsync);
            app.MapDelete("/api/machines/{machineId:guid}", DeleteMachineAsync).RequireAuthorization(Policies.Admin);

            var agent = app.MapGroup("/api/agent").AllowAnonymous();
            agent.MapPost("/enroll", EnrollAsync).RequireRateLimiting(Policies.PasswordAttempts);
            agent.MapGet("/install.sh", () => Script("install.sh"));
            agent.MapGet("/install.ps1", () => Script("install.ps1"));
            agent.MapGet("/packages/{kind}", DownloadPackage);
            return app;
        }

        /// <summary>Admins add machines; a machine's owners may also reconnect it.</summary>
        private static async Task<Results<Ok<EnrollmentCreated>, NotFound, ForbidHttpResult, ValidationProblem>> CreateAsync(
            CreateEnrollmentRequest request, ClaimsPrincipal user, MachineScope scope, NunamDbContext db,
            IndexerPackages packages, IOptions<AgentOptions> options, CancellationToken cancellationToken)
        {
            if (request.MachineId is { } machineId)
            {
                if (!await scope.CanManageAsync(user, machineId, cancellationToken)
                    || !await db.Machines.AnyAsync(m => m.Id == machineId, cancellationToken))
                {
                    return TypedResults.NotFound();
                }
            }
            else if (!user.IsAdmin())
            {
                return TypedResults.Forbid();
            }

            var name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
            if (name?.Length > 200)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Use at most 200 characters."] });
            }

            var code = SessionTokens.Create();
            var enrollment = new MachineEnrollment
            {
                CodeHash = SessionTokens.Hash(code),
                MachineId = request.MachineId,
                Name = request.MachineId is null ? name : null,
                CreatedBy = user.Identity!.Name!,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(options.Value.EnrollmentMinutes),
            };
            db.MachineEnrollments.Add(enrollment);
            await db.SaveChangesAsync(cancellationToken);

            return TypedResults.Ok(new EnrollmentCreated(enrollment.Id, code, enrollment.ExpiresAt, packages.Available()));
        }

        private static async Task<Results<Ok<EnrollmentState>, NotFound>> GetAsync(
            Guid id, ClaimsPrincipal user, NunamDbContext db, CancellationToken cancellationToken)
        {
            var userName = user.Identity!.Name!;
            var isAdmin = user.IsAdmin();
            var state = await db.MachineEnrollments
                .Where(e => e.Id == id && (isAdmin || e.CreatedBy == userName))
                .Select(e => new EnrollmentState(e.Id, e.ExpiresAt, e.UsedAt, e.EnrolledMachineId))
                .FirstOrDefaultAsync(cancellationToken);
            return state is null ? TypedResults.NotFound() : TypedResults.Ok(state);
        }

        /// <summary>Removes the machine with its files, jobs and token; its indexer can't sign in any more.</summary>
        private static async Task<Results<NoContent, NotFound>> DeleteMachineAsync(
            Guid machineId, ClaimsPrincipal user, NunamDbContext db, ILogger<MachineScope> logger, CancellationToken cancellationToken)
        {
            var deleted = await db.Machines.Where(m => m.Id == machineId).ExecuteDeleteAsync(cancellationToken);
            if (deleted == 0) return TypedResults.NotFound();
            logger.LogInformation("{User} removed machine {MachineId}", user.Identity!.Name, machineId);
            return TypedResults.NoContent();
        }

        private static async Task<Results<Ok<EnrollResponse>, ValidationProblem, ProblemHttpResult>> EnrollAsync(
            EnrollRequest request, NunamDbContext db, ILogger<MachineScope> logger, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 128
                || string.IsNullOrWhiteSpace(request.Hostname) || request.Hostname.Length > 255 || request.Name?.Length > 200)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["code"] = ["Send the code, the hostname, and a name of up to 200 characters."] });
            }

            var hash = SessionTokens.Hash(request.Code.Trim());
            var now = DateTimeOffset.UtcNow;
            var enrollment = await db.MachineEnrollments
                .FirstOrDefaultAsync(e => e.CodeHash == hash && e.UsedAt == null && e.ExpiresAt > now, cancellationToken);
            // Marked used first and only where still unused, so two installs racing with one code can't both get a token.
            if (enrollment is null || await db.MachineEnrollments
                    .Where(e => e.Id == enrollment.Id && e.UsedAt == null)
                    .ExecuteUpdateAsync(e => e.SetProperty(x => x.UsedAt, now), cancellationToken) == 0)
            {
                return TypedResults.Problem("The code is unknown, used or expired. Create a new one under Machines in the web UI.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var hostname = request.Hostname.Trim();
            Machine? machine;
            if (enrollment.MachineId is { } machineId)
            {
                machine = await db.Machines.AsTracking().FirstOrDefaultAsync(m => m.Id == machineId, cancellationToken);
                if (machine is null)
                {
                    return TypedResults.Problem("The machine this code was for has been removed.", statusCode: StatusCodes.Status400BadRequest);
                }
                machine.Hostname = hostname;
            }
            else
            {
                var name = enrollment.Name ?? (string.IsNullOrWhiteSpace(request.Name) ? hostname : request.Name.Trim());
                machine = new Machine { Name = name, Hostname = hostname };
                db.Machines.Add(machine);
            }

            var token = SessionTokens.Create();
            machine.TokenHash = SessionTokens.Hash(token);
            machine.TokenIssuedAt = now;
            machine.LastSeen = now;
            await db.SaveChangesAsync(cancellationToken);

            await db.MachineEnrollments.Where(e => e.Id == enrollment.Id)
                .ExecuteUpdateAsync(e => e.SetProperty(x => x.EnrolledMachineId, machine.Id), cancellationToken);

            logger.LogInformation("{Hostname} ({OperatingSystem}) enrolled as machine {MachineId} with a code from {User}",
                hostname, request.OperatingSystem, machine.Id, enrollment.CreatedBy);
            return TypedResults.Ok(new EnrollResponse(machine.Id, machine.Name, token));
        }

        private static IResult Script(string name)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Nunam.API.Agents.{name}")!;
            using var reader = new StreamReader(stream);
            return Results.Text(reader.ReadToEnd(), "text/plain; charset=utf-8");
        }

        private static IResult DownloadPackage(string kind, string? arch, IndexerPackages packages) =>
            packages.Find(kind, arch ?? "x64") is { } file
                ? Results.File(file.FullName, "application/octet-stream", file.Name, enableRangeProcessing: true)
                : Results.Problem($"The server has no {kind} package for {arch ?? "x64"}. Copy one into its package folder.",
                    statusCode: StatusCodes.Status404NotFound);
    }
}
