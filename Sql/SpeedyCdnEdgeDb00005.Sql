CREATE TABLE IF NOT EXISTS S3ImageCacheElements
(
    S3ImageCacheElementId INTEGER PRIMARY KEY AUTOINCREMENT,

    CachePath TEXT,

    FileSizeBytes INTEGER NOT NULL,

    ExpireUtc INTEGER NOT NULL,

    LastAccessedUtc INTEGER NOT NULL,

    Updated DATETIME NOT NULL DEFAULT "0001-01-01 00:00",
    Inserted DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX Idx_S3ImageCacheElement_ExpireUtc ON S3ImageCacheElements (ExpireUtc);
CREATE INDEX Idx_S3ImageCacheElement_LastAccessedUtc ON S3ImageCacheElements (LastAccessedUtc);
