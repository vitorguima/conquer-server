-- Conquer Online 5065 Private Server — MySQL 8 compatible schema
-- Generated for M1 POC. Column names align with C# repositories.

SET FOREIGN_KEY_CHECKS = 0;

CREATE DATABASE IF NOT EXISTS conquer
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

USE conquer;

-- ============================================================
-- accounts: auth table (plain-text password for M1 POC)
-- AccountID aligns with AccountRepository.FindByUsername query
-- Salt column reserved for future hashing (empty for now)
-- ============================================================
CREATE TABLE IF NOT EXISTS `account` (
    `AccountID` INT NOT NULL AUTO_INCREMENT,
    `Username`  VARCHAR(16)  NOT NULL,
    `Password`  VARCHAR(64)  NOT NULL,
    `Salt`      VARCHAR(64)  NOT NULL DEFAULT '',
    `Permission` INT         NOT NULL DEFAULT 1,
    `Status`    INT          NOT NULL DEFAULT 0,
    PRIMARY KEY (`AccountID`),
    UNIQUE KEY `uq_username` (`Username`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ============================================================
-- characters: character table
-- Columns align exactly with CharacterRepository queries
-- ============================================================
CREATE TABLE IF NOT EXISTS `characters` (
    `CharacterID`  INT  NOT NULL AUTO_INCREMENT,
    `AccountID`    INT  NOT NULL,
    `Name`         VARCHAR(16) NOT NULL,
    `Mesh`         INT  NOT NULL DEFAULT 0,
    `Avatar`       INT  NOT NULL DEFAULT 0,
    `Level`        INT  NOT NULL DEFAULT 1,
    `MapID`        INT  NOT NULL DEFAULT 1010,
    `X`            INT  NOT NULL DEFAULT 61,
    `Y`            INT  NOT NULL DEFAULT 109,
    `Silver`       INT  NOT NULL DEFAULT 1000,
    `Strength`     INT  NOT NULL DEFAULT 0,
    `Agility`      INT  NOT NULL DEFAULT 0,
    `Vitality`     INT  NOT NULL DEFAULT 0,
    `Spirit`       INT  NOT NULL DEFAULT 0,
    `HealthPoints` INT  NOT NULL DEFAULT 0,
    `ManaPoints`   INT  NOT NULL DEFAULT 0,
    PRIMARY KEY (`CharacterID`),
    UNIQUE KEY `uq_name` (`Name`),
    INDEX `idx_account` (`AccountID`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ============================================================
-- Test account: username=testplayer, password=password123 (plain text for M1)
-- ============================================================
INSERT IGNORE INTO `account` (Username, Password, Salt, Status, Permission)
VALUES ('testplayer', 'password123', '', 1, 5);

-- ============================================================
-- cq_npc: static NPCs (EPIC-3)
-- Columns align exactly with NpcRepository.All() query.
-- The real Map-1010 (BirthVillage) NPC set, sourced from the original `npcs` table
-- (Nov_16_Backup.sql): real lookface Mesh + tile X/Y, Type=2 (NpcType.Task = clickable
-- dialog). That table has NO name column, and the original [2030] spawn omits the name
-- for nameless NPCs -> the client renders the bare model with no floating label
-- (authentic). So Name='' here on purpose; SpawnNpc.Build emits NetString Count=0.
-- UID = original npc id + 1_000_000 to keep NPCs out of the low characters.CharacterID
-- range (the world roster is keyed by UID across all entity kinds). BaseId reserved
-- (EPIC-8, unused v1).
-- ============================================================
CREATE TABLE IF NOT EXISTS `cq_npc` (
    `UID`    INT          NOT NULL,
    `Name`   VARCHAR(32)  NOT NULL DEFAULT '',
    `MapID`  INT          NOT NULL,
    `X`      INT          NOT NULL,
    `Y`      INT          NOT NULL,
    `Mesh`   INT          NOT NULL DEFAULT 1,
    `Type`   INT          NOT NULL DEFAULT 2,
    `BaseId` INT          NULL,
    PRIMARY KEY (`UID`),
    INDEX `idx_npc_map` (`MapID`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

INSERT IGNORE INTO `cq_npc` (UID, Name, MapID, X, Y, Mesh, Type, BaseId) VALUES
    (1000425, '', 1010, 96,  42, 2690, 2, NULL),
    (1005672, '', 1010, 62, 105, 5050, 2, NULL),
    (1010004, '', 1010, 70,  37,  170, 2, NULL),
    (1010005, '', 1010, 66,  46,   56, 2, NULL),
    (1010006, '', 1010, 72,  50,   86, 2, NULL),
    (1010007, '', 1010, 76,  55,   46, 2, NULL),
    (1010008, '', 1010, 80,  62,   36, 2, NULL),
    (1010009, '', 1010, 85,  41,  180, 2, NULL),
    (1010010, '', 1010, 88,  31,  190, 2, NULL),
    (1010055, '', 1010, 74,  37,  130, 2, NULL);

-- ============================================================
-- monstertype: monster stat templates (EPIC-4 Phase 0)
-- Subset of the original `monstertype` table (Nov_16_Backup.sql) — the columns combat +
-- spawn need in Phase 0. A few low-level newbie monsters; more can be imported later.
-- Columns align with MonsterTypeRepository.All().
-- ============================================================
CREATE TABLE IF NOT EXISTS `monstertype` (
    `ID`          INT          NOT NULL,
    `Name`        VARCHAR(32)  NOT NULL DEFAULT '?T',
    `Mesh`        INT          NOT NULL DEFAULT 0,
    `Life`        INT          NOT NULL DEFAULT 0,
    `AttackMin`   INT          NOT NULL DEFAULT 0,
    `AttackMax`   INT          NOT NULL DEFAULT 0,
    `AttackRange` INT          NOT NULL DEFAULT 1,
    `ViewRange`   INT          NOT NULL DEFAULT 8,
    `Defence`     INT          NOT NULL DEFAULT 0,
    `Level`       INT          NOT NULL DEFAULT 1,
    `BonusExp`    INT          NOT NULL DEFAULT 0,
    PRIMARY KEY (`ID`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

INSERT IGNORE INTO `monstertype`
    (ID, Name, Mesh, Life, AttackMin, AttackMax, AttackRange, ViewRange, Defence, Level, BonusExp) VALUES
    (1, 'Pheasant',   104,  33,  5,  6, 1, 8, 0, 1, 100),
    (2, 'Turtledove', 304,  81, 10, 15, 1, 8, 0, 7, 100),
    (6, 'Rabbit',     112,  20,  3,  4, 1, 8, 0, 1, 100);

-- ============================================================
-- spawns: monster spawn regions (EPIC-4 Phase 0). One TEST region of Pheasants on Map 1010
-- (BirthVillage) right by the spawn point (~60,108) so a fresh login sees monsters immediately.
-- Box (52,100)-(68,114), up to 6 monsters. Columns align with SpawnRepository.All().
-- ============================================================
CREATE TABLE IF NOT EXISTS `spawns` (
    `UID`         INT NOT NULL AUTO_INCREMENT,
    `Map`         INT NOT NULL,
    `X1`          INT NOT NULL,
    `Y1`          INT NOT NULL,
    `X2`          INT NOT NULL,
    `Y2`          INT NOT NULL,
    `MonsterType` INT NOT NULL,
    `AmountPer`   INT NOT NULL DEFAULT 1,
    `AmountMax`   INT NOT NULL DEFAULT 1,
    `Frequency`   INT NOT NULL DEFAULT 10,
    PRIMARY KEY (`UID`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

INSERT IGNORE INTO `spawns`
    (UID, Map, X1, Y1, X2, Y2, MonsterType, AmountPer, AmountMax, Frequency) VALUES
    (1, 1010, 52, 100, 68, 114, 1, 2, 6, 10);

SET FOREIGN_KEY_CHECKS = 1;
