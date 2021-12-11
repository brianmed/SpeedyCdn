CREATE UNIQUE INDEX Idx_ImageCacheElement_CachePath ON ImageCacheElements (CachePath);
CREATE INDEX Idx_ImageCacheElement_LastAccessedUtc ON ImageCacheElements (LastAccessedUtc);
