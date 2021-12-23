CREATE TABLE IF NOT EXISTS StaticCacheElements
(
    StaticCacheElementId INTEGER PRIMARY KEY,

    CachePath TEXT NOT NULL,

    UrlPath TEXT NOT NULL,

    QueryString TEXT NOT NULL,

    FileSizeBytes INTEGER NOT NULL,

    LastAccessedUtc INTEGER NOT NULL,

    Updated DATETIME NOT NULL DEFAULT "0001-01-01 00:00",
    Inserted DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
) WITHOUT ROWID;

CREATE UNIQUE INDEX Idx_StaticCacheElement_Id ON StaticCacheElements (StaticCacheElementId);
CREATE UNIQUE INDEX Idx_StaticCacheElement_UrlPath ON StaticCacheElements (UrlPath, QueryString);
CREATE INDEX Idx_StaticCacheElement_LastAccessedUtc ON StaticCacheElements (LastAccessedUtc);
