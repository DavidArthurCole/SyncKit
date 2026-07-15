using System.Text.Json;
using Discord.WebSocket;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Scriban;
using SyncKit.Auth;
using SyncKit.Contract;

namespace SyncKit.Bot;

public sealed record AdminConfigRequest(
    string? DashboardChannelId,
    string? GithubFeedThreadId,
    string? DeployNotificationsThreadId,
    string? SuccessEmbedJson,
    string? FailureEmbedJson,
    string? UptodateEmbedJson);

public sealed record VariableDoc(string Name, string Desc);

public sealed record AdminConfigResponse(
    string? DashboardChannelId,
    string? GithubFeedThreadId,
    string? DeployNotificationsThreadId,
    string? SuccessEmbedJson,
    string? FailureEmbedJson,
    string? UptodateEmbedJson,
    string? GithubWebhookUrl,
    EmbedSpec DefaultSuccess,
    EmbedSpec DefaultFailure,
    EmbedSpec DefaultUptodate,
    IReadOnlyList<VariableDoc> Variables) {

    private static List<VariableDoc> Vars() {
        var list = new List<VariableDoc>(DeployEmbedDefaults.Variables.Count);
        foreach (var (name, desc) in DeployEmbedDefaults.Variables) list.Add(new VariableDoc(name, desc));
        return list;
    }

    public static AdminConfigResponse From(
        ChannelConfig? cc,
        string? dashboardChannelId,
        string? githubFeedThreadId,
        string? deployNotificationsThreadId,
        string? githubWebhookUrl) => new(
            dashboardChannelId,
            githubFeedThreadId,
            deployNotificationsThreadId,
            cc?.SuccessEmbedJson,
            cc?.FailureEmbedJson,
            cc?.UptodateEmbedJson,
            githubWebhookUrl,
            DeployEmbedDefaults.Success,
            DeployEmbedDefaults.Failure,
            DeployEmbedDefaults.AlreadyUpToDate,
            Vars());
}

// Maps the /admin/* surface onto an existing WebApplication. Fully additive - callers only
// invoke Map when DiscordAdminClientId/Secret/CallbackUrl are all set (see SyncKitBotBuilder.WithAdminUi).
// The three optional delegates bridge to the guild's ChannelHub/ChannelStateStore; when null the
// webhook and prefill steps are skipped (admin UI stays usable before the bot is fully connected).
public static class AdminRoutes {
    // Pending OAuth states (state -> unused placeholder) live in-memory; a login completes within
    // the same process lifetime the state was issued in, so no DB persistence is needed here.
    private static readonly Dictionary<string, byte> PendingStates = [];

    public static void Map(
        WebApplication app,
        BotConfig cfg,
        ChannelConfigStore configStore,
        AdminSessionStore sessionStore,
        DiscordSocketClient client,
        Func<ulong, CancellationToken, Task<string?>>? ensureGithubWebhook = null,
        Func<CancellationToken, Task>? teardownGithubWebhook = null,
        Func<ThreadKind, CancellationToken, Task<string?>>? resolveExistingThreadId = null) {
        app.MapGet("/admin/login", () => {
            var (url, state) = DiscordOAuth.AuthUrl();
            lock (PendingStates) PendingStates[state] = 0;
            return Results.Redirect(url);
        });

        app.MapGet("/admin/callback", async (HttpContext ctx, string code, string state) => {
            lock (PendingStates) {
                if (!PendingStates.Remove(state))
                    return Results.Text("invalid or expired login attempt", "text/plain", null, StatusCodes.Status400BadRequest);
            }

            string? sessionToken = null;
            DiscordUser? discordUser = null;
            await DiscordOAuth.HandleCallbackAsync(code, state, (_, token, user) => {
                sessionToken = token;
                discordUser = user;
                return Task.CompletedTask;
            }, ctx.RequestAborted);

            if (sessionToken is null || discordUser is null)
                return Results.Text("login failed", "text/plain", null, StatusCodes.Status400BadRequest);

            if (!IsGuildAdmin(client, cfg.GuildId, discordUser.Id))
                return Results.Text("not a guild administrator", "text/plain", null, StatusCodes.Status403Forbidden);

            var expiresAt = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
            await sessionStore.CreateAsync(sessionToken, discordUser.Id, expiresAt, ctx.RequestAborted);

            ctx.Response.Cookies.Append("synckit_admin_session", sessionToken, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromDays(30) });
            return Results.Redirect("/admin");
        });

        app.MapPost("/admin/logout", async (HttpContext ctx) => {
            if (ctx.Request.Cookies.TryGetValue("synckit_admin_session", out var token) && !string.IsNullOrEmpty(token))
                await sessionStore.DeleteAsync(token, ctx.RequestAborted);
            ctx.Response.Cookies.Delete("synckit_admin_session");
            return Results.Redirect("/admin/login");
        });

        var admin = app.MapGroup("/admin").AddEndpointFilter(async (efiContext, next) => {
            var httpCtx = efiContext.HttpContext;
            if (httpCtx.Request.Path.Value is "/admin/login" or "/admin/callback") return await next(efiContext);

            if (!httpCtx.Request.Cookies.TryGetValue("synckit_admin_session", out var token) || string.IsNullOrEmpty(token))
                return (object?)Results.Redirect("/admin/login");

            var (found, discordId, expiresAt) = await sessionStore.LookupAsync(token, httpCtx.RequestAborted);
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!found || nowUnix > expiresAt)
                return (object?)Results.Redirect("/admin/login");

            if (!IsGuildAdmin(client, cfg.GuildId, discordId))
                return (object?)Results.Text("not authorized", "text/plain", null, StatusCodes.Status403Forbidden);

            var slid = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
            await sessionStore.TouchAsync(token, slid, httpCtx.RequestAborted);
            httpCtx.Items["DiscordId"] = discordId;
            return await next(efiContext);
        });

        admin.MapGet("/", () => Results.Content(PageHtml, "text/html"));

        admin.MapGet("/api/config", async (HttpContext ctx) => {
            var ct = ctx.RequestAborted;
            var cc = await configStore.GetAsync(cfg.GuildId, cfg.Name, ct);

            var githubFeedThreadId = cc?.GithubFeedThreadId;
            var deployNotificationsThreadId = cc?.DeployNotificationsThreadId;

            if (resolveExistingThreadId is not null) {
                githubFeedThreadId ??= await resolveExistingThreadId(ThreadKind.GithubFeed, ct);
                deployNotificationsThreadId ??= await resolveExistingThreadId(ThreadKind.DeployNotifications, ct);
            }

            // GET stays side-effect free: never create a webhook here, leave the url null until a save.
            var resp = AdminConfigResponse.From(cc, cc?.DashboardChannelId, githubFeedThreadId, deployNotificationsThreadId, null);
            return Results.Json(resp);
        });

        admin.MapPut("/api/config", async (HttpContext ctx, AdminConfigRequest req) => {
            var ct = ctx.RequestAborted;

            if (ValidateEmbedJson(req.SuccessEmbedJson) is string se) return BadRequestText($"Success embed invalid: {se}");
            if (ValidateEmbedJson(req.FailureEmbedJson) is string fe) return BadRequestText($"Failure embed invalid: {fe}");
            if (ValidateEmbedJson(req.UptodateEmbedJson) is string ue) return BadRequestText($"Already-up-to-date embed invalid: {ue}");

            var dashboardChannelId = Blank(req.DashboardChannelId);
            var githubFeedThreadId = Blank(req.GithubFeedThreadId);
            var deployNotificationsThreadId = Blank(req.DeployNotificationsThreadId);

            await configStore.UpsertAsync(cfg.GuildId, cfg.Name,
                dashboardChannelId, githubFeedThreadId, deployNotificationsThreadId,
                Blank(req.SuccessEmbedJson), Blank(req.FailureEmbedJson), Blank(req.UptodateEmbedJson),
                ct);

            string? githubWebhookUrl = null;
            if (githubFeedThreadId is not null && ulong.TryParse(githubFeedThreadId, out var threadId)) {
                if (ensureGithubWebhook is not null) githubWebhookUrl = await ensureGithubWebhook(threadId, ct);
            } else if (teardownGithubWebhook is not null) {
                await teardownGithubWebhook(ct);
            }

            var cc = await configStore.GetAsync(cfg.GuildId, cfg.Name, ct);
            var resp = AdminConfigResponse.From(cc, dashboardChannelId, githubFeedThreadId, deployNotificationsThreadId, githubWebhookUrl);
            return Results.Json(resp);
        });
    }

    private static IResult BadRequestText(string message) =>
        Results.Text(message, "text/plain", null, StatusCodes.Status400BadRequest);

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    // Returns null when valid (or absent), else a short message naming the first broken part.
    private static string? ValidateEmbedJson(string? json) {
        if (string.IsNullOrWhiteSpace(json)) return null;

        EmbedSpec? spec;
        try {
            spec = JsonSerializer.Deserialize<EmbedSpec>(json);
        } catch (JsonException ex) {
            return ex.Message;
        }
        if (spec is null) return "empty spec";

        foreach (var s in new[] { spec.AuthorName, spec.AuthorUrl, spec.AuthorIconUrl, spec.Title, spec.TitleUrl, spec.Description, spec.ImageUrl, spec.ThumbnailUrl, spec.FooterText, spec.FooterIconUrl }) {
            if (BadTemplate(s)) return "template parse error";
        }
        if (spec.Fields is not null) {
            foreach (var f in spec.Fields) {
                if (BadTemplate(f.Name) || BadTemplate(f.Value)) return "field template parse error";
            }
        }
        return null;
    }

    private static bool BadTemplate(string? s) =>
        !string.IsNullOrEmpty(s) && Template.Parse(s).HasErrors;

    private static bool IsGuildAdmin(DiscordSocketClient client, string guildIdStr, string discordId) {
        if (!ulong.TryParse(guildIdStr, out var guildId) || !ulong.TryParse(discordId, out var userId)) return false;
        var guild = client.GetGuild(guildId);
        var member = guild?.GetUser(userId);
        return member is not null && member.GuildPermissions.Administrator;
    }

    private const string PageHtml = """
        <!doctype html>
        <html>
        <head>
        <meta charset="utf-8">
        <title>SyncKit Bot Config</title>
        <style>
        body { font-family: system-ui, sans-serif; max-width: 820px; margin: 2rem auto; padding: 0 1rem; color: #e6e6e6; background: #1b1c20; }
        h1 { font-size: 1.4rem; }
        h2 { font-size: 1.1rem; margin-top: 2rem; border-bottom: 1px solid #3a3b40; padding-bottom: 0.3rem; }
        h3 { font-size: 0.95rem; margin: 1.2rem 0 0.3rem; }
        label { display: block; margin-top: 0.9rem; font-weight: 600; font-size: 0.85rem; }
        input, textarea, select { width: 100%; box-sizing: border-box; padding: 0.45rem; font-family: inherit; font-size: 0.9rem; background: #26272c; color: #e6e6e6; border: 1px solid #3a3b40; border-radius: 4px; }
        input[type=color] { padding: 0.1rem; height: 2.2rem; width: 3rem; }
        input[type=checkbox] { width: auto; }
        textarea { min-height: 4.5rem; font-family: ui-monospace, monospace; }
        button { margin-top: 0.9rem; padding: 0.5rem 1rem; background: #4752c4; color: #fff; border: none; border-radius: 4px; cursor: pointer; font-size: 0.85rem; }
        button.secondary { background: #3a3b40; }
        button.small { margin-top: 0; padding: 0.3rem 0.6rem; }
        .row { display: flex; gap: 0.5rem; align-items: center; }
        .row > * { margin-top: 0; }
        .grid2 { display: grid; grid-template-columns: 1fr 1fr; gap: 0.5rem; }
        .grid3 { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 0.5rem; }
        .tabs { display: flex; gap: 0.3rem; margin-top: 0.9rem; }
        .tab { padding: 0.4rem 0.8rem; background: #26272c; border: 1px solid #3a3b40; border-radius: 4px; cursor: pointer; font-size: 0.85rem; }
        .tab.active { background: #4752c4; border-color: #4752c4; }
        .field-row { display: grid; grid-template-columns: 1fr 2fr auto auto; gap: 0.4rem; align-items: center; margin-top: 0.4rem; }
        .field-row label { margin-top: 0; font-weight: 400; display: flex; align-items: center; gap: 0.25rem; }
        table { border-collapse: collapse; width: 100%; margin-top: 0.5rem; font-size: 0.82rem; }
        th, td { border: 1px solid #3a3b40; padding: 0.35rem 0.5rem; text-align: left; }
        th { background: #26272c; }
        code { background: #26272c; padding: 0.1rem 0.3rem; border-radius: 3px; }
        #status { margin-left: 0.8rem; font-size: 0.85rem; }
        .hint { font-size: 0.8rem; color: #9a9ba0; margin-top: 0.2rem; }
        .readonly { background: #202126; }
        </style>
        </head>
        <body>
        <h1>Bot Channel Config</h1>

        <label for="dashboard">Dashboard channel ID</label>
        <input id="dashboard" type="text" placeholder="channel id">

        <h2>GitHub feed</h2>
        <label for="ghThread">Thread ID</label>
        <input id="ghThread" type="text" placeholder="thread id">
        <label for="ghWebhook">GitHub webhook URL</label>
        <div class="row">
          <input id="ghWebhook" type="text" class="readonly" readonly placeholder="saved after you set a thread id and save">
          <button type="button" class="secondary small" id="ghCopy">Copy</button>
        </div>
        <div class="hint">Paste this URL into your GitHub repo webhook settings. Blank until you save a thread ID.</div>

        <h2>Deploy notifications</h2>
        <label for="dnThread">Thread ID</label>
        <input id="dnThread" type="text" placeholder="thread id">

        <div class="tabs">
          <div class="tab active" data-event="success">Success</div>
          <div class="tab" data-event="failure">Failure</div>
          <div class="tab" data-event="uptodate">Already up to date</div>
        </div>

        <div id="builder">
          <div class="row" style="margin-top:0.9rem;">
            <div>
              <label for="eColor">Color</label>
              <input id="eColor" type="color" value="#5865f2">
            </div>
            <div style="flex:1;">
              <label for="eColorHex">Hex</label>
              <input id="eColorHex" type="text" placeholder="#5865f2">
            </div>
          </div>

          <h3>Author</h3>
          <div class="grid3">
            <div><label for="eAuthorName">Name</label><input id="eAuthorName" type="text"></div>
            <div><label for="eAuthorUrl">URL</label><input id="eAuthorUrl" type="text"></div>
            <div><label for="eAuthorIconUrl">Icon URL</label><input id="eAuthorIconUrl" type="text"></div>
          </div>

          <h3>Title</h3>
          <div class="grid2">
            <div><label for="eTitle">Title</label><input id="eTitle" type="text"></div>
            <div><label for="eTitleUrl">URL</label><input id="eTitleUrl" type="text"></div>
          </div>

          <label for="eDescription">Description</label>
          <textarea id="eDescription"></textarea>

          <h3>Fields</h3>
          <div id="fields"></div>
          <button type="button" class="secondary small" id="addField">Add field</button>
          <div class="hint">Up to 25 fields.</div>

          <h3>Media</h3>
          <div class="grid2">
            <div><label for="eImageUrl">Image URL</label><input id="eImageUrl" type="text"></div>
            <div><label for="eThumbnailUrl">Thumbnail URL</label><input id="eThumbnailUrl" type="text"></div>
          </div>

          <h3>Footer</h3>
          <div class="grid2">
            <div><label for="eFooterText">Text</label><input id="eFooterText" type="text"></div>
            <div><label for="eFooterIconUrl">Icon URL</label><input id="eFooterIconUrl" type="text"></div>
          </div>

          <label class="row" style="margin-top:0.9rem;"><input id="eTimestamp" type="checkbox"> <span>Include timestamp</span></label>

          <button type="button" class="secondary" id="resetEvent">Reset this event to default</button>
        </div>

        <h3>Available variables</h3>
        <table id="varsTable">
          <thead><tr><th>Variable</th><th>Description</th></tr></thead>
          <tbody></tbody>
        </table>

        <div style="margin-top:1.5rem;">
          <button type="button" id="save">Save</button>
          <span id="status"></span>
        </div>

        <script>
        const EVENTS = ['success', 'failure', 'uptodate'];
        let activeEvent = 'success';
        const forms = { success: null, failure: null, uptodate: null };
        let defaults = { success: null, failure: null, uptodate: null };

        const $ = id => document.getElementById(id);

        function emptyForm() {
          return { color: null, authorName: '', authorUrl: '', authorIconUrl: '', title: '', titleUrl: '', description: '', fields: [], imageUrl: '', thumbnailUrl: '', footerText: '', footerIconUrl: '', timestamp: false };
        }

        function specToForm(spec) {
          if (!spec) return emptyForm();
          return {
            color: (spec.color === null || spec.color === undefined) ? null : spec.color,
            authorName: spec.authorName || '',
            authorUrl: spec.authorUrl || '',
            authorIconUrl: spec.authorIconUrl || '',
            title: spec.title || '',
            titleUrl: spec.titleUrl || '',
            description: spec.description || '',
            fields: (spec.fields || []).map(f => ({ name: f.name || '', value: f.value || '', inline: !!f.inline })),
            imageUrl: spec.imageUrl || '',
            thumbnailUrl: spec.thumbnailUrl || '',
            footerText: spec.footerText || '',
            footerIconUrl: spec.footerIconUrl || '',
            timestamp: !!spec.timestamp,
          };
        }

        function intToHex(n) {
          if (n === null || n === undefined) return '#000000';
          return '#' + (n & 0xffffff).toString(16).padStart(6, '0');
        }
        function hexToInt(hex) {
          const m = /^#?([0-9a-fA-F]{6})$/.exec((hex || '').trim());
          return m ? parseInt(m[1], 16) : null;
        }
        function nn(s) { return (s && s.trim() !== '') ? s : null; }

        function renderFieldRow(f) {
          const row = document.createElement('div');
          row.className = 'field-row';
          const name = document.createElement('input');
          name.type = 'text'; name.placeholder = 'name'; name.value = f.name || '';
          const value = document.createElement('input');
          value.type = 'text'; value.placeholder = 'value'; value.value = f.value || '';
          const inlineLabel = document.createElement('label');
          const inline = document.createElement('input');
          inline.type = 'checkbox'; inline.checked = !!f.inline;
          inlineLabel.appendChild(inline);
          inlineLabel.appendChild(document.createTextNode('inline'));
          const remove = document.createElement('button');
          remove.type = 'button'; remove.className = 'secondary small'; remove.textContent = 'x';
          remove.addEventListener('click', () => { row.remove(); });
          row.appendChild(name); row.appendChild(value); row.appendChild(inlineLabel); row.appendChild(remove);
          row._get = () => ({ name: name.value, value: value.value, inline: inline.checked });
          return row;
        }

        function readFieldsFromDom() {
          return Array.from($('fields').children).map(r => r._get());
        }

        function paintForm(form) {
          $('eColorHex').value = form.color === null ? '' : intToHex(form.color);
          $('eColor').value = form.color === null ? '#000000' : intToHex(form.color);
          $('eAuthorName').value = form.authorName;
          $('eAuthorUrl').value = form.authorUrl;
          $('eAuthorIconUrl').value = form.authorIconUrl;
          $('eTitle').value = form.title;
          $('eTitleUrl').value = form.titleUrl;
          $('eDescription').value = form.description;
          $('eImageUrl').value = form.imageUrl;
          $('eThumbnailUrl').value = form.thumbnailUrl;
          $('eFooterText').value = form.footerText;
          $('eFooterIconUrl').value = form.footerIconUrl;
          $('eTimestamp').checked = form.timestamp;
          const box = $('fields');
          box.innerHTML = '';
          form.fields.forEach(f => box.appendChild(renderFieldRow(f)));
        }

        function readForm() {
          const hex = $('eColorHex').value;
          return {
            color: hex.trim() === '' ? null : hexToInt(hex),
            authorName: $('eAuthorName').value,
            authorUrl: $('eAuthorUrl').value,
            authorIconUrl: $('eAuthorIconUrl').value,
            title: $('eTitle').value,
            titleUrl: $('eTitleUrl').value,
            description: $('eDescription').value,
            fields: readFieldsFromDom(),
            imageUrl: $('eImageUrl').value,
            thumbnailUrl: $('eThumbnailUrl').value,
            footerText: $('eFooterText').value,
            footerIconUrl: $('eFooterIconUrl').value,
            timestamp: $('eTimestamp').checked,
          };
        }

        function formToSpec(form) {
          const fields = form.fields
            .filter(f => (f.name && f.name.trim() !== '') || (f.value && f.value.trim() !== ''))
            .slice(0, 25)
            .map(f => ({ name: f.name || '', value: f.value || '', inline: !!f.inline }));
          return {
            color: form.color === null || form.color === undefined ? null : (form.color & 0xffffff),
            authorName: nn(form.authorName),
            authorUrl: nn(form.authorUrl),
            authorIconUrl: nn(form.authorIconUrl),
            title: nn(form.title),
            titleUrl: nn(form.titleUrl),
            description: nn(form.description),
            fields: fields,
            imageUrl: nn(form.imageUrl),
            thumbnailUrl: nn(form.thumbnailUrl),
            footerText: nn(form.footerText),
            footerIconUrl: nn(form.footerIconUrl),
            timestamp: !!form.timestamp,
          };
        }

        function stashActive() { forms[activeEvent] = readForm(); }

        function switchEvent(ev) {
          stashActive();
          activeEvent = ev;
          document.querySelectorAll('.tab').forEach(t => t.classList.toggle('active', t.dataset.event === ev));
          paintForm(forms[ev]);
        }

        function addField() {
          if ($('fields').children.length >= 25) return;
          $('fields').appendChild(renderFieldRow({ name: '', value: '', inline: false }));
        }

        function syncColor(from) {
          if (from === 'picker') {
            $('eColorHex').value = $('eColor').value;
          } else {
            const i = hexToInt($('eColorHex').value);
            if (i !== null) $('eColor').value = intToHex(i);
          }
        }

        async function load() {
          const r = await fetch('/admin/api/config');
          const cfg = await r.json();
          $('dashboard').value = cfg.dashboardChannelId || '';
          $('ghThread').value = cfg.githubFeedThreadId || '';
          $('dnThread').value = cfg.deployNotificationsThreadId || '';
          $('ghWebhook').value = cfg.githubWebhookUrl || '';

          defaults = { success: cfg.defaultSuccess, failure: cfg.defaultFailure, uptodate: cfg.defaultUptodate };
          const saved = { success: cfg.successEmbedJson, failure: cfg.failureEmbedJson, uptodate: cfg.uptodateEmbedJson };
          EVENTS.forEach(ev => {
            if (saved[ev]) {
              try { forms[ev] = specToForm(JSON.parse(saved[ev])); }
              catch (e) { forms[ev] = specToForm(defaults[ev]); }
            } else {
              forms[ev] = specToForm(defaults[ev]);
            }
          });

          const tbody = $('varsTable').querySelector('tbody');
          tbody.innerHTML = '';
          (cfg.variables || []).forEach(v => {
            const tr = document.createElement('tr');
            const td1 = document.createElement('td');
            const code = document.createElement('code');
            code.textContent = v.name;
            td1.appendChild(code);
            const td2 = document.createElement('td');
            td2.textContent = v.desc;
            tr.appendChild(td1); tr.appendChild(td2);
            tbody.appendChild(tr);
          });

          paintForm(forms[activeEvent]);
        }

        async function save() {
          stashActive();
          const body = {
            dashboardChannelId: nn($('dashboard').value),
            githubFeedThreadId: nn($('ghThread').value),
            deployNotificationsThreadId: nn($('dnThread').value),
            successEmbedJson: JSON.stringify(formToSpec(forms.success)),
            failureEmbedJson: JSON.stringify(formToSpec(forms.failure)),
            uptodateEmbedJson: JSON.stringify(formToSpec(forms.uptodate)),
          };
          const r = await fetch('/admin/api/config', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
          if (r.ok) {
            const cfg = await r.json();
            $('ghWebhook').value = cfg.githubWebhookUrl || '';
            $('status').textContent = 'Saved.';
          } else {
            const text = await r.text();
            $('status').textContent = text || 'Save failed.';
          }
        }

        document.querySelectorAll('.tab').forEach(t => t.addEventListener('click', () => switchEvent(t.dataset.event)));
        $('addField').addEventListener('click', addField);
        $('eColor').addEventListener('input', () => syncColor('picker'));
        $('eColorHex').addEventListener('input', () => syncColor('hex'));
        $('resetEvent').addEventListener('click', () => { forms[activeEvent] = specToForm(defaults[activeEvent]); paintForm(forms[activeEvent]); });
        $('save').addEventListener('click', save);
        $('ghCopy').addEventListener('click', () => {
          const v = $('ghWebhook').value;
          if (v) navigator.clipboard.writeText(v);
        });

        load();
        </script>
        </body>
        </html>
        """;
}
