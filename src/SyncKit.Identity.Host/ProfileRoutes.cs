using SyncKit.Auth;
using SyncKit.Contract;
using SyncKit.Identity;

namespace SyncKit.Identity.Host;

public static class ProfileRoutes {
    public static void Map(
        WebApplication app, SessionCookieOptions sessionOptions, string avatarStorageDir,
        RevocationStore revocations, ProfileService profiles, UserQueries users) {
        Func<string, CancellationToken, Task<bool>> isRevoked = revocations.IsRevokedAsync;

        var profileRoutes = app.MapGroup("/profile");

        profileRoutes.MapGet("/me", async (HttpContext ctx) => {
            var userId = await ProfileAuth.TryGetUserIdAsync(ctx, sessionOptions, isRevoked, ctx.RequestAborted);
            if (userId is null) return Results.Unauthorized();

            var user = await users.GetAsync(userId.Value, ctx.RequestAborted);
            if (user is null) return Results.NotFound();
            var identities = await profiles.ListIdentitiesAsync(userId.Value, ctx.RequestAborted);

            return Results.Ok(new ProfileResponse {
                UserId = user.UserId,
                Username = user.Username,
                Avatar = user.Avatar,
                AvatarIsCustom = user.AvatarIsCustom,
                Identities = [.. identities.Select(i => new ProfileIdentityResponse {
                    Provider = i.Provider,
                    Subject = i.Subject,
                    Username = i.Username,
                    Avatar = i.Avatar,
                    LinkedAt = i.LinkedAt,
                })],
            });
        });

        profileRoutes.MapPost("/identities/{provider}/{subject}/unlink", async (HttpContext ctx, string provider, string subject) => {
            var userId = await ProfileAuth.TryGetUserIdAsync(ctx, sessionOptions, isRevoked, ctx.RequestAborted);
            if (userId is null) return Results.Unauthorized();

            var result = await profiles.UnlinkAsync(userId.Value, provider, subject, ctx.RequestAborted);
            return result switch {
                UnlinkResult.Unlinked => Results.NoContent(),
                UnlinkResult.LastIdentity => Results.BadRequest("cannot unlink the last identity on an account"),
                _ => Results.NotFound(),
            };
        });

        profileRoutes.MapPost("/avatar", async (HttpContext ctx) => {
            var userId = await ProfileAuth.TryGetUserIdAsync(ctx, sessionOptions, isRevoked, ctx.RequestAborted);
            if (userId is null) return Results.Unauthorized();
            if (!ctx.Request.HasFormContentType) return Results.BadRequest("expected multipart/form-data");

            var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
            var file = form.Files["file"];
            if (file is null) return Results.BadRequest("missing file");

            await using var stream = file.OpenReadStream();
            var url = await AvatarStore.SaveAsync(avatarStorageDir, userId.Value, stream, file.ContentType, ctx.RequestAborted);
            if (url is null) return Results.BadRequest("unsupported content-type or file too large");

            await profiles.SetCustomAvatarAsync(userId.Value, url, ctx.RequestAborted);
            return Results.NoContent();
        });

        profileRoutes.MapPost("/avatar/select", async (HttpContext ctx, AvatarSelectRequest req) => {
            var userId = await ProfileAuth.TryGetUserIdAsync(ctx, sessionOptions, isRevoked, ctx.RequestAborted);
            if (userId is null) return Results.Unauthorized();

            var selected = await profiles.SelectIdentityAvatarAsync(userId.Value, req.Provider, req.Subject, ctx.RequestAborted);
            return selected ? Results.NoContent() : Results.NotFound();
        });

        app.MapGet("/avatars/{userId:guid}", (Guid userId) => {
            if (!AvatarStore.TryGetPath(avatarStorageDir, userId, out var path, out var contentType))
                return Results.NotFound();
            return Results.File(path, contentType, enableRangeProcessing: false);
        });
    }
}
