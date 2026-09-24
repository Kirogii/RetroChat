const Database = require('better-sqlite3');
const path = require('path');
const Env = require('../config/env');

const DB_PATH = Env.DB_PATH || path.join(__dirname, '../../data/chat.db');

// Ensure directory exists
const fs = require('fs');
const dir = path.dirname(DB_PATH);
if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });

const db = new Database(DB_PATH);
db.pragma('journal_mode = WAL');
db.pragma('foreign_keys = ON');

function initDatabase() {
    db.exec(`
        CREATE TABLE IF NOT EXISTS verified_users (
            hwid TEXT PRIMARY KEY,
            roblox_id INTEGER NOT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS game_registry (
            universe_id INTEGER PRIMARY KEY,
            api_key TEXT NOT NULL,
            is_unlisted INTEGER DEFAULT 0,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS chat_usage (
            user_key TEXT PRIMARY KEY,
            total_seconds INTEGER DEFAULT 0,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS verification_codes (
            code TEXT PRIMARY KEY,
            roblox_id INTEGER NOT NULL,
            hwid TEXT,
            expires_at DATETIME NOT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
        );
    `);
    console.log('[SQLite] Database initialized at', DB_PATH);
}

module.exports = { db, initDatabase };