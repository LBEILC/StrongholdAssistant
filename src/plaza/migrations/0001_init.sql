PRAGMA foreign_keys = ON;
CREATE TABLE users (
 id TEXT PRIMARY KEY, email TEXT UNIQUE, nickname TEXT,
 nickname_key TEXT UNIQUE, created_at TEXT NOT NULL,
 banned INTEGER NOT NULL DEFAULT 0, deleted INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE login_codes (
 id INTEGER PRIMARY KEY AUTOINCREMENT, email TEXT NOT NULL, ip TEXT NOT NULL,
 code_hash TEXT NOT NULL, created_at TEXT NOT NULL, expires_at TEXT NOT NULL, attempts INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX codes_email_time ON login_codes(email, created_at DESC);
CREATE INDEX codes_ip_time ON login_codes(ip, created_at);
CREATE TABLE sessions (
 token_hash TEXT PRIMARY KEY, user_id TEXT NOT NULL REFERENCES users(id),
 created_at TEXT NOT NULL, renewed_at TEXT NOT NULL, expires_at TEXT NOT NULL
);
CREATE INDEX sessions_user ON sessions(user_id);
CREATE TABLE posts (
 id TEXT PRIMARY KEY, user_id TEXT NOT NULL REFERENCES users(id), code TEXT NOT NULL,
 fingerprint INTEGER NOT NULL, lineup TEXT NOT NULL, description TEXT NOT NULL,
 status TEXT NOT NULL DEFAULT 'visible' CHECK(status IN ('visible','hidden','deleted')),
 featured INTEGER NOT NULL DEFAULT 0, likes INTEGER NOT NULL DEFAULT 0, imports INTEGER NOT NULL DEFAULT 0,
 created_at TEXT NOT NULL, updated_at TEXT NOT NULL, UNIQUE(user_id, code)
);
CREATE INDEX posts_current_new ON posts(status, fingerprint, created_at DESC, id DESC);
CREATE INDEX posts_current_hot ON posts(status, fingerprint, (likes * 2 + imports) DESC, created_at DESC, id DESC);
CREATE INDEX posts_current_featured ON posts(status, fingerprint, featured DESC, created_at DESC, id DESC);
CREATE INDEX posts_time ON posts(status, created_at DESC, id DESC);
CREATE INDEX posts_author ON posts(user_id, created_at DESC);
CREATE TABLE likes (
 post_id TEXT NOT NULL REFERENCES posts(id) ON DELETE CASCADE,
 user_id TEXT NOT NULL REFERENCES users(id), PRIMARY KEY(post_id, user_id)
);
CREATE INDEX likes_user ON likes(user_id);
CREATE TABLE imports (
 post_id TEXT NOT NULL REFERENCES posts(id) ON DELETE CASCADE,
 device TEXT NOT NULL, PRIMARY KEY(post_id, device)
);
CREATE TRIGGER like_added AFTER INSERT ON likes BEGIN
 UPDATE posts SET likes = likes + 1 WHERE id = NEW.post_id;
END;
CREATE TRIGGER like_removed AFTER DELETE ON likes BEGIN
 UPDATE posts SET likes = likes - 1 WHERE id = OLD.post_id;
END;
CREATE TRIGGER import_added AFTER INSERT ON imports BEGIN
 UPDATE posts SET imports = imports + 1 WHERE id = NEW.post_id;
END;
