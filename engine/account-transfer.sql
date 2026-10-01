-- AFK Realm account transfer.
-- Copies accounts and all of their characters from {SRC_AUTH}/{SRC_CHARS} into {DST_AUTH}/{DST_CHARS}.
--
-- Export: source = the live server, destination = empty staging schemas, ids are kept (@identity = 1).
-- Import: source = staging schemas loaded from an .afkaccount file, destination = the live server.
--         Every id that could collide is renumbered above the highest id already used:
--         accounts, character guids, item guids, pet ids, mail ids, equipment set guids.
--         An account that already exists (same name) is reused: its password is replaced
--         by the exported one and the characters are added to it.
-- Not copied: guild/arena team/group membership, auctions, instance locks, corpses,
-- active auras, calendar, tickets and logs - they belong to the old server.
--
-- The caller sets @accounts ('CHRIS,NATALIE') and @identity (0 or 1) before this script.

USE {WORK};
SET SESSION group_concat_max_len = 1048576;
-- Lenient mode: a column that only the newer server has gets its implicit default.
SET SESSION sql_mode = 'NO_ENGINE_SUBSTITUTION';

DROP TABLE IF EXISTS m_acc, m_chr, m_item, m_pet, m_mail, m_set, m_rule, m_colmap, m_tmp, m_report;
DROP PROCEDURE IF EXISTS m_optional_items;
DROP PROCEDURE IF EXISTS m_copy;
DROP PROCEDURE IF EXISTS m_run;
CREATE TABLE m_report (copied_table VARCHAR(64), copied_rows INT);

-- ---------------------------------------------------------------- accounts
CREATE TABLE m_acc (old INT UNSIGNED PRIMARY KEY, new INT UNSIGNED NULL, uname VARCHAR(32), existed TINYINT NOT NULL DEFAULT 0);
INSERT INTO m_acc (old, uname)
  SELECT id, username FROM {SRC_AUTH}.account WHERE FIND_IN_SET(UPPER(username) COLLATE utf8mb4_general_ci, UPPER(CONVERT(@accounts USING utf8mb4)) COLLATE utf8mb4_general_ci);
UPDATE m_acc m JOIN {DST_AUTH}.account t ON UPPER(t.username) = UPPER(m.uname) COLLATE utf8mb4_general_ci SET m.new = t.id, m.existed = 1;
SET @b = (SELECT COALESCE(MAX(id), 0) FROM {DST_AUTH}.account);
CREATE TABLE m_tmp AS SELECT old, ROW_NUMBER() OVER (ORDER BY old) AS rn FROM m_acc WHERE new IS NULL;
UPDATE m_acc m JOIN m_tmp x ON x.old = m.old SET m.new = IF(@identity = 1, m.old, @b + x.rn);
DROP TABLE m_tmp;

-- ---------------------------------------------------------------- characters
CREATE TABLE m_chr (old INT UNSIGNED PRIMARY KEY, new INT UNSIGNED, oldname VARCHAR(12), newname VARCHAR(12), renamed TINYINT NOT NULL DEFAULT 0);
SET @b = (SELECT COALESCE(MAX(guid), 0) FROM {DST_CHARS}.characters);
INSERT INTO m_chr (old, new, oldname, newname)
  SELECT c.guid, IF(@identity = 1, c.guid, @b + ROW_NUMBER() OVER (ORDER BY c.guid)), c.name, c.name
  FROM {SRC_CHARS}.characters c JOIN m_acc a ON a.old = c.account
  WHERE c.deleteDate IS NULL OR c.deleteDate = 0;
-- A name already taken on the destination gets a placeholder name and the rename
-- flag (at_login 1): the game asks for a new name at the next login.
UPDATE m_chr m JOIN {DST_CHARS}.characters t ON t.name = m.oldname COLLATE utf8mb4_general_ci
  SET m.newname = CONCAT('Mig', m.new), m.renamed = 1;

-- ---------------------------------------------------------------- mail, pets, sets
CREATE TABLE m_mail (old INT UNSIGNED PRIMARY KEY, new INT UNSIGNED);
SET @b = (SELECT COALESCE(MAX(id), 0) FROM {DST_CHARS}.mail);
INSERT INTO m_mail SELECT id, IF(@identity = 1, id, @b + ROW_NUMBER() OVER (ORDER BY id)) FROM {SRC_CHARS}.mail WHERE receiver IN (SELECT old FROM m_chr);

CREATE TABLE m_pet (old INT UNSIGNED PRIMARY KEY, new INT UNSIGNED);
SET @b = (SELECT COALESCE(MAX(id), 0) FROM {DST_CHARS}.character_pet);
INSERT INTO m_pet SELECT id, IF(@identity = 1, id, @b + ROW_NUMBER() OVER (ORDER BY id)) FROM {SRC_CHARS}.character_pet WHERE owner IN (SELECT old FROM m_chr);

CREATE TABLE m_set (old BIGINT UNSIGNED PRIMARY KEY, new BIGINT UNSIGNED);
SET @b = (SELECT COALESCE(MAX(setguid), 0) FROM {DST_CHARS}.character_equipmentsets);
INSERT INTO m_set SELECT setguid, IF(@identity = 1, setguid, @b + ROW_NUMBER() OVER (ORDER BY setguid)) FROM {SRC_CHARS}.character_equipmentsets WHERE guid IN (SELECT old FROM m_chr);

-- ---------------------------------------------------------------- items
CREATE TABLE m_tmp (old INT UNSIGNED PRIMARY KEY);
INSERT IGNORE INTO m_tmp SELECT guid FROM {SRC_CHARS}.item_instance WHERE owner_guid IN (SELECT old FROM m_chr);
INSERT IGNORE INTO m_tmp SELECT item FROM {SRC_CHARS}.character_inventory WHERE guid IN (SELECT old FROM m_chr);
INSERT IGNORE INTO m_tmp SELECT item_guid FROM {SRC_CHARS}.mail_items WHERE mail_id IN (SELECT old FROM m_mail);
INSERT IGNORE INTO m_tmp SELECT item_guid FROM {SRC_CHARS}.character_gifts WHERE guid IN (SELECT old FROM m_chr);
DELIMITER //
CREATE PROCEDURE m_optional_items()
BEGIN
  IF (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = '{SRC_CHARS}' AND table_name = 'mod_ascension_bank_item') > 0 THEN
    INSERT IGNORE INTO m_tmp SELECT item_guid FROM {SRC_CHARS}.mod_ascension_bank_item
      WHERE (owner_kind = 0 AND owner_id IN (SELECT old FROM m_chr)) OR (owner_kind = 1 AND owner_id IN (SELECT old FROM m_acc));
  END IF;
  IF (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = '{SRC_CHARS}' AND table_name = 'ascension_manastorm_cache') > 0 THEN
    INSERT IGNORE INTO m_tmp SELECT item FROM {SRC_CHARS}.ascension_manastorm_cache WHERE guid IN (SELECT old FROM m_chr);
  END IF;
END //
DELIMITER ;
CALL m_optional_items();
DROP PROCEDURE m_optional_items;
-- Only items that really exist are carried over.
CREATE TABLE m_item (old INT UNSIGNED PRIMARY KEY, new INT UNSIGNED);
SET @b = (SELECT COALESCE(MAX(guid), 0) FROM {DST_CHARS}.item_instance);
INSERT INTO m_item SELECT t.old, IF(@identity = 1, t.old, @b + ROW_NUMBER() OVER (ORDER BY t.old)) FROM m_tmp t JOIN {SRC_CHARS}.item_instance i ON i.guid = t.old;
DROP TABLE m_tmp;

-- ---------------------------------------------------------------- copy rules
-- m_rule: which rows of a table belong to the transferred accounts/characters.
-- m_colmap: how a column is rewritten. Kinds:
--   acc/chr/item/pet/mail/set    mandatory mapping
--   chr?/item?/mail?             mapped when possible, otherwise unchanged
--   chr0                         mapped when possible, otherwise 0
--   sql:<expression>             literal expression over alias s
CREATE TABLE m_rule (tbl VARCHAR(64) PRIMARY KEY, filter TEXT, ord INT);
CREATE TABLE m_colmap (tbl VARCHAR(64), col VARCHAR(64), kind TEXT, PRIMARY KEY (tbl, col));

INSERT INTO m_rule VALUES
 ('characters',                 's.guid IN (SELECT old FROM m_chr)', 1),
 ('item_instance',              's.guid IN (SELECT old FROM m_item)', 2),
 ('character_inventory',        's.guid IN (SELECT old FROM m_chr) AND s.item IN (SELECT old FROM m_item)', 3),
 ('character_pet',              's.id IN (SELECT old FROM m_pet)', 3),
 ('character_pet_declinedname', 's.id IN (SELECT old FROM m_pet)', 4),
 ('pet_aura',                   's.guid IN (SELECT old FROM m_pet)', 4),
 ('pet_spell',                  's.guid IN (SELECT old FROM m_pet)', 4),
 ('pet_spell_cooldown',         's.guid IN (SELECT old FROM m_pet)', 4),
 ('mail',                       's.id IN (SELECT old FROM m_mail)', 3),
 ('mail_items',                 's.mail_id IN (SELECT old FROM m_mail) AND s.item_guid IN (SELECT old FROM m_item)', 4),
 ('character_equipmentsets',    's.setguid IN (SELECT old FROM m_set)', 4),
 ('character_gifts',            's.guid IN (SELECT old FROM m_chr) AND s.item_guid IN (SELECT old FROM m_item)', 4),
 ('character_social',           's.guid IN (SELECT old FROM m_chr) AND s.friend IN (SELECT old FROM m_chr)', 4),
 ('item_refund_instance',       's.item_guid IN (SELECT old FROM m_item) AND s.player_guid IN (SELECT old FROM m_chr)', 4),
 ('item_loot_storage',          's.containerGUID IN (SELECT old FROM m_item)', 4),
 ('ascension_manastorm_cache',  's.guid IN (SELECT old FROM m_chr) AND s.item IN (SELECT old FROM m_item)', 4),
 ('ascension_manastorm_clear',  's.guid IN (SELECT old FROM m_chr)', 4),
 ('mod_ascension_bank_tab',     '(s.owner_kind = 0 AND s.owner_id IN (SELECT old FROM m_chr)) OR (s.owner_kind = 1 AND s.owner_id IN (SELECT old FROM m_acc))', 4),
 ('mod_ascension_bank_money',   '(s.owner_kind = 0 AND s.owner_id IN (SELECT old FROM m_chr)) OR (s.owner_kind = 1 AND s.owner_id IN (SELECT old FROM m_acc))', 4),
 ('mod_ascension_bank_item',    '((s.owner_kind = 0 AND s.owner_id IN (SELECT old FROM m_chr)) OR (s.owner_kind = 1 AND s.owner_id IN (SELECT old FROM m_acc))) AND s.item_guid IN (SELECT old FROM m_item)', 4),
 ('account_appearance_collection', 's.account_id IN (SELECT old FROM m_acc)', 5),
 ('account_ascension_settings', 's.account_id IN (SELECT old FROM m_acc)', 5),
 ('account_vanity_collection',  's.account_id IN (SELECT old FROM m_acc)', 5),
 ('account_data',               's.accountId IN (SELECT old FROM m_acc)', 5),
 ('account_tutorial',           's.accountId IN (SELECT old FROM m_acc)', 5),
 ('coa_account_warchest',       's.account IN (SELECT old FROM m_acc)', 5);

INSERT INTO m_colmap VALUES
 ('characters','guid','chr'), ('characters','account','acc'),
 ('characters','name','sql:(SELECT newname FROM m_chr WHERE old = s.guid)'),
 ('characters','at_login','sql:s.at_login | IF((SELECT renamed FROM m_chr WHERE old = s.guid) = 1, 1, 0)'),
 ('characters','online','sql:0'), ('characters','instance_id','sql:0'),
 ('item_instance','guid','item'), ('item_instance','owner_guid','chr0'),
 ('item_instance','creatorGuid','chr0'), ('item_instance','giftCreatorGuid','chr0'),
 ('character_inventory','guid','chr'), ('character_inventory','bag','item?'), ('character_inventory','item','item'),
 ('character_pet','id','pet'), ('character_pet','owner','chr'),
 ('character_pet_declinedname','id','pet'), ('character_pet_declinedname','owner','chr'),
 ('pet_aura','guid','pet'), ('pet_aura','casterGuid','sql:0'),
 ('pet_spell','guid','pet'), ('pet_spell_cooldown','guid','pet'),
 ('mail','id','mail'), ('mail','receiver','chr'),
 ('mail','sender','sql:IF(s.messageType = 0, COALESCE((SELECT new FROM m_chr WHERE old = s.sender), s.sender), s.sender)'),
 ('mail_items','mail_id','mail'), ('mail_items','item_guid','item'), ('mail_items','receiver','chr'),
 ('character_equipmentsets','guid','chr'), ('character_equipmentsets','setguid','set'),
 ('character_gifts','guid','chr'), ('character_gifts','item_guid','item'),
 ('character_social','guid','chr'), ('character_social','friend','chr'),
 ('item_refund_instance','item_guid','item'), ('item_refund_instance','player_guid','chr'),
 ('item_loot_storage','containerGUID','item'),
 ('ascension_manastorm_cache','item','item'), ('ascension_manastorm_cache','guid','chr'),
 ('ascension_manastorm_clear','guid','chr'), ('ascension_manastorm_clear','mail_id','mail?'),
 ('coa_character_looted_item','itemGuid','item?'),
 ('mod_ascension_bank_tab','owner_id','sql:IF(s.owner_kind = 0, (SELECT new FROM m_chr WHERE old = s.owner_id), (SELECT new FROM m_acc WHERE old = s.owner_id))'),
 ('mod_ascension_bank_money','owner_id','sql:IF(s.owner_kind = 0, (SELECT new FROM m_chr WHERE old = s.owner_id), (SELECT new FROM m_acc WHERE old = s.owner_id))'),
 ('mod_ascension_bank_item','owner_id','sql:IF(s.owner_kind = 0, (SELECT new FROM m_chr WHERE old = s.owner_id), (SELECT new FROM m_acc WHERE old = s.owner_id))'),
 ('mod_ascension_bank_item','item_guid','item'),
 ('account_appearance_collection','account_id','acc'), ('account_ascension_settings','account_id','acc'),
 ('account_vanity_collection','account_id','acc'), ('account_data','accountId','acc'),
 ('account_tutorial','accountId','acc'), ('coa_account_warchest','account','acc');
INSERT INTO m_colmap SELECT 'character_equipmentsets', CONCAT('item', n), 'item?' FROM
  (SELECT 0 n UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9
   UNION SELECT 10 UNION SELECT 11 UNION SELECT 12 UNION SELECT 13 UNION SELECT 14 UNION SELECT 15 UNION SELECT 16 UNION SELECT 17 UNION SELECT 18) x;

-- Every other table whose only character reference is `guid` (plain per-character data).
INSERT INTO m_rule (tbl, filter, ord)
  SELECT c.table_name, 's.guid IN (SELECT old FROM m_chr)', 6
  FROM information_schema.columns c
  WHERE c.table_schema = '{SRC_CHARS}' AND c.column_name = 'guid'
    AND (c.table_name LIKE 'character\_%' OR c.table_name LIKE 'coa\_%' OR c.table_name LIKE 'ascension\_manastorm\_%'
         OR c.table_name IN ('mod_craftsmans_codex', 'mail_server_character', 'battleground_deserters'))
    AND c.table_name NOT IN ('character_aura', 'character_instance', 'character_banned', 'character_arena_stats')
    AND c.table_name NOT IN (SELECT tbl FROM m_rule);
INSERT IGNORE INTO m_colmap (tbl, col, kind)
  SELECT tbl, 'guid', 'chr' FROM m_rule WHERE ord = 6;

-- ---------------------------------------------------------------- copy engine
DELIMITER //
CREATE PROCEDURE m_copy(IN p_tbl VARCHAR(64), IN p_filter TEXT)
BEGIN
  DECLARE v_cols TEXT;
  DECLARE v_exprs TEXT;
  IF (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = '{DST_CHARS}' AND table_name = p_tbl) > 0
     AND (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = '{SRC_CHARS}' AND table_name = p_tbl) > 0 THEN
    SELECT GROUP_CONCAT(CONCAT('`', t.column_name, '`') ORDER BY t.ordinal_position),
           GROUP_CONCAT(
             CASE
               WHEN m.kind IS NULL THEN CONCAT('s.`', t.column_name, '`')
               WHEN m.kind LIKE 'sql:%' THEN SUBSTRING(m.kind, 5)
               WHEN m.kind = 'chr0' THEN CONCAT('COALESCE((SELECT new FROM m_chr WHERE old = s.`', t.column_name, '`), 0)')
               WHEN m.kind LIKE '%?' THEN CONCAT('COALESCE((SELECT new FROM m_', REPLACE(m.kind, '?', ''), ' WHERE old = s.`', t.column_name, '`), s.`', t.column_name, '`)')
               ELSE CONCAT('(SELECT new FROM m_', m.kind, ' WHERE old = s.`', t.column_name, '`)')
             END ORDER BY t.ordinal_position)
      INTO v_cols, v_exprs
      FROM information_schema.columns t
      JOIN information_schema.columns s2 ON s2.table_schema = '{SRC_CHARS}' AND s2.table_name = t.table_name AND s2.column_name = t.column_name
      LEFT JOIN m_colmap m ON m.tbl = t.table_name AND m.col = t.column_name
      WHERE t.table_schema = '{DST_CHARS}' AND t.table_name = p_tbl
        AND (t.extra NOT LIKE '%GENERATED%');
    SET @m_sql = CONCAT('INSERT IGNORE INTO {DST_CHARS}.`', p_tbl, '` (', v_cols, ') SELECT ', v_exprs,
                        ' FROM {SRC_CHARS}.`', p_tbl, '` s WHERE ', p_filter);
    PREPARE m_stmt FROM @m_sql;
    EXECUTE m_stmt;
    SET @m_rows = ROW_COUNT();
    IF @m_rows > 0 THEN INSERT INTO m_report VALUES (p_tbl, @m_rows); END IF;
    DEALLOCATE PREPARE m_stmt;
  END IF;
END //

CREATE PROCEDURE m_run()
BEGIN
  DECLARE done INT DEFAULT 0;
  DECLARE v_tbl VARCHAR(64);
  DECLARE v_filter TEXT;
  DECLARE cur CURSOR FOR SELECT tbl, filter FROM m_rule ORDER BY ord, tbl;
  DECLARE CONTINUE HANDLER FOR NOT FOUND SET done = 1;
  DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;

  IF (SELECT COUNT(*) FROM m_acc) = 0 THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'None of the accounts was found.';
  END IF;
  -- Guard against importing twice: a character that already sits on the target
  -- account under its exported name means this file was imported before.
  IF (SELECT COUNT(*) FROM m_chr c JOIN m_acc a ON a.existed = 1
        JOIN {SRC_CHARS}.characters sc ON sc.guid = c.old AND sc.account = a.old
        JOIN {DST_CHARS}.characters t ON t.account = a.new AND t.name = c.oldname COLLATE utf8mb4_general_ci) > 0 THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'These characters already exist on this account; the file was apparently imported before. Nothing was changed.';
  END IF;

  START TRANSACTION;

  -- auth: new accounts are inserted, existing ones get the exported password.
  INSERT INTO {DST_AUTH}.account (id, username, salt, verifier, email, reg_mail, joindate, expansion, locale)
    SELECT m.new, a.username, a.salt, a.verifier, a.email, a.reg_mail, a.joindate, a.expansion, a.locale
    FROM {SRC_AUTH}.account a JOIN m_acc m ON m.old = a.id WHERE m.existed = 0;
  UPDATE {DST_AUTH}.account t JOIN m_acc m ON m.new = t.id AND m.existed = 1
    JOIN {SRC_AUTH}.account a ON a.id = m.old
    SET t.salt = a.salt, t.verifier = a.verifier, t.session_key = NULL;
  DELETE FROM {DST_AUTH}.account_access WHERE id IN (SELECT new FROM m_acc);
  INSERT INTO {DST_AUTH}.account_access (id, gmlevel, RealmID, comment)
    SELECT m.new, x.gmlevel, x.RealmID, x.comment FROM {SRC_AUTH}.account_access x JOIN m_acc m ON m.old = x.id;

  OPEN cur;
  read_loop: LOOP
    FETCH cur INTO v_tbl, v_filter;
    IF done = 1 THEN LEAVE read_loop; END IF;
    CALL m_copy(v_tbl, v_filter);
  END LOOP;
  CLOSE cur;

  -- character count per account shown on the realm list
  IF (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = '{DST_AUTH}' AND table_name = 'realmlist') > 0 THEN
    DELETE FROM {DST_AUTH}.realmcharacters WHERE acctid IN (SELECT new FROM m_acc);
    INSERT INTO {DST_AUTH}.realmcharacters (realmid, acctid, numchars)
      SELECT r.id, m.new, (SELECT COUNT(*) FROM {DST_CHARS}.characters c WHERE c.account = m.new)
      FROM m_acc m CROSS JOIN {DST_AUTH}.realmlist r;
  END IF;

  COMMIT;
END //
DELIMITER ;

CALL m_run();

-- Result lines read by AFK Realm (tab separated): kind, values...
SELECT 'ACCOUNT', m.uname, m.old, m.new, m.existed FROM m_acc m;
SELECT 'CHARACTER', c.oldname, c.newname, c.renamed FROM m_chr c;
SELECT 'TABLE', copied_table, copied_rows FROM m_report ORDER BY copied_table;

DROP PROCEDURE m_copy;
DROP PROCEDURE m_run;
