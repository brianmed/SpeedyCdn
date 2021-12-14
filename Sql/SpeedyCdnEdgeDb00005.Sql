CREATE TABLE IF NOT EXISTS S3ImageCacheElements
(
    S3ImageCacheElementId INTEGER PRIMARY KEY AUTOINCREMENT,

    CachePath TEXT NOT NULL,

    FileSizeBytes INTEGER NOT NULL,

    ExpireUtc INTEGER NOT NULL,

    LastAccessedUtc INTEGER NOT NULL,

    Updated DATETIME NOT NULL DEFAULT "0001-01-01 00:00",
    Inserted DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX Idx_S3ImageCacheElement_CachePath ON S3ImageCacheElements (CachePath);
CREATE INDEX Idx_S3ImageCacheElement_LastAccessedUtc ON S3ImageCacheElements (LastAccessedUtc);
