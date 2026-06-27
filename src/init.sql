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
-- UID band >= 90000 avoids collision with characters.CharacterID AUTO_INCREMENT
-- (the world roster is keyed by UID across all entity kinds).
-- Mesh is a placeholder lookface (live-capture the exact humanoid id);
-- Type=2 (NpcType.Task = clickable dialog); BaseId reserved (EPIC-8, unused v1).
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
    (90001, 'Guide',   1010, 63, 109, 1, 2, NULL),
    (90002, 'Greeter', 1010, 60, 111, 1, 2, NULL);

SET FOREIGN_KEY_CHECKS = 1;
