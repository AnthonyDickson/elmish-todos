-- Note that while `GUID`, `BOOLEAN` and `DATETIME` are not real SQLite data types,
-- they act as hints to SqlHydra for codegen, see
-- https://github.com/JordanMarr/SqlHydra/blob/main/src/SqlHydra.Cli/Sqlite/SqliteDataTypes.fs
-- for the full list of supported types.
CREATE TABLE [Todos] (
    Id        GUID NOT NULL PRIMARY KEY,
    Title     TEXT NOT NULL,
    Completed BOOLEAN NOT NULL DEFAULT 0,
    CreatedAt INTEGER NOT NULL
);
