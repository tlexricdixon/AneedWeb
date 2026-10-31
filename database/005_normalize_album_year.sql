-- One-time cleanup for the Azure SQL catalog.
-- Review and run manually against the catalog database.
-- Missing years must be NULL, not empty or whitespace-only strings.

BEGIN TRANSACTION;

UPDATE [dbo].[Songs]
SET [AlbumYear] = NULL
WHERE [AlbumYear] IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(nvarchar(32), [AlbumYear]))) = N'';

COMMIT TRANSACTION;
