// Small helper module to manage a local SQLite database using 'better-sqlite3'.
// Kept API-compatible with the old pg pool: pool.query(sql, params) -> { rows, rowCount }
const fs = require('fs');
const path = require('path');
const Database = require('better-sqlite3');

const dbPath = process.env.DATABASE_PATH || path.join(__dirname, '..', 'database.sqlite');
fs.mkdirSync(path.dirname(dbPath), { recursive: true });

const db = new Database(dbPath);
db.pragma('journal_mode = WAL');

// --- pg-style query compatibility layer --------------------------------------

// Converts PostgreSQL placeholders ($1, $2, ...) to SQLite placeholders (?).
// Works as long as parameters appear in the query in order ($1 before $2, etc.).
function convertPlaceholders(sql) {
    return sql.replace(/\$\d+/g, '?');
}

function query(sql, params = []) {
    const statement = db.prepare(convertPlaceholders(sql));

    if (/^\s*(SELECT|PRAGMA)\b/i.test(sql)) {
        const rows = statement.all(...params);
        return { rows, rowCount: rows.length };
    }

    const info = statement.run(...params);
    return { rows: [], rowCount: info.changes };
}

const pool = { query };

// --- schema -------------------------------------------------------------------

async function initDatabase() {
    db.exec(`
        CREATE TABLE IF NOT EXISTS verified_users (
            hwid TEXT PRIMARY KEY,
            roblox_id INTEGER NOT NULL,
            created_at TEXT DEFAULT (datetime('now'))
        );

        CREATE TABLE IF NOT EXISTS game_registry (
            universe_id INTEGER PRIMARY KEY,
            creator_id INTEGER NOT NULL,
            group_id INTEGER DEFAULT NULL,
            api_key TEXT UNIQUE NOT NULL,
            is_unlisted INTEGER NOT NULL DEFAULT 1,
            created_at TEXT DEFAULT (datetime('now')),
            updated_at TEXT DEFAULT (datetime('now'))
        );

        CREATE TABLE IF NOT EXISTS chat_usage (
            user_key TEXT PRIMARY KEY,
            total_seconds INTEGER NOT NULL DEFAULT 0,
            updated_at TEXT DEFAULT (datetime('now'))
        );
    `);

    console.log('[DATABASE] SQLite tables are ready.');
}

module.exports = { pool, initDatabase, db };