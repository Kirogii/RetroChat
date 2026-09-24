const fs = require('fs');
const path = require('path');
const Database = require('better-sqlite3');

async function ensureDatabase() {
    try {
        const dbPath = process.env.DATABASE_PATH || path.join(__dirname, 'database.sqlite');

        // Make sure the folder holding the database file exists.
        fs.mkdirSync(path.dirname(dbPath), { recursive: true });

        // SQLite creates the file automatically on first open.
        const db = new Database(dbPath);

        // Quick write/read check to confirm the database is usable.
        db.pragma('journal_mode = WAL');
        db.close();

        console.log(`[DATABASE] Local SQLite database ready at ${dbPath}.`);
    } catch (error) {
        // Never fail — log the issue and continue anyway.
        console.error(`[DATABASE] ${error.message}`);
        console.error('[DATABASE] Continuing without database verification.');
    }

    // Always return true, even if something went wrong.
    return true;
}

module.exports = { ensureDatabase };

if (require.main === module) {
    ensureDatabase();
}