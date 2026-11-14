CREATE TABLE ratings (
 season TEXT NOT NULL, kind TEXT NOT NULL CHECK(kind IN ('operator','equipment')), name TEXT NOT NULL,
 user_id TEXT NOT NULL REFERENCES users(id), id TEXT NOT NULL UNIQUE,
 score INTEGER NOT NULL CHECK(score BETWEEN 1 AND 5), comment TEXT NOT NULL DEFAULT '',
 comment_hidden INTEGER NOT NULL DEFAULT 0 CHECK(comment_hidden IN (0,1)),
 created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
 PRIMARY KEY(season,kind,name,user_id)
);
CREATE INDEX ratings_summary ON ratings(season,kind);
CREATE INDEX ratings_comments ON ratings(season,kind,name,updated_at DESC,id DESC);
CREATE TABLE rating_usage (ip_hash TEXT NOT NULL, created_at TEXT NOT NULL);
CREATE INDEX rating_usage_ip_time ON rating_usage(ip_hash,created_at);
DROP TRIGGER comment_reports_removed;
DROP TRIGGER post_reports_removed;
DROP TRIGGER report_added;
DROP TRIGGER report_updated;
CREATE TABLE reports_new (
 target_kind TEXT NOT NULL CHECK(target_kind IN ('post','comment','rating')), target_id TEXT NOT NULL,
 user_id TEXT NOT NULL REFERENCES users(id), reason TEXT NOT NULL, detail TEXT NOT NULL DEFAULT '',
 created_at TEXT NOT NULL, resolved_at TEXT,
 PRIMARY KEY(target_kind, target_id, user_id)
);
INSERT INTO reports_new SELECT * FROM reports;
DROP TABLE reports;
ALTER TABLE reports_new RENAME TO reports;
CREATE TRIGGER report_added AFTER INSERT ON reports BEGIN
 INSERT INTO report_usage VALUES(NEW.user_id,date(NEW.created_at,'+8 hours'),1)
 ON CONFLICT(user_id,day) DO UPDATE SET count=count+1;
END;
CREATE TRIGGER report_updated AFTER UPDATE OF reason,detail,created_at ON reports BEGIN
 INSERT INTO report_usage VALUES(NEW.user_id,date(NEW.created_at,'+8 hours'),1)
 ON CONFLICT(user_id,day) DO UPDATE SET count=count+1;
END;
CREATE TRIGGER rating_reports_removed AFTER DELETE ON ratings BEGIN
 DELETE FROM reports WHERE target_kind='rating' AND target_id=OLD.id;
END;
CREATE TRIGGER comment_reports_removed AFTER DELETE ON comments BEGIN
 DELETE FROM reports WHERE target_kind='comment' AND target_id=OLD.id;
END;
CREATE TRIGGER post_reports_removed AFTER DELETE ON posts BEGIN
 DELETE FROM reports WHERE target_kind='post' AND target_id=OLD.id;
END;