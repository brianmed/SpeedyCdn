CREATE TABLE IF NOT EXISTS BarcodeCacheElements
(
    BarcodeCacheElementId INTEGER PRIMARY KEY,

    CachePath TEXT NOT NULL,

    UrlPath TEXT NOT NULL,

    QueryString TEXT NOT NULL,

    FileSizeBytes INTEGER NOT NULL,

    LastAccessedUtc INTEGER NOT NULL,

    Updated DATETIME NOT NULL DEFAULT "0001-01-01 00:00",
    Inserted DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
) WITHOUT ROWID;

CREATE UNIQUE INDEX Idx_BarcodeCacheElement_Id ON BarcodeCacheElements (BarcodeCacheElementId);
CREATE UNIQUE INDEX Idx_BarcodeCacheElement_UrlPath ON BarcodeCacheElements (UrlPath, QueryString);
CREATE INDEX Idx_BarcodeCacheElement_LastAccessedUtc ON BarcodeCacheElements (LastAccessedUtc);
