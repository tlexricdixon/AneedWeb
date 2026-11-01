-- Run in Azure SQL database AneedCatalog, not SAM MariaDB.
IF OBJECT_ID(N'dbo.SongIdentity',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SongIdentity (
        SamSongId BIGINT NOT NULL PRIMARY KEY,
        AneedId UNIQUEIDENTIFIER NOT NULL UNIQUE,
        UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;
GO
IF OBJECT_ID(N'dbo.ArtistImages',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ArtistImages (
        Artist NVARCHAR(512) NOT NULL PRIMARY KEY,
        ImageUrl NVARCHAR(1000) NOT NULL,
        Credit NVARCHAR(500) NULL,
        SourceUrl NVARCHAR(1000) NULL,
        License NVARCHAR(100) NULL,
        UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;
GO
