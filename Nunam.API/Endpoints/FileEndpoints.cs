using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Nunam.API.Auth;
using Nunam.API.Contracts;
using Nunam.API.Services;
using Nunam.Core.Data;
using Nunam.Core.Files;

namespace Nunam.API.Endpoints
{
    public static class FileEndpoints
    {
        private const int MaxContentChars = 20_000;

        public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
        {
            var files = app.MapGroup("/api/files");
            files.MapGet("/{id:guid}", GetFileAsync).WithName("GetFile");
            files.MapGet("/{id:guid}/preview", GetPreviewAsync)
                .WithName("GetPreview")
                .WithSummary("The stored preview image; pass size to get a thumbnail whose longer edge is at most that many pixels.");
            return app;
        }

        private static async Task<Results<Ok<FileDetails>, NotFound>> GetFileAsync(
            Guid id, ClaimsPrincipal user, MachineScope scope, NunamDbContext db, CancellationToken cancellationToken)
        {
            var machineIds = await scope.VisibleMachineIdsAsync(user, cancellationToken);
            var file = await db.Files
                .Where(f => f.Id == id && (machineIds == null || machineIds.Contains(f.MachineId)))
                .Select(f => new
                {
                    f.Id, f.FileName, f.Path, Machine = f.Machine.Name, f.FileType, f.Extension, f.SizeBytes,
                    f.FileCreatedAt, f.FileModifiedAt, f.IndexedAt, f.Description, f.DescriptionModel, f.DescriptionProvider, f.DescribedAt, f.Metadata,
                    Content = f.Content!.Substring(0, MaxContentChars),
                    ContentLength = f.Content == null ? 0 : f.Content.Length,
                    HasPreview = f.PreviewImage != null,
                    HasEmbedding = f.Embedding != null,
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (file is null) return TypedResults.NotFound();

            return TypedResults.Ok(new FileDetails(
                file.Id, file.FileName, file.Path, file.Machine, file.FileType, file.Extension, file.SizeBytes,
                file.FileCreatedAt, file.FileModifiedAt, file.IndexedAt, file.Description,
                file.DescriptionModel, file.DescriptionProvider, file.DescribedAt,
                file.Metadata is null ? null : JsonDocument.Parse(file.Metadata).RootElement.Clone(),
                file.Content, file.ContentLength, file.ContentLength > MaxContentChars,
                file.HasPreview, file.HasEmbedding));
        }

        private static async Task<IResult> GetPreviewAsync(
            Guid id, int? size, HttpContext http, ClaimsPrincipal user, MachineScope scope, NunamDbContext db,
            ThumbnailService thumbnails, CancellationToken cancellationToken)
        {
            var machineIds = await scope.VisibleMachineIdsAsync(user, cancellationToken);
            var visible = db.Files.Where(f => f.Id == id && (machineIds == null || machineIds.Contains(f.MachineId)));
            var file = await visible
                .Where(f => f.PreviewImage != null)
                .Select(f => new { f.Extension, f.LastModified })
                .FirstOrDefaultAsync(cancellationToken);
            if (file is null) return TypedResults.NotFound();

            // no-cache, not max-age: the browser asks again every time, so access taken away ends at once
            // instead of when the cached copy expires. The answer is usually a body-less 304.
            http.Response.Headers.CacheControl = "private, no-cache";
            var version = file.LastModified.UtcTicks;
            var etag = new EntityTagHeaderValue($"\"{version:x}-{size ?? 0}\"");
            if (http.Request.GetTypedHeaders().IfNoneMatch.Any(t => t.Compare(etag, useStrongComparison: false)))
            {
                http.Response.Headers.ETag = etag.ToString();
                return TypedResults.StatusCode(StatusCodes.Status304NotModified);
            }

            var preview = await visible.Select(f => f.PreviewImage).FirstOrDefaultAsync(cancellationToken);
            if (preview is null) return TypedResults.NotFound();

            var image = size is { } edge ? thumbnails.GetThumbnail(id, version, preview, edge) : preview;
            if (image is null) return TypedResults.NotFound();

            return TypedResults.File(image, MediaTypes.Detect(image, file.Extension), entityTag: etag);
        }
    }
}
