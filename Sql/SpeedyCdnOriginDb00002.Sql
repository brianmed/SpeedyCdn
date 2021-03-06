CREATE TABLE IF NOT EXISTS DisplayUrl
(
    DisplayUrlId INTEGER PRIMARY KEY AUTOINCREMENT,

    Display TEXT NOT NULL,

    RedirectPath TEXT NOT NULL,

    QueryString TEXT,

    Updated DATETIME NOT NULL DEFAULT "0001-01-01 00:00",
    Inserted DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX Idx_DisplayUrl_Display ON DisplayUrl (Display);
