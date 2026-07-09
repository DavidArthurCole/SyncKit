-- Server-side mapping for the /login/start -> /login/callback hop. The PKCE code_verifier and
-- the host app's origin (for the postMessage target) can't be round-tripped through the browser
-- as query params without exposing the verifier, so they're stored here keyed by the OIDC
-- state value and looked up when Authentik redirects back.
CREATE TABLE IF NOT EXISTS oauth_states (
    state         TEXT NOT NULL PRIMARY KEY,
    code_verifier TEXT NOT NULL,
    return_origin TEXT NOT NULL,
    expires_at    TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_oauth_states_expires_at ON oauth_states(expires_at);
