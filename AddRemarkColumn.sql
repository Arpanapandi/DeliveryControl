-- Add Remark column to PreparationRecords and PullingRecords
-- Match = normal transaction (affects stock), Mismatch = log only (no stock impact)

-- For SQLite
ALTER TABLE PreparationRecords ADD COLUMN Remark TEXT DEFAULT 'Match';
ALTER TABLE PullingRecords ADD COLUMN Remark TEXT DEFAULT 'Match';

-- For SQL Server (uncomment if using SQL Server)
-- ALTER TABLE PreparationRecords ADD Remark NVARCHAR(20) NOT NULL DEFAULT 'Match';
-- ALTER TABLE PullingRecords ADD Remark NVARCHAR(20) NOT NULL DEFAULT 'Match';
