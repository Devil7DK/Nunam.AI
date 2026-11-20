using System.Security.Claims;
using Nunam.API.Auth;
using Nunam.API.Contracts;
using Nunam.API.Services;

namespace Nunam.API.Endpoints
{
    public static class SearchEndpoints
    {
        public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/search", SearchAsync)
                .WithName("Search")
                .WithSummary("Search indexed files. An empty q lists files newest first, with the same filters.");
            return app;
        }

        private static async Task<IResult> SearchAsync(
            [AsParameters] SearchRequest request, ClaimsPrincipal user, MachineScope scope, SearchService search,
            CancellationToken cancellationToken)
        {
            var parsed = SearchRequestParser.Parse(request, out var errors);
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

            try
            {
                var machineIds = await scope.VisibleMachineIdsAsync(user, cancellationToken);
                return TypedResults.Ok(await search.SearchAsync(parsed, machineIds, cancellationToken));
            }
            catch (SemanticSearchUnavailableException ex)
            {
                return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == Npgsql.PostgresErrorCodes.QueryCanceled && !cancellationToken.IsCancellationRequested)
            {
                return TypedResults.Problem("The search took too long. Try fewer or more specific words.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }
    }
}
