CREATE UNIQUE INDEX Idx_StaticCacheElement_CachePath ON StaticCacheElements (CachePath);
CREATE INDEX Idx_StaticCacheElement_LastAccessedUtc ON StaticCacheElements (LastAccessedUtc);
