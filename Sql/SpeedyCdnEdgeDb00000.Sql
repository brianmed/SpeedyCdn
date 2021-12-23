CREATE TABLE IF NOT EXISTS ImageCacheElements
(
    ImageCacheElementId INTEGER PRIMARY KEY,

    CachePath TEXT NOT NULL,

    UrlPath TEXT NOT NULL,

    QueryString TEXT NOT NULL,

    FileSizeBytes INTEGER NOT NULL,

    LastAccessedUtc INTEGER NOT NULL,

    Updated DATETIME NOT NULL DEFAULT "0001-01-01 00:00",
    Inserted DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
) WITHOUT ROWID;

CREATE UNIQUE INDEX Idx_ImageCacheElement_Id ON ImageCacheElements (ImageCacheElementId);
CREATE UNIQUE INDEX Idx_ImageCacheElement_UrlPath ON ImageCacheElements (UrlPath, QueryString);
CREATE INDEX Idx_ImageCacheElement_LastAccessedUtc ON ImageCacheElements (LastAccessedUtc);
