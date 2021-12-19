CREATE UNIQUE INDEX Idx_ImageCacheElement_UrlPath ON ImageCacheElements (UrlPath, QueryString);
CREATE INDEX Idx_ImageCacheElement_LastAccessedUtc ON ImageCacheElements (LastAccessedUtc);
