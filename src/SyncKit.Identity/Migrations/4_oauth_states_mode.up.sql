-- Whether /login/callback should postMessage to window.opener (popup) or window.parent (iframe).
ALTER TABLE oauth_states ADD COLUMN IF NOT EXISTS mode TEXT NOT NULL DEFAULT 'popup';
