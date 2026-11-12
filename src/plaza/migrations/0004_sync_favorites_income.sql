CREATE TABLE favorites (
 user_id TEXT NOT NULL REFERENCES users(id), post_id TEXT NOT NULL REFERENCES posts(id) ON DELETE CASCADE,
 created_at TEXT NOT NULL, PRIMARY KEY(user_id, post_id)
);
CREATE INDEX favorites_time ON favorites(user_id, created_at DESC, post_id DESC);
CREATE TABLE cloud_lineups (
 user_id TEXT NOT NULL REFERENCES users(id), id TEXT NOT NULL, data TEXT,
 updated_at TEXT NOT NULL, deleted INTEGER NOT NULL, changed_at TEXT NOT NULL,
 PRIMARY KEY(user_id, id)
);
CREATE INDEX cloud_lineups_changes ON cloud_lineups(user_id, changed_at);
CREATE TRIGGER cloud_lineups_insert_limit BEFORE INSERT ON cloud_lineups
 WHEN NEW.deleted=0 AND NOT EXISTS(SELECT 1 FROM cloud_lineups WHERE user_id=NEW.user_id AND id=NEW.id AND deleted=0)
 AND (SELECT COUNT(*) FROM cloud_lineups WHERE user_id=NEW.user_id AND deleted=0)>=300
 BEGIN SELECT RAISE(ABORT, 'cloud_too_many'); END;
CREATE TRIGGER cloud_lineups_update_limit BEFORE UPDATE ON cloud_lineups
 WHEN OLD.deleted=1 AND NEW.deleted=0
 AND (SELECT COUNT(*) FROM cloud_lineups WHERE user_id=NEW.user_id AND deleted=0)>=300
 BEGIN SELECT RAISE(ABORT, 'cloud_too_many'); END;
CREATE TABLE cloud_prefs (
 user_id TEXT PRIMARY KEY REFERENCES users(id), data TEXT NOT NULL,
 updated_at TEXT NOT NULL, changed_at TEXT NOT NULL
);
CREATE TABLE sync_usage (user_id TEXT NOT NULL REFERENCES users(id), created_at TEXT NOT NULL);
CREATE INDEX sync_usage_time ON sync_usage(user_id, created_at);
CREATE TABLE income_reports (
 id TEXT PRIMARY KEY, device TEXT NOT NULL, ip TEXT NOT NULL, created_at TEXT NOT NULL
);
CREATE INDEX income_device_time ON income_reports(device, created_at);
CREATE INDEX income_ip_time ON income_reports(ip, created_at);
CREATE TABLE income_samples (
 device TEXT NOT NULL, mode TEXT NOT NULL, solo INTEGER NOT NULL, round INTEGER NOT NULL,
 start_money INTEGER NOT NULL, fingerprint INTEGER NOT NULL, strategy_id TEXT, created_at TEXT NOT NULL
);
CREATE INDEX income_group ON income_samples(fingerprint, mode, solo, round, start_money, device);
CREATE TABLE income_summary (fingerprint INTEGER PRIMARY KEY, data TEXT NOT NULL, updated_at TEXT NOT NULL);
