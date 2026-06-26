# DDL Audit: Redux SQL Dump (Nov_16_Backup.sql)

Source: `src/Nov_16_Backup.sql`
MySQL dump from 2014-11-16, Source Server Version: 50621 (MySQL 5.6.21)

---

## Password Storage Format

**Plain text. No hashing.**

The `accounts.Password` column is `varchar(16)` and the sample data confirms cleartext storage:

```
('1000020', 'test',        'test',        ...)
('1000024', 'Daniel1',     '1234567890',  ...)
('1000034', 'huntercps',   'thekinghere', ...)
```

There is no SHA1, MD5, or any hash in the DDL or data. The column is only 16 characters wide — too short for any standard hash (SHA1 hex = 40 chars, SHA1 base64 = 28 chars).

**Implication for task 2.4:** `ValidateSha1()` is the wrong function name. Auth comparison is a direct string equality check: `inputPassword == storedPassword`. If we want to add hashing for the modernized version, we must also migrate existing rows or seed a fresh DB.

---

## Accounts Table Schema

```sql
CREATE TABLE `accounts` (
  `UID`         int(11)     NOT NULL AUTO_INCREMENT,
  `Username`    varchar(16) NOT NULL,
  `Password`    varchar(16) NOT NULL,
  `EMail`       varchar(64) DEFAULT NULL,
  `EmailStatus` int(3)      NOT NULL DEFAULT '0',
  `Question`    varchar(32) DEFAULT NULL,
  `Answer`      varchar(32) DEFAULT NULL,
  `Permission`  int(3)      NOT NULL DEFAULT '1',
  `Token`       int(11)     NOT NULL DEFAULT '0',
  `Timestamp`   int(10)     NOT NULL DEFAULT '0',
  PRIMARY KEY (`UID`,`Username`)
) ENGINE=InnoDB AUTO_INCREMENT=1000036 DEFAULT CHARSET=latin1;
```

Notes:
- `Permission` = 5 for all sample accounts (GM-level flag)
- `Token` is an int (not the ulong session token used by the server — likely a legacy field)
- Composite PK on (`UID`, `Username`) — unusual; `UID` alone would suffice
- `int(M)` display widths (e.g. `int(3)`, `int(11)`) are deprecated in MySQL 8.0.17 and removed in MySQL 8.0.20+ — harmless but will produce warnings on import

---

## Characters Table Schema

```sql
CREATE TABLE `characters` (
  `UID`                int(8)    NOT NULL,
  `Name`               varchar(16) NOT NULL,
  `Spouse`             varchar(16) NOT NULL DEFAULT 'None',
  `Lookface`           int(11)   NOT NULL,
  `Hair`               int(3)    NOT NULL DEFAULT '0',
  `Level`              int(4)    NOT NULL DEFAULT '1',
  `Money`              int(11)   NOT NULL DEFAULT '0',
  `WhMoney`            int(11)   NOT NULL DEFAULT '0',
  `CP`                 int(11)   NOT NULL DEFAULT '0',
  `Experience`         int(22)   NOT NULL DEFAULT '0',
  `Strength`           int(4)    NOT NULL DEFAULT '0',
  `Agility`            int(4)    NOT NULL DEFAULT '0',
  `Spirit`             int(4)    NOT NULL DEFAULT '0',
  `Vitality`           int(4)    NOT NULL DEFAULT '0',
  `ExtraStats`         int(4)    NOT NULL DEFAULT '0',
  `Life`               int(6)    NOT NULL DEFAULT '0',
  `Mana`               int(6)    NOT NULL DEFAULT '0',
  `Map`                int(6)    NOT NULL DEFAULT '1010',
  `X`                  int(4)    NOT NULL DEFAULT '89',
  `Y`                  int(4)    NOT NULL DEFAULT '38',
  `Pk`                 int(4)    NOT NULL DEFAULT '0',
  `Profession`         int(4)    NOT NULL DEFAULT '0',
  `Profession1`        int(4)    NOT NULL DEFAULT '0',
  `Profession2`        int(4)    NOT NULL DEFAULT '0',
  `Profession3`        int(4)    NOT NULL DEFAULT '0',
  `QuizPoints`         int(6)    NOT NULL DEFAULT '0',
  `VirtuePoints`       int(5)    NOT NULL DEFAULT '0',
  `Online`             tinyint(1) NOT NULL DEFAULT '0',
  `HeavenBlessExpires` datetime  NOT NULL DEFAULT '2011-01-25 11:36:12',
  `DoubleExpExpires`   datetime  DEFAULT '2011-01-25 11:36:12',
  `TrainingTime`       int(3)    NOT NULL,
  `OfflineTGEntered`   datetime  NOT NULL,
  `LuckyTimeRemaining` int(11)   NOT NULL DEFAULT '0',
  PRIMARY KEY (`UID`,`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;
```

Notes:
- `Experience int(22)` — `int` is always 4 bytes regardless of display width; sample data has values up to ~7.5M which fits int32. However, high-level characters could need bigint — this is a latent overflow risk.
- Static `datetime` defaults (`'2011-01-25 11:36:12'`) — MySQL 8 strict mode may reject non-zero static datetime defaults on columns without `DEFAULT CURRENT_TIMESTAMP`. Needs testing.
- Composite PK on (`UID`, `Name`).

---

## MySQL 8 Compatibility Issues

| Issue | Table / Column(s) | Severity | Fix Needed |
|-------|-------------------|----------|------------|
| `ZEROFILL` attribute | `guild` (id, leader_id, fealty_syn, del_flag, amount, enemy0-4, ally0-4), `magictype` (all int columns), `monstertype` (all int columns), `passages` (UID, EnterMap, EnterID, ExitMap, ExitID), `portals` (UID, MapID, PortalID, PortalX, PortalY) | **ERROR** (MySQL 8.0.17+ deprecated; removed in 8.0.20+) | Remove `zerofill`; add leading-zero formatting in app code if needed |
| `DOUBLE(M,D)` precision syntax | `drop_rules.RuleChance double(4,2)` | **ERROR** (deprecated 8.0.17, removed 8.0.20+) | Change to `DOUBLE` or `DECIMAL(4,2)` |
| `ENGINE=MyISAM` | `guild`, `guildattr`, `levexp`, `magictype`, `monstertype`, `passages`, `portals` | **WARNING** (MyISAM supported but lacks FK support, no transactions) | Change to `ENGINE=InnoDB` |
| `int(M)` display-width syntax | All tables (pervasive — `int(3)`, `int(4)`, `int(8)`, `int(11)`, `int(22)`, etc.) | **WARNING** (deprecated 8.0.17, ignored in 8.0.20+) | Remove display widths: `int(11)` → `int`, `tinyint(1)` → `tinyint(1)` (keep for boolean semantics) |
| `ROW_FORMAT=DYNAMIC` / `ROW_FORMAT=FIXED` on MyISAM tables | `guild` (DYNAMIC), `guildattr` (FIXED), `levexp` (FIXED) | **WARNING** (invalid on InnoDB after engine change) | Remove or omit `ROW_FORMAT` after converting to InnoDB |
| Static non-`CURRENT_TIMESTAMP` datetime defaults | `characters.HeavenBlessExpires`, `characters.DoubleExpExpires`, `characters.OfflineTGEntered` | **WARNING** (MySQL 8 strict mode may reject) | Change to `DEFAULT NULL` or `DEFAULT '0001-01-01 00:00:00'` consistently |
| `utf8` charset (alias for `utf8mb3`) | `associates`, `bugreports`, `events`, `guild`, `guildattr`, `levexp`, `magictype` | **NOTE** (MySQL 8 treats `utf8` as alias for `utf8mb3`; `utf8mb4` is preferred) | Standardize to `utf8mb4` where multi-byte chars needed; `latin1` is fine for pure-ASCII game data |
| `SET FOREIGN_KEY_CHECKS=0` at start, never reset to 1 | Dump header only | **NOTE** (non-fatal but sloppy) | Add `SET FOREIGN_KEY_CHECKS=1;` at end of init.sql |

---

## Other Notable Tables

| Table | Engine | Charset | Auth/Char relevance |
|-------|--------|---------|---------------------|
| `accounts` | InnoDB | latin1 | Auth — username/password lookup |
| `characters` | InnoDB | latin1 | Character creation/load |
| `items` | InnoDB | latin1 | Character inventory |
| `items_log` | InnoDB | latin1 | Item transaction audit |
| `itemtype` | InnoDB | latin1 | Item definitions (static reference data) |
| `itemadd` | InnoDB | latin1 | Item enchant bonus table |
| `guild` | **MyISAM** | utf8 | Guild info — has ZEROFILL, needs migration |
| `guildattr` | **MyISAM** | utf8 | Guild membership — needs migration |
| `associates` | InnoDB | utf8 | Friend/enemy relationships |
| `nobility` | InnoDB | latin1 | Nobility donation tracking |
| `magictype` | **MyISAM** | latin1 | Skill definitions — heavy ZEROFILL |
| `monstertype` | **MyISAM** | latin1 | Monster stats — heavy ZEROFILL |
| `passages` | **MyISAM** | latin1 | Map portal entrance data — ZEROFILL |
| `portals` | **MyISAM** | latin1 | Map portal exit data — ZEROFILL |
| `levexp` | **MyISAM** | utf8 | Level/XP curve table |
| `drop_rules` | InnoDB | latin1 | Monster drop table — has DOUBLE(4,2) |
| `npcs` | InnoDB | latin1 | NPC placement |
| `maps` | InnoDB | latin1 | Map definitions |
| `spawns` | InnoDB | latin1 | Monster spawn points |
| `skills` | InnoDB | latin1 | Character skill instances |
| `proficiencies` | InnoDB | latin1 | Character weapon proficiency |
| `reborns` | InnoDB | latin1 | Rebirth/reincarnation records |
| `stats` | InnoDB | latin1 | Character stat history |
| `shops` | InnoDB | latin1 | NPC shop inventory |
| `sobs` | InnoDB | latin1 | Server objects (SOBs/furniture) |
| `tasks` | InnoDB | latin1 | Quest tasks |
| `bugreports` | InnoDB | utf8 | Bug report log |
| `chat_log` | InnoDB | latin1 | Chat message log |
| `minedrops` | InnoDB | latin1 | Mining loot table |

Total: 30 tables. No foreign key constraints defined anywhere in the dump (MyISAM tables cannot have them; InnoDB tables in this dump also omit them).

---

## Summary for init.sql (task 1.26)

The modernized `init.sql` must:
1. Replace all `ZEROFILL` with plain `unsigned` (or remove unsigned if not needed)
2. Change `DOUBLE(4,2)` to `DECIMAL(4,2)` in `drop_rules`
3. Convert all `ENGINE=MyISAM` tables to `ENGINE=InnoDB`
4. Remove `ROW_FORMAT` clauses (or keep only InnoDB-valid ones)
5. Normalize `int(N)` display widths — drop the `(N)` suffix throughout
6. Add `SET FOREIGN_KEY_CHECKS=1;` at end
7. Optionally upgrade `CHARSET=utf8` to `utf8mb4` for guild/associate tables
8. Keep `accounts.Password varchar(16)` as plain text for M1 (matches Redux auth logic)
