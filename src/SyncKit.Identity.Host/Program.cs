using Npgsql;
using SyncKit.Contract;
using SyncKit.Db;
using SyncKit.Identity;
using SyncKit.Identity.Models;

var connString = Environment.GetEnvironmentVariable("IDENTITY_DB_CONNECTION")
    ?? throw new InvalidOperationException("IDENTITY_DB_CONNECTION is required");
var apiSecret = Environment.GetEnvironmentVariable("IDENTITY_API_SECRET")
    ?? throw new InvalidOperationException("IDENTITY_API_SECRET is required");
var port = Environment.GetEnvironmentVariable("IDENTITY_API_PORT") ?? "8090";
var adminIds = Environment.GetEnvironmentVariable("IDENTITY_ADMIN_DISCORD_IDS");

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://*:{port}");

var dataSource = NpgsqlDataSource.Create(connString);
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton(AdminAllowlist.FromConfig(adminIds));
builder.Services.AddSingleton<IdentityResolver>();
builder.Services.AddSingleton<RevocationStore>();
builder.Services.AddSingleton<UserQueries>();

var app = builder.Build();

await using (var conn = await dataSource.OpenConnectionAsync())
    await Migrator.MigrateAsync(conn, Path.Combine(AppContext.BaseDirectory, "Migrations"));

app.Use(async (ctx, next) =>
{
    var auth = ctx.Request.Headers.Authorization.ToString();
    if (auth != $"Bearer {apiSecret}")
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsync("unauthorized");
        return;
    }
    await next();
});

app.MapPost("/identity/resolve", async (IdentityResolveRequest req, IdentityResolver resolver, CancellationToken ct) =>
{
    var result = await resolver.ResolveAsync(req.Provider, req.Subject, req.DiscordId, req.Username, req.Avatar, ct);
    return Results.Ok(new IdentityResolveResponse
    {
        UserId = result.UserId,
        Role = result.Role,
        DiscordId = result.DiscordId,
        IsNew = result.IsNew,
    });
});

app.MapGet("/identity/{userId:guid}", async (Guid userId, UserQueries users, CancellationToken ct) =>
{
    var user = await users.GetAsync(userId, ct);
    if (user is null) return Results.NotFound();
    return Results.Ok(ToResponse(user));
});

// Session revocation is sid-keyed only (revoked_sessions has no user_id column) - no userId in
// these routes; back-channel logout tokens only ever carry a sid, never a user_id, so requiring
// one here would force every caller into an unnecessary extra lookup.
app.MapPost("/identity/revoke-session", async (RevokeSessionRequest req, RevocationStore store, CancellationToken ct) =>
{
    await store.RevokeAsync(req.Sid, ct);
    return Results.NoContent();
});

app.MapGet("/identity/sessions/{sid}/revoked", async (string sid, RevocationStore store, CancellationToken ct) =>
    Results.Ok(await store.IsRevokedAsync(sid, ct)));

app.MapPost("/identity/merge", async (MergeUsersRequest req, IdentityResolver resolver, CancellationToken ct) =>
{
    var winner = await resolver.MergeAsync(req.KeepUserId, req.MergeUserId, ct);
    return Results.Ok(new { userId = winner });
});

app.MapGet("/identity/admin/users", async (UserQueries users, CancellationToken ct) =>
    Results.Ok((await users.ListAsync(ct)).Select(ToResponse)));

app.MapPost("/identity/{userId:guid}/role", async (Guid userId, SetRoleRequest req, UserQueries users, CancellationToken ct) =>
{
    var ok = await users.SetRoleAsync(userId, UserRoles.Parse(req.Role), ct);
    return ok ? Results.NoContent() : Results.NotFound();
});

app.Run();

static IdentityUserResponse ToResponse(User u) => new()
{
    UserId = u.UserId,
    DiscordId = u.DiscordId,
    Username = u.Username,
    Avatar = u.Avatar,
    Role = u.Role,
    CreatedAt = u.CreatedAt,
    LastLoginAt = u.LastLoginAt,
};
