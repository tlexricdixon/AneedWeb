-- Test fixture ONLY. Run against an isolated database, never the station SAMDB.
CREATE DATABASE IF NOT EXISTS aneed_fixture CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE aneed_fixture;
CREATE TABLE songlist (
 ID INT PRIMARY KEY AUTO_INCREMENT,
 artist VARCHAR(255) NOT NULL DEFAULT '',
 title VARCHAR(255) NOT NULL DEFAULT '',
 album VARCHAR(255) NOT NULL DEFAULT '',
 albumyear VARCHAR(4) NOT NULL DEFAULT '0',
 info TEXT NULL,
 picture VARCHAR(255) NOT NULL DEFAULT '',
 genre VARCHAR(20) NOT NULL DEFAULT '',
 composer VARCHAR(100) NOT NULL DEFAULT '',
 label VARCHAR(255) NOT NULL DEFAULT '',
 ISRC VARCHAR(50) NOT NULL DEFAULT '',
 songtype CHAR(1) NOT NULL DEFAULT 'S'
) ENGINE=MyISAM;
INSERT INTO songlist (ID,artist,title,album,albumyear,info,picture,genre,composer,label,ISRC) VALUES
(1,'Queen','Radio Ga Ga','The Works','1984','Fixture song description','C:/SAM/covers/queen.jpg','Rock','Roger Taylor','EMI','GBTEST000001'),
(2,'Queen','Under Pressure','Hot Space','1982',NULL,'','Rock','','',''),
(3,'Kate Bush','Running Up That Hill','Hounds of Love','1985','A fixture description','','Art Pop','','',''),
(4,'Percent 100%','Literal % title','','0',NULL,'','','','',''),
(5,'Guns N\' Roses','Sweet Child O\' Mine','Appetite for Destruction','1987','','','','','','');
INSERT INTO songlist (artist,title,songtype) VALUES ('Advertiser','Hidden advert','A');
CREATE USER IF NOT EXISTS 'aneed_fixture_reader'@'127.0.0.1';
GRANT SELECT ON aneed_fixture.songlist TO 'aneed_fixture_reader'@'127.0.0.1';

-- Ensure broadcast matching can find songs outside the first 100 search results.
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 000');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 001');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 002');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 003');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 004');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 005');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 006');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 007');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 008');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 009');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 010');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 011');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 012');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 013');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 014');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 015');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 016');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 017');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 018');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 019');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 020');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 021');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 022');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 023');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 024');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 025');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 026');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 027');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 028');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 029');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 030');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 031');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 032');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 033');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 034');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 035');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 036');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 037');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 038');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 039');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 040');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 041');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 042');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 043');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 044');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 045');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 046');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 047');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 048');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 049');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 050');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 051');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 052');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 053');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 054');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 055');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 056');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 057');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 058');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 059');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 060');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 061');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 062');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 063');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 064');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 065');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 066');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 067');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 068');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 069');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 070');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 071');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 072');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 073');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 074');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 075');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 076');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 077');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 078');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 079');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 080');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 081');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 082');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 083');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 084');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 085');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 086');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 087');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 088');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 089');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 090');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 091');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 092');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 093');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 094');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 095');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 096');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 097');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 098');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 099');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 100');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 101');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 102');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 103');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 104');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 105');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 106');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 107');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 108');
INSERT INTO songlist (artist,title) VALUES ('Earlier Artist','Fixture song 109');
