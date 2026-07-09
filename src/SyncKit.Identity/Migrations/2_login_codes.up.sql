-- Single-use, short-lived codes handed to the browser after a popup-based login completes.
-- The browser only ever sees this opaque code (via postMessage); the host app's backend
-- redeems it server-side via POST /identity/redeem for the actual resolved identity.
CREATE TABLE IF NOT EXISTS login_codes (
    code       TEXT NOT NULL PRIMARY KEY,
    user_id    UUID NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    is_new     BOOLEAN NOT NULL DEFAULT false,
    expires_at TIMESTAMPTZ NOT NULL,
    redeemed_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS ix_login_codes_expires_at ON login_codes(expires_at);
