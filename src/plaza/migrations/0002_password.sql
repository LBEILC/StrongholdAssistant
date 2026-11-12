ALTER TABLE users ADD COLUMN password_hash TEXT;
ALTER TABLE sessions ADD COLUMN login_method TEXT NOT NULL DEFAULT 'code' CHECK(login_method IN ('code','password'));
CREATE TABLE password_attempts (
 id INTEGER PRIMARY KEY AUTOINCREMENT, email TEXT NOT NULL, ip TEXT NOT NULL,
 created_at TEXT NOT NULL, success INTEGER NOT NULL CHECK(success IN (0,1))
);
CREATE INDEX password_email_time ON password_attempts(email, created_at DESC);
CREATE INDEX password_ip_time ON password_attempts(ip, created_at);
