-- Publish-only public editorial data. Run manually against Monster AneedCatalog AFTER backup.
-- No changes to SAM MariaDB, dbo.Songs, dbo.SongIdentity, or existing catalog rows.
IF OBJECT_ID(N'dbo.SongAnalysisPublished', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SongAnalysisPublished (
        AneedId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_SongAnalysisPublished PRIMARY KEY,
        SamSongId BIGINT NOT NULL,
        Summary NVARCHAR(2000) NOT NULL,
        ThemesJson NVARCHAR(3000) NULL,
        EmotionsJson NVARCHAR(3000) NULL,
        EmotionalArc NVARCHAR(2000) NULL,
        AnalysisVersion INT NOT NULL,
        PublishedAt DATETIME2(3) NOT NULL CONSTRAINT DF_SongAnalysisPublished_PublishedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_SongAnalysisPublished_SamSongId UNIQUE (SamSongId),
        CONSTRAINT FK_SongAnalysisPublished_Identity FOREIGN KEY (SamSongId)
            REFERENCES dbo.SongIdentity(SamSongId),
        CONSTRAINT CK_SongAnalysisPublished_Summary CHECK (LEN(LTRIM(RTRIM(Summary))) > 0)
    );
END;
GO
-- Make the read endpoint's permission explicit for the actual website SQL login.
-- GRANT SELECT ON dbo.SongAnalysisPublished TO [YourWebsiteUser];
-- Create a separate publisher account and grant SELECT on dbo.SongIdentity and
-- SELECT/INSERT/UPDATE on dbo.SongAnalysisPublished ONLY.
-- Do not grant table DELETE or broad db_owner rights to the publishing account.
