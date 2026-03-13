-- =====================================================================
-- Migration: AddLeaderVerificationFields
-- Tanggal: 2026-02-26
-- Deskripsi: Tambah kolom Leader Verification ke tabel DeliverySchedules
-- =====================================================================

-- SQLite syntax (Development)
ALTER TABLE DeliverySchedules ADD COLUMN IsLeaderVerified INTEGER NOT NULL DEFAULT 0;
ALTER TABLE DeliverySchedules ADD COLUMN LeaderVerifiedAt TEXT NULL;
ALTER TABLE DeliverySchedules ADD COLUMN LeaderVerifiedBy TEXT NULL;

-- =====================================================================
-- SQL Server syntax (Production) - uncomment jika pakai SQL Server
-- =====================================================================
-- ALTER TABLE DeliverySchedules ADD IsLeaderVerified BIT NOT NULL DEFAULT 0;
-- ALTER TABLE DeliverySchedules ADD LeaderVerifiedAt DATETIME NULL;
-- ALTER TABLE DeliverySchedules ADD LeaderVerifiedBy NVARCHAR(100) NULL;
