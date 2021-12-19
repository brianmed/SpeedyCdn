CREATE UNIQUE INDEX Idx_StaticCacheElement_UrlPath ON StaticCacheElements (UrlPath, QueryString);
CREATE INDEX Idx_StaticCacheElement_LastAccessedUtc ON StaticCacheElements (LastAccessedUtc);
