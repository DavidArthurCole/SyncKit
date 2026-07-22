using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using SyncKit.Contract;

namespace SyncKit.Identity.AdminUi;

public sealed record ProfileAvatarContext(string? AvatarUrl, string? Error, EventCallback<InputFileChangeEventArgs> OnFileSelected);

public sealed record ProfileIdentityItem(ProfileIdentityResponse Identity, bool CanUnlink, EventCallback OnSelectAvatar, EventCallback OnUnlink);

public sealed record ProfileIdentitiesContext(IReadOnlyList<ProfileIdentityItem> Items, string? Banner);

public sealed record ProfileLinkItem(string Provider, bool Linked, string LinkUrl);

public sealed record ProfileLinkRowContext(IReadOnlyList<ProfileLinkItem> Items);

public sealed record ProfileProviderItem(
    string Provider,
    bool Linked,
    string LinkUrl,
    ProfileIdentityResponse? Identity,
    bool CanUnlink,
    EventCallback OnSelectAvatar,
    EventCallback OnUnlink);

public sealed record ProfileProvidersContext(IReadOnlyList<ProfileProviderItem> Items, string? Banner);
