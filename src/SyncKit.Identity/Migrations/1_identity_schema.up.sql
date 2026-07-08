CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS users (
    user_id       UUID NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    discord_id    TEXT,
    username      TEXT NOT NULL DEFAULT '',
    avatar        TEXT,
    role          TEXT NOT NULL DEFAULT 'viewer',
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_login_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_users_discord_id ON users(discord_id) WHERE discord_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS identities (
    user_id   UUID NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    provider  TEXT NOT NULL,
    subject   TEXT NOT NULL,
    linked_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (provider, subject)
);

CREATE INDEX IF NOT EXISTS ix_identities_user_id ON identities(user_id);

CREATE TABLE IF NOT EXISTS revoked_sessions (
    sid        TEXT NOT NULL PRIMARY KEY,
    revoked_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
