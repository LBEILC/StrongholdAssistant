CREATE TABLE notices (
 id TEXT PRIMARY KEY, user_id TEXT NOT NULL REFERENCES users(id),
 kind TEXT NOT NULL, post_id TEXT NOT NULL, post_name TEXT NOT NULL,
 comment_id TEXT, comment_body TEXT, created_at TEXT NOT NULL, read INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX notices_user_time ON notices(user_id, created_at DESC, id DESC);
CREATE INDEX notices_user_read_time ON notices(user_id, read, created_at);
CREATE INDEX notices_time ON notices(created_at);
CREATE INDEX notices_post ON notices(post_id);
CREATE INDEX notices_comment ON notices(comment_id);
CREATE UNIQUE INDEX notices_unread_like ON notices(user_id, post_id) WHERE kind='like' AND read=0;
CREATE TABLE notice_actors (
 sequence INTEGER PRIMARY KEY AUTOINCREMENT,
 notice_id TEXT NOT NULL REFERENCES notices(id) ON DELETE CASCADE,
 user_id TEXT NOT NULL REFERENCES users(id), created_at TEXT NOT NULL,
 UNIQUE(notice_id,user_id)
);
CREATE INDEX notice_actors_user ON notice_actors(user_id);
CREATE INDEX notice_actors_time ON notice_actors(created_at);
CREATE TRIGGER notice_actor_added AFTER INSERT ON notice_actors BEGIN
 UPDATE notices SET created_at=NEW.created_at WHERE id=NEW.notice_id;
END;
CREATE TRIGGER notice_actor_removed AFTER DELETE ON notice_actors BEGIN
 DELETE FROM notices WHERE id=OLD.notice_id AND NOT EXISTS(SELECT 1 FROM notice_actors WHERE notice_id=OLD.notice_id);
 UPDATE notices SET created_at=(SELECT MAX(created_at) FROM notice_actors WHERE notice_id=OLD.notice_id) WHERE id=OLD.notice_id;
END;
CREATE TRIGGER notice_comment_added AFTER INSERT ON comments WHEN NEW.status='visible' BEGIN
 INSERT INTO notices(id,user_id,kind,post_id,post_name,comment_id,comment_body,created_at)
 SELECT lower(hex(randomblob(16))),p.user_id,'comment',p.id,json_extract(p.lineup,'$.name'),NEW.id,NEW.body,NEW.created_at
 FROM posts p WHERE p.id=NEW.post_id AND p.user_id<>NEW.user_id
 AND NOT EXISTS(SELECT 1 FROM comments c WHERE c.id=NEW.reply_to AND c.user_id=p.user_id);
 INSERT INTO notices(id,user_id,kind,post_id,post_name,comment_id,comment_body,created_at)
 SELECT lower(hex(randomblob(16))),c.user_id,'reply',p.id,json_extract(p.lineup,'$.name'),NEW.id,NEW.body,NEW.created_at
 FROM comments c JOIN posts p ON p.id=NEW.post_id WHERE c.id=NEW.reply_to AND c.user_id<>NEW.user_id;
 INSERT INTO notice_actors(notice_id,user_id,created_at)
 SELECT id,NEW.user_id,NEW.created_at FROM notices WHERE comment_id=NEW.id AND kind IN ('comment','reply');
END;
CREATE TRIGGER notice_favorite_added AFTER INSERT ON favorites BEGIN
 INSERT INTO notices(id,user_id,kind,post_id,post_name,created_at)
 SELECT lower(hex(randomblob(16))),user_id,'favorite',id,json_extract(lineup,'$.name'),NEW.created_at
 FROM posts WHERE id=NEW.post_id AND user_id<>NEW.user_id;
 INSERT INTO notice_actors(notice_id,user_id,created_at)
 SELECT id,NEW.user_id,NEW.created_at FROM notices WHERE post_id=NEW.post_id AND kind='favorite' AND read=0
 AND NOT EXISTS(SELECT 1 FROM notice_actors WHERE notice_id=notices.id);
END;
CREATE TRIGGER notice_like_added AFTER INSERT ON likes BEGIN
 INSERT OR IGNORE INTO notices(id,user_id,kind,post_id,post_name,created_at)
 SELECT lower(hex(randomblob(16))),user_id,'like',id,json_extract(lineup,'$.name'),strftime('%Y-%m-%dT%H:%M:%fZ','now')
 FROM posts WHERE id=NEW.post_id AND user_id<>NEW.user_id;
 INSERT INTO notice_actors(notice_id,user_id,created_at)
 SELECT id,NEW.user_id,strftime('%Y-%m-%dT%H:%M:%fZ','now') FROM notices
 WHERE post_id=NEW.post_id AND kind='like' AND read=0 AND user_id<>NEW.user_id;
END;
CREATE TRIGGER notice_favorite_removed AFTER DELETE ON favorites BEGIN
 DELETE FROM notice_actors WHERE user_id=OLD.user_id AND notice_id IN
 (SELECT id FROM notices WHERE post_id=OLD.post_id AND kind='favorite' AND read=0);
END;
CREATE TRIGGER notice_like_removed AFTER DELETE ON likes BEGIN
 DELETE FROM notice_actors WHERE user_id=OLD.user_id AND notice_id IN
 (SELECT id FROM notices WHERE post_id=OLD.post_id AND kind='like' AND read=0);
END;
CREATE TRIGGER notice_comment_hidden AFTER UPDATE OF status ON comments WHEN NEW.status<>'visible' BEGIN
 DELETE FROM notices WHERE comment_id=NEW.id AND kind IN ('comment','reply');
END;
CREATE TRIGGER notice_comment_deleted AFTER DELETE ON comments BEGIN
 DELETE FROM notices WHERE comment_id=OLD.id AND kind IN ('comment','reply');
END;
CREATE TRIGGER notice_post_deleted AFTER UPDATE OF status ON posts WHEN NEW.status='deleted' BEGIN
 DELETE FROM notices WHERE post_id=NEW.id AND kind<>'post_hidden';
END;
CREATE TRIGGER notice_post_removed AFTER DELETE ON posts BEGIN
 DELETE FROM notices WHERE post_id=OLD.id AND kind<>'post_hidden';
END;
CREATE TRIGGER notice_user_disabled AFTER UPDATE OF banned,deleted ON users WHEN NEW.banned=1 OR NEW.deleted=1 BEGIN
 DELETE FROM notice_actors WHERE user_id=NEW.id;
END;
