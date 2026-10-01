-- AFK Realm module journal
--
-- Records what the SQL files of a module change in the server databases, so the module
-- can be removed again later together with its database changes.
--
--   CALL afk_modules.afk_begin(batch)   before the module's SQL files are applied:
--        copies every table listed in afk_modules.watch (filled by the engine from the
--        table names found in the SQL files) and remembers which tables exist.
--   CALL afk_modules.afk_finish(batch)  afterwards: compares, and keeps only the rows that
--        changed (old and new version) plus the tables the module created or dropped.
--   CALL afk_modules.afk_undo('name')   puts everything back. A row is only reverted when it
--        still looks exactly like the module left it; anything changed later is kept and
--        reported. Prints lines "REPORT <kind> <table> <text>".
--
-- Everything lives in the schema afk_modules, which is part of every server backup.

CREATE DATABASE IF NOT EXISTS afk_modules CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE afk_modules;

CREATE TABLE IF NOT EXISTS modules (
    name      VARCHAR(100) NOT NULL PRIMARY KEY,
    repo      VARCHAR(500) NOT NULL DEFAULT '',
    branch    VARCHAR(200) NOT NULL DEFAULT '',
    revision  VARCHAR(40)  NOT NULL DEFAULT '',
    installed DATETIME     NULL,
    status    VARCHAR(20)  NOT NULL DEFAULT 'ok',
    note      TEXT         NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- One batch = the SQL files of one module applied to one database in one run.
CREATE TABLE IF NOT EXISTS batches (
    id      INT AUTO_INCREMENT PRIMARY KEY,
    module  VARCHAR(100) NOT NULL,
    db      VARCHAR(64)  NOT NULL,
    created DATETIME     NOT NULL,
    files   MEDIUMTEXT   NOT NULL,          -- comma-separated file names, as in the updates table
    done    TINYINT      NOT NULL DEFAULT 0,
    KEY (module)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- kind: rows (shadow tables e<id>_old / e<id>_new), created, dropped (copy in e<id>_old),
--       altered (structure changed; not undone), untracked (changed without a copy; not undone)
CREATE TABLE IF NOT EXISTS entries (
    id       INT AUTO_INCREMENT PRIMARY KEY,
    batch    INT          NOT NULL,
    db       VARCHAR(64)  NOT NULL,
    tbl      VARCHAR(64)  NOT NULL,
    kind     VARCHAR(16)  NOT NULL,
    old_rows INT          NOT NULL DEFAULT 0,
    new_rows INT          NOT NULL DEFAULT 0,
    KEY (batch)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Tables to record for the batch that is being applied right now.
CREATE TABLE IF NOT EXISTS watch (
    db      VARCHAR(64)  NOT NULL,
    tbl     VARCHAR(64)  NOT NULL,
    state   TINYINT      NOT NULL DEFAULT 0,   -- 0 missing, 1 copied, 2 created by this module earlier
    sig     MEDIUMTEXT   NULL,
    copy    VARCHAR(64)  NULL,
    PRIMARY KEY (db, tbl)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS tables_before (
    db  VARCHAR(64) NOT NULL,
    tbl VARCHAR(64) NOT NULL,
    PRIMARY KEY (db, tbl)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DROP PROCEDURE IF EXISTS afk_exec;
DROP FUNCTION IF EXISTS afk_q;
DROP FUNCTION IF EXISTS afk_sig;
DROP FUNCTION IF EXISTS afk_cols;
DROP FUNCTION IF EXISTS afk_match;
DROP FUNCTION IF EXISTS afk_keymatch;
DROP PROCEDURE IF EXISTS afk_begin;
DROP PROCEDURE IF EXISTS afk_finish;
DROP PROCEDURE IF EXISTS afk_undo;

DELIMITER //

CREATE PROCEDURE afk_exec(IN s LONGTEXT)
BEGIN
    SET @afk_sql = s;
    PREPARE afk_stmt FROM @afk_sql;
    EXECUTE afk_stmt;
    SET @afk_rows = ROW_COUNT();
    DEALLOCATE PREPARE afk_stmt;
END //

-- `db`.`table`
CREATE FUNCTION afk_q(d VARCHAR(64), t VARCHAR(64)) RETURNS VARCHAR(200) DETERMINISTIC
    RETURN CONCAT('`', REPLACE(d, '`', '``'), '`.`', REPLACE(t, '`', '``'), '`') //

-- Structure fingerprint: columns and indexes.
CREATE FUNCTION afk_sig(d VARCHAR(64), t VARCHAR(64)) RETURNS MEDIUMTEXT READS SQL DATA
BEGIN
    DECLARE c MEDIUMTEXT; DECLARE i MEDIUMTEXT;
    SELECT GROUP_CONCAT(CONCAT_WS(':', column_name, column_type, is_nullable, IFNULL(column_default, '~'), extra, IFNULL(collation_name, ''))
                        ORDER BY ordinal_position SEPARATOR '|') INTO c
      FROM information_schema.columns WHERE table_schema = d AND table_name = t;
    SELECT GROUP_CONCAT(CONCAT_WS(':', index_name, non_unique, seq_in_index, IFNULL(column_name, ''))
                        ORDER BY index_name, seq_in_index SEPARATOR '|') INTO i
      FROM information_schema.statistics WHERE table_schema = d AND table_name = t;
    RETURN CONCAT(IFNULL(c, ''), '#', IFNULL(i, ''));
END //

-- Comma-separated column list without generated columns, optionally prefixed with an alias.
CREATE FUNCTION afk_cols(d VARCHAR(64), t VARCHAR(64), alias VARCHAR(10)) RETURNS MEDIUMTEXT READS SQL DATA
BEGIN
    DECLARE r MEDIUMTEXT;
    SELECT GROUP_CONCAT(CONCAT(IF(alias = '', '', CONCAT(alias, '.')), '`', REPLACE(column_name, '`', '``'), '`')
                        ORDER BY ordinal_position SEPARATOR ', ') INTO r
      FROM information_schema.columns
     WHERE table_schema = d AND table_name = t AND IFNULL(generation_expression, '') = '';
    RETURN r;
END //

-- "a.x = b.x AND a.y <=> b.y ...": two rows are identical (NULL-safe, all stored columns).
CREATE FUNCTION afk_match(d VARCHAR(64), t VARCHAR(64), a VARCHAR(10), b VARCHAR(10)) RETURNS MEDIUMTEXT READS SQL DATA
BEGIN
    DECLARE r MEDIUMTEXT;
    -- Text is compared byte by byte, so changes in case or accents count as changes too.
    SELECT GROUP_CONCAT(IF(c.column_key = 'PRI',
                           CONCAT(a, '.`', REPLACE(c.column_name, '`', '``'), '` = ', b, '.`', REPLACE(c.column_name, '`', '``'), '`'),
                           IF(c.data_type IN ('char', 'varchar', 'tinytext', 'text', 'mediumtext', 'longtext', 'enum', 'set'),
                              CONCAT('BINARY ', a, '.`', REPLACE(c.column_name, '`', '``'), '` <=> BINARY ', b, '.`', REPLACE(c.column_name, '`', '``'), '`'),
                              CONCAT(a, '.`', REPLACE(c.column_name, '`', '``'), '` <=> ', b, '.`', REPLACE(c.column_name, '`', '``'), '`')))
                        ORDER BY c.ordinal_position SEPARATOR ' AND ') INTO r
      FROM information_schema.columns c
     WHERE c.table_schema = d AND c.table_name = t AND IFNULL(c.generation_expression, '') = '';
    RETURN r;
END //

-- "a.id = b.id": two rows have the same primary key (all columns when there is none).
CREATE FUNCTION afk_keymatch(d VARCHAR(64), t VARCHAR(64), a VARCHAR(10), b VARCHAR(10)) RETURNS MEDIUMTEXT READS SQL DATA
BEGIN
    DECLARE r MEDIUMTEXT;
    SELECT GROUP_CONCAT(CONCAT(a, '.`', REPLACE(s.column_name, '`', '``'), '` = ', b, '.`', REPLACE(s.column_name, '`', '``'), '`')
                        ORDER BY s.seq_in_index SEPARATOR ' AND ') INTO r
      FROM information_schema.statistics s
     WHERE s.table_schema = d AND s.table_name = t AND s.index_name = 'PRIMARY';
    RETURN IFNULL(r, afk_match(d, t, a, b));
END //

CREATE PROCEDURE afk_begin(IN p_batch INT)
BEGIN
    DECLARE v_module VARCHAR(100);
    DECLARE v_db, v_tbl VARCHAR(64);
    DECLARE v_n INT DEFAULT 0;
    DECLARE v_done INT DEFAULT 0;
    DECLARE cur CURSOR FOR SELECT db, tbl FROM watch ORDER BY db, tbl;
    DECLARE CONTINUE HANDLER FOR NOT FOUND SET v_done = 1;
    SET SESSION group_concat_max_len = 16777216;
    SELECT module INTO v_module FROM batches WHERE id = p_batch;
    -- Changes are later found by the tables' last update time (whole seconds); the pause
    -- keeps writes from just before (for example the core's own updates) out of that window.
    DO SLEEP(1.2);

    DELETE FROM tables_before;
    INSERT INTO tables_before (db, tbl)
        SELECT table_schema, table_name FROM information_schema.tables
         WHERE table_schema IN ('acore_auth', 'acore_characters', 'acore_world', 'acore_playerbots') AND table_type = 'BASE TABLE';

    OPEN cur;
    copy_loop: LOOP
        FETCH cur INTO v_db, v_tbl;
        IF v_done THEN LEAVE copy_loop; END IF;
        SET v_n = v_n + 1;
        IF NOT EXISTS (SELECT 1 FROM tables_before WHERE db = v_db AND tbl = v_tbl) THEN
            UPDATE watch SET state = 0 WHERE db = v_db AND tbl = v_tbl;
        ELSEIF EXISTS (SELECT 1 FROM entries e JOIN batches b ON b.id = e.batch
                        WHERE b.module = v_module AND e.kind = 'created' AND e.db = v_db AND e.tbl = v_tbl) THEN
            -- The module's own table: it is dropped as a whole when the module is removed.
            UPDATE watch SET state = 2 WHERE db = v_db AND tbl = v_tbl;
        ELSE
            CALL afk_exec(CONCAT('DROP TABLE IF EXISTS afk_modules.`c', p_batch, '_', v_n, '`'));
            CALL afk_exec(CONCAT('CREATE TABLE afk_modules.`c', p_batch, '_', v_n, '` LIKE ', afk_q(v_db, v_tbl)));
            CALL afk_exec(CONCAT('INSERT INTO afk_modules.`c', p_batch, '_', v_n, '` (', afk_cols(v_db, v_tbl, ''), ') SELECT ',
                                 afk_cols(v_db, v_tbl, ''), ' FROM ', afk_q(v_db, v_tbl)));
            UPDATE watch SET state = 1, sig = afk_sig(v_db, v_tbl), copy = CONCAT('c', p_batch, '_', v_n)
             WHERE db = v_db AND tbl = v_tbl;
        END IF;
    END LOOP;
    CLOSE cur;
    UPDATE batches SET created = NOW() WHERE id = p_batch;
END //

CREATE PROCEDURE afk_finish(IN p_batch INT)
BEGIN
    DECLARE v_module VARCHAR(100);
    DECLARE v_created DATETIME;
    DECLARE v_db, v_tbl, v_copy VARCHAR(64);
    DECLARE v_state, v_entry, v_old, v_new INT;
    DECLARE v_sig MEDIUMTEXT;
    DECLARE v_done INT DEFAULT 0;
    DECLARE cur CURSOR FOR SELECT db, tbl, state, sig, copy FROM watch WHERE state = 1 ORDER BY db, tbl;
    DECLARE CONTINUE HANDLER FOR NOT FOUND SET v_done = 1;
    SET SESSION group_concat_max_len = 16777216;
    SET SESSION information_schema_stats_expiry = 0;
    SELECT module, created INTO v_module, v_created FROM batches WHERE id = p_batch;

    -- New tables
    INSERT INTO entries (batch, db, tbl, kind)
        SELECT p_batch, t.table_schema, t.table_name, 'created' FROM information_schema.tables t
         WHERE t.table_schema IN ('acore_auth', 'acore_characters', 'acore_world', 'acore_playerbots') AND t.table_type = 'BASE TABLE'
           AND NOT EXISTS (SELECT 1 FROM tables_before b WHERE b.db = t.table_schema AND b.tbl = t.table_name);

    OPEN cur;
    diff_loop: LOOP
        FETCH cur INTO v_db, v_tbl, v_state, v_sig, v_copy;
        IF v_done THEN LEAVE diff_loop; END IF;
        IF NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = v_db AND table_name = v_tbl) THEN
            -- Dropped by the module: the copy is kept to bring it back.
            INSERT INTO entries (batch, db, tbl, kind) VALUES (p_batch, v_db, v_tbl, 'dropped');
            SET v_entry = LAST_INSERT_ID();
            CALL afk_exec(CONCAT('RENAME TABLE afk_modules.`', v_copy, '` TO afk_modules.`e', v_entry, '_old`'));
            CALL afk_exec(CONCAT('SELECT COUNT(*) INTO @afk_n FROM afk_modules.`e', v_entry, '_old`'));
            UPDATE entries SET old_rows = @afk_n WHERE id = v_entry;
        ELSEIF BINARY afk_sig(v_db, v_tbl) <> BINARY v_sig THEN
            INSERT INTO entries (batch, db, tbl, kind) VALUES (p_batch, v_db, v_tbl, 'altered');
            CALL afk_exec(CONCAT('DROP TABLE afk_modules.`', v_copy, '`'));
        ELSE
            INSERT INTO entries (batch, db, tbl, kind) VALUES (p_batch, v_db, v_tbl, 'rows');
            SET v_entry = LAST_INSERT_ID();
            CALL afk_exec(CONCAT('CREATE TABLE afk_modules.`e', v_entry, '_old` LIKE afk_modules.`', v_copy, '`'));
            CALL afk_exec(CONCAT('CREATE TABLE afk_modules.`e', v_entry, '_new` LIKE afk_modules.`', v_copy, '`'));
            -- Old versions: rows of the copy that no longer exist unchanged.
            CALL afk_exec(CONCAT('INSERT INTO afk_modules.`e', v_entry, '_old` (', afk_cols(v_db, v_tbl, ''), ') SELECT ', afk_cols(v_db, v_tbl, 'b'),
                                 ' FROM afk_modules.`', v_copy, '` b WHERE NOT EXISTS (SELECT 1 FROM ', afk_q(v_db, v_tbl), ' a WHERE ',
                                 afk_match(v_db, v_tbl, 'a', 'b'), ')'));
            SET v_old = @afk_rows;
            -- New versions: rows of the table that did not exist like this before.
            CALL afk_exec(CONCAT('INSERT INTO afk_modules.`e', v_entry, '_new` (', afk_cols(v_db, v_tbl, ''), ') SELECT ', afk_cols(v_db, v_tbl, 'a'),
                                 ' FROM ', afk_q(v_db, v_tbl), ' a WHERE NOT EXISTS (SELECT 1 FROM afk_modules.`', v_copy, '` b WHERE ',
                                 afk_match(v_db, v_tbl, 'a', 'b'), ')'));
            SET v_new = @afk_rows;
            CALL afk_exec(CONCAT('DROP TABLE afk_modules.`', v_copy, '`'));
            IF v_old = 0 AND v_new = 0 THEN
                CALL afk_exec(CONCAT('DROP TABLE afk_modules.`e', v_entry, '_old`'));
                CALL afk_exec(CONCAT('DROP TABLE afk_modules.`e', v_entry, '_new`'));
                DELETE FROM entries WHERE id = v_entry;
            ELSE
                UPDATE entries SET old_rows = v_old, new_rows = v_new WHERE id = v_entry;
            END IF;
        END IF;
    END LOOP;
    CLOSE cur;

    -- Tables that changed although the SQL files did not name them (for example through a
    -- stored procedure). They have no copy, so these changes can only be undone with a backup.
    INSERT INTO entries (batch, db, tbl, kind)
        SELECT p_batch, t.table_schema, t.table_name, 'untracked' FROM information_schema.tables t
         WHERE t.table_schema IN ('acore_auth', 'acore_characters', 'acore_world', 'acore_playerbots') AND t.table_type = 'BASE TABLE'
           AND t.update_time >= v_created AND t.table_name <> 'updates'
           AND NOT EXISTS (SELECT 1 FROM watch w WHERE w.db = t.table_schema AND w.tbl = t.table_name)
           AND NOT EXISTS (SELECT 1 FROM entries e WHERE e.batch = p_batch AND e.db = t.table_schema AND e.tbl = t.table_name);

    UPDATE entries e SET new_rows = (SELECT table_rows FROM information_schema.tables t WHERE t.table_schema = e.db AND t.table_name = e.tbl)
     WHERE e.batch = p_batch AND e.kind = 'created';
    DELETE FROM watch;
    DELETE FROM tables_before;
    UPDATE batches SET done = 1 WHERE id = p_batch;
END //

CREATE PROCEDURE afk_undo(IN p_module VARCHAR(100))
BEGIN
    DECLARE v_batch, v_entry, v_old, v_new, v_n INT;
    DECLARE v_db, v_tbl VARCHAR(64);
    DECLARE v_kind VARCHAR(16);
    DECLARE v_bdb VARCHAR(64);
    DECLARE v_files MEDIUMTEXT;
    DECLARE v_done INT DEFAULT 0;
    DECLARE cur CURSOR FOR
        SELECT e.id, e.batch, e.db, e.tbl, e.kind, e.old_rows, e.new_rows FROM entries e JOIN batches b ON b.id = e.batch
         WHERE b.module = p_module ORDER BY e.batch DESC, e.id DESC;
    DECLARE bcur CURSOR FOR SELECT id, db, files FROM batches WHERE module = p_module ORDER BY id DESC;
    DECLARE CONTINUE HANDLER FOR NOT FOUND SET v_done = 1;
    SET SESSION group_concat_max_len = 16777216;

    DROP TEMPORARY TABLE IF EXISTS afk_report;
    CREATE TEMPORARY TABLE afk_report (id INT AUTO_INCREMENT PRIMARY KEY, kind VARCHAR(16), tbl VARCHAR(140), msg TEXT);

    OPEN cur;
    undo_loop: LOOP
        FETCH cur INTO v_entry, v_batch, v_db, v_tbl, v_kind, v_old, v_new;
        IF v_done THEN LEAVE undo_loop; END IF;
        SET @afk_exists = EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = v_db AND table_name = v_tbl);

        IF v_kind = 'rows' THEN
            IF NOT @afk_exists THEN
                INSERT INTO afk_report (kind, tbl, msg) VALUES ('skipped', CONCAT(v_db, '.', v_tbl), 'the table no longer exists');
            ELSEIF BINARY IFNULL(afk_cols(v_db, v_tbl, ''), '') <> BINARY IFNULL(afk_cols('afk_modules', CONCAT('e', v_entry, '_new'), ''), '') THEN
                INSERT INTO afk_report (kind, tbl, msg) VALUES ('skipped', CONCAT(v_db, '.', v_tbl), 'the table structure changed since; restore a backup to undo these rows');
            ELSE
                -- Rows the module added or changed and that nobody touched since.
                CALL afk_exec(CONCAT('SELECT COUNT(*) INTO @afk_n FROM afk_modules.`e', v_entry, '_new` n WHERE EXISTS (SELECT 1 FROM ', afk_q(v_db, v_tbl),
                                     ' t WHERE ', afk_keymatch(v_db, v_tbl, 't', 'n'), ') AND NOT EXISTS (SELECT 1 FROM ', afk_q(v_db, v_tbl),
                                     ' t WHERE ', afk_match(v_db, v_tbl, 't', 'n'), ')'));
                SET v_n = @afk_n;
                SET @afk_kept_new = @afk_n;
                CALL afk_exec(CONCAT('DELETE t FROM ', afk_q(v_db, v_tbl), ' t JOIN afk_modules.`e', v_entry, '_new` n ON ', afk_match(v_db, v_tbl, 't', 'n')));
                SET @afk_deleted = @afk_rows;
                -- Rows the module removed or changed come back unless their key is taken again.
                CALL afk_exec(CONCAT('SELECT COUNT(*) INTO @afk_n FROM afk_modules.`e', v_entry, '_old` o WHERE EXISTS (SELECT 1 FROM ', afk_q(v_db, v_tbl),
                                     ' t WHERE ', afk_keymatch(v_db, v_tbl, 't', 'o'), ')'));
                SET v_n = v_n + @afk_n;
                CALL afk_exec(CONCAT('SELECT COUNT(*) INTO @afk_n FROM afk_modules.`e', v_entry, '_old`'));
                SET @afk_wanted = @afk_n - (v_n - IFNULL(@afk_kept_new, 0));
                -- IGNORE: a row whose other unique key is taken again is kept out (and reported) instead of stopping the undo.
                CALL afk_exec(CONCAT('INSERT IGNORE INTO ', afk_q(v_db, v_tbl), ' (', afk_cols(v_db, v_tbl, ''), ') SELECT ', afk_cols(v_db, v_tbl, 'o'),
                                     ' FROM afk_modules.`e', v_entry, '_old` o WHERE NOT EXISTS (SELECT 1 FROM ', afk_q(v_db, v_tbl),
                                     ' t WHERE ', afk_keymatch(v_db, v_tbl, 't', 'o'), ')'));
                SET @afk_restored = @afk_rows;
                SET v_n = v_n + GREATEST(0, @afk_wanted - @afk_restored);
                -- Ids the module used are free again (the counter goes back to the highest id left).
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = v_db AND table_name = v_tbl AND extra LIKE '%auto_increment%') THEN
                    CALL afk_exec(CONCAT('ALTER TABLE ', afk_q(v_db, v_tbl), ' AUTO_INCREMENT = 1'));
                END IF;
                INSERT INTO afk_report (kind, tbl, msg)
                    VALUES ('rows', CONCAT(v_db, '.', v_tbl), CONCAT(@afk_deleted, ' row(s) removed, ', @afk_restored, ' row(s) restored'));
                IF v_n > 0 THEN
                    INSERT INTO afk_report (kind, tbl, msg)
                        VALUES ('kept', CONCAT(v_db, '.', v_tbl), CONCAT(v_n, ' row(s) were changed again after the module was installed and were left as they are'));
                END IF;
            END IF;
        ELSEIF v_kind = 'created' THEN
            IF @afk_exists THEN
                CALL afk_exec(CONCAT('SELECT COUNT(*) INTO @afk_n FROM ', afk_q(v_db, v_tbl)));
                CALL afk_exec(CONCAT('DROP TABLE ', afk_q(v_db, v_tbl)));
                INSERT INTO afk_report (kind, tbl, msg) VALUES ('dropped', CONCAT(v_db, '.', v_tbl), CONCAT('table of the module removed (', @afk_n, ' row(s))'));
            END IF;
        ELSEIF v_kind = 'dropped' THEN
            IF @afk_exists THEN
                INSERT INTO afk_report (kind, tbl, msg) VALUES ('skipped', CONCAT(v_db, '.', v_tbl), 'the module had deleted this table, but it exists again; left as it is');
            ELSE
                CALL afk_exec(CONCAT('CREATE TABLE ', afk_q(v_db, v_tbl), ' LIKE afk_modules.`e', v_entry, '_old`'));
                CALL afk_exec(CONCAT('INSERT IGNORE INTO ', afk_q(v_db, v_tbl), ' (', afk_cols('afk_modules', CONCAT('e', v_entry, '_old'), ''), ') SELECT ',
                                     afk_cols('afk_modules', CONCAT('e', v_entry, '_old'), ''), ' FROM afk_modules.`e', v_entry, '_old`'));
                INSERT INTO afk_report (kind, tbl, msg) VALUES ('restored', CONCAT(v_db, '.', v_tbl), CONCAT('table the module had deleted restored (', @afk_rows, ' row(s))'));
            END IF;
        ELSEIF v_kind = 'altered' THEN
            INSERT INTO afk_report (kind, tbl, msg) VALUES ('manual', CONCAT(v_db, '.', v_tbl), 'the module changed the structure of this table; only a backup can undo that');
        ELSE
            INSERT INTO afk_report (kind, tbl, msg) VALUES ('manual', CONCAT(v_db, '.', v_tbl), 'changed by the module without a record; only a backup can undo that');
        END IF;
    END LOOP;
    CLOSE cur;

    -- The module's files leave the updates tables, so a later reinstall applies them again.
    SET v_done = 0;
    OPEN bcur;
    batch_loop: LOOP
        FETCH bcur INTO v_batch, v_bdb, v_files;
        IF v_done THEN LEAVE batch_loop; END IF;
        IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = v_bdb AND table_name = 'updates') THEN
            CALL afk_exec(CONCAT('DELETE FROM ', afk_q(v_bdb, 'updates'), ' WHERE state = ''MODULE'' AND FIND_IN_SET(name, ', QUOTE(v_files), ') > 0'));
        END IF;
    END LOOP;
    CLOSE bcur;

    -- The records go only now, so an undo that stopped half-way can simply run again.
    SET v_done = 0;
    OPEN cur;
    drop_loop: LOOP
        FETCH cur INTO v_entry, v_batch, v_db, v_tbl, v_kind, v_old, v_new;
        IF v_done THEN LEAVE drop_loop; END IF;
        CALL afk_exec(CONCAT('DROP TABLE IF EXISTS afk_modules.`e', v_entry, '_old`'));
        CALL afk_exec(CONCAT('DROP TABLE IF EXISTS afk_modules.`e', v_entry, '_new`'));
    END LOOP;
    CLOSE cur;
    DELETE e FROM entries e JOIN batches b ON b.id = e.batch WHERE b.module = p_module;
    DELETE FROM batches WHERE module = p_module;
    DELETE FROM modules WHERE name = p_module;
    SELECT 'REPORT', kind, tbl, msg FROM afk_report ORDER BY id;
    DROP TEMPORARY TABLE afk_report;
END //

DELIMITER ;
