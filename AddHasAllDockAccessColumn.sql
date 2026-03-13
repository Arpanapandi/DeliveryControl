-- Add HasAllDockAccess column to Users table
-- Run this script on production database

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'HasAllDockAccess'
)
BEGIN
    ALTER TABLE Users ADD HasAllDockAccess BIT NOT NULL DEFAULT 0;
    PRINT 'Column HasAllDockAccess added to Users table';
END
ELSE
BEGIN
    PRINT 'Column HasAllDockAccess already exists';
END
GO
