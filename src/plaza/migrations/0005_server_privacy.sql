ALTER TABLE income_samples ADD COLUMN source TEXT;
ALTER TABLE imports ADD COLUMN source TEXT;
CREATE UNIQUE INDEX imports_source ON imports(post_id, source);
DELETE FROM income_summary;
DELETE FROM income_reports;
