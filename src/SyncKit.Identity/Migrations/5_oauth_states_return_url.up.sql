-- return_origin only ever stored an origin; now stores the full URL the user was on, so
-- post-login redirect lands on the exact page, not just the app root.
ALTER TABLE oauth_states RENAME COLUMN return_origin TO return_url;
