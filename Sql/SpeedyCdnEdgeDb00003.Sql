CREATE TABLE IF NOT EXISTS S3ImageCacheElements
(
    S3ImageCacheElementId INTEGER PRIMARY KEY,

    CachePath TEXT NOT NULL,

    UrlPath TEXT NOT NULL,

    QueryString TEXT NOT NULL,

    FileSizeBytes INTEGER NOT NULL,

    LastAccessedUtc INTEGER NOT NULL,

    Updated DATETIME NOT NULL DEFAULT "0001-01-01 00:00",
    Inserted DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
) WITHOUT ROWID;

CREATE UNIQUE INDEX Idx_S3ImageCacheElement_Id ON S3ImageCacheElements (S3ImageCacheElementId);
CREATE UNIQUE INDEX Idx_S3ImageCacheElement_UrlPath ON S3ImageCacheElements (UrlPath, QueryString);
CREATE INDEX Idx_S3ImageCacheElement_LastAccessedUtc ON S3ImageCacheElements (LastAccessedUtc);
