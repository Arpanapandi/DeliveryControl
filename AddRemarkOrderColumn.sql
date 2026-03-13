-- Add RemarkOrder column to Customers table
-- Run this script on the SQLite database if EF migration is not used

-- Check if column already exists, if not add it
-- SQLite does not support IF NOT EXISTS for ALTER TABLE directly,
-- so we use a try-catch approach or just run it once.

ALTER TABLE "Customers" ADD COLUMN "RemarkOrder" TEXT NULL;
