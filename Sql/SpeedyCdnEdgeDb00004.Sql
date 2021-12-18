CREATE TABLE IF NOT EXISTS BarcodeCacheElements
(
    BarcodeCacheElementId INTEGER PRIMARY KEY AUTOINCREMENT,

    CachePath TEXT,

    FileSizeBytes INTEGER NOT NULL,

    LastAccessedUtc INTEGER NOT NULL,

    Updated DATETIME NOT NULL DEFAULT "0001-01-01 00:00",
    Inserted DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX Idx_BarcodeCacheElement_LastAccessedUtc ON BarcodeCacheElements (LastAccessedUtc);
