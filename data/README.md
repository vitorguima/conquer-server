# Data Files

## DMAP Map Files

Place `.dmap` passability map files in `data/maps/`.

These files are **not included in the repository** (gitignored) because they are
extracted from the Conquer Online 5017 client or sourced from community archives.

### Sourcing DMAP files

1. **From the client**: Extract from the CO 5017 client installation directory under
   `map/` — files are named by map ID, e.g., `1002.dmap` (Twin City).
2. **Community archives**: Several open-source CO emulator projects (e.g., COServer,
   Redux) distribute map packs. Search GitHub for `conquer online dmap`.

### Minimum required for M1

- `1002.dmap` — Twin City (default spawn map)

### Directory structure

```
data/
  maps/
    1002.dmap    <- Twin City (required)
    ...          <- additional maps
```

## Community SQL Dump

A MySQL dump containing base game data (maps, NPC positions, monster spawns) is
available from community CO emulator projects. For M1 (auth + movement only),
only the `accounts` and `characters` tables are needed — these are created
automatically via inline migrations at server startup (`CREATE TABLE IF NOT EXISTS`).

Test accounts are seeded via `docker/init/01-schema.sql` when using Docker Compose.
