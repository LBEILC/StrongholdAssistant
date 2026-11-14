CREATE TABLE traffic_days (
 date TEXT NOT NULL, host TEXT NOT NULL,
 requests INTEGER NOT NULL, bytes INTEGER NOT NULL, visitors INTEGER NOT NULL,
 cacheHits INTEGER NOT NULL, cacheMisses INTEGER NOT NULL,
 status4xx INTEGER NOT NULL, status5xx INTEGER NOT NULL,
 PRIMARY KEY (date, host)
);
CREATE TABLE traffic_paths (
 date TEXT NOT NULL, host TEXT NOT NULL, path TEXT NOT NULL,
 requests INTEGER NOT NULL, bytes INTEGER NOT NULL,
 PRIMARY KEY (date, host, path)
);
CREATE TABLE traffic_state (id INTEGER PRIMARY KEY CHECK (id = 1), lastReportAt TEXT NOT NULL);
