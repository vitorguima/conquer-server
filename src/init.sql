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

SET FOREIGN_KEY_CHECKS = 1;
