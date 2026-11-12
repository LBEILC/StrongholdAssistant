ALTER TABLE posts ADD COLUMN comments INTEGER NOT NULL DEFAULT 0;
CREATE TABLE comments (
 id TEXT PRIMARY KEY, post_id TEXT NOT NULL REFERENCES posts(id) ON DELETE CASCADE,
 user_id TEXT NOT NULL REFERENCES users(id), body TEXT NOT NULL,
 reply_to TEXT, reply_nickname TEXT,
 status TEXT NOT NULL DEFAULT 'visible' CHECK(status IN ('visible','hidden','deleted')),
 created_at TEXT NOT NULL
);
CREATE INDEX comments_post_time ON comments(post_id, status, created_at DESC, id DESC);
CREATE INDEX comments_user_time ON comments(user_id, created_at DESC);
CREATE TRIGGER comment_added AFTER INSERT ON comments WHEN NEW.status='visible' BEGIN
 UPDATE posts SET comments=comments+1 WHERE id=NEW.post_id;
END;
CREATE TRIGGER comment_removed AFTER DELETE ON comments WHEN OLD.status='visible' BEGIN
 UPDATE posts SET comments=comments-1 WHERE id=OLD.post_id;
END;
CREATE TRIGGER comment_status_changed AFTER UPDATE OF status ON comments BEGIN
 UPDATE posts SET comments=comments+(NEW.status='visible')-(OLD.status='visible') WHERE id=NEW.post_id;
END;
CREATE TABLE reports (
 target_kind TEXT NOT NULL CHECK(target_kind IN ('post','comment')), target_id TEXT NOT NULL,
 user_id TEXT NOT NULL REFERENCES users(id), reason TEXT NOT NULL, detail TEXT NOT NULL DEFAULT '',
 created_at TEXT NOT NULL, resolved_at TEXT,
 PRIMARY KEY(target_kind, target_id, user_id)
);
CREATE TABLE report_usage (
 user_id TEXT NOT NULL REFERENCES users(id), day TEXT NOT NULL, count INTEGER NOT NULL,
 PRIMARY KEY(user_id, day)
);
CREATE TRIGGER report_added AFTER INSERT ON reports BEGIN
 INSERT INTO report_usage VALUES(NEW.user_id,date(NEW.created_at,'+8 hours'),1)
 ON CONFLICT(user_id,day) DO UPDATE SET count=count+1;
END;
CREATE TRIGGER report_updated AFTER UPDATE OF reason,detail,created_at ON reports BEGIN
 INSERT INTO report_usage VALUES(NEW.user_id,date(NEW.created_at,'+8 hours'),1)
 ON CONFLICT(user_id,day) DO UPDATE SET count=count+1;
END;
CREATE TRIGGER comment_reports_removed AFTER DELETE ON comments BEGIN
 DELETE FROM reports WHERE target_kind='comment' AND target_id=OLD.id;
END;
CREATE TRIGGER post_reports_removed AFTER DELETE ON posts BEGIN
 DELETE FROM reports WHERE target_kind='post' AND target_id=OLD.id;
END;
