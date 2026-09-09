import sqlite3
from pathlib import Path
from contextlib import contextmanager
from assets import AssetTrackingMixin

DB_PATH = Path(__file__).with_name("main.db")


class AssetSystem(AssetTrackingMixin):
    """SQLite-backed Asset Tracking ERP."""

    def __init__(self):
        self._create_tables()

    def _connect(self):
        conn = sqlite3.connect(DB_PATH)
        conn.row_factory = sqlite3.Row
        conn.execute("PRAGMA foreign_keys = ON")
        return conn

    @contextmanager
    def _db(self):
        conn = self._connect()
        try:
            yield conn
            conn.commit()
        finally:
            conn.close()

    def _create_tables(self):
        with self._db() as conn:
            conn.execute(
                """CREATE TABLE IF NOT EXISTS assets (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    asset_tag TEXT NOT NULL UNIQUE,
                    name TEXT NOT NULL,
                    category TEXT NOT NULL,
                    description TEXT DEFAULT '',
                    serial_no TEXT DEFAULT '',
                    brand TEXT DEFAULT '',
                    model TEXT DEFAULT '',
                    purchase_date TEXT DEFAULT '',
                    purchase_cost REAL NOT NULL DEFAULT 0,
                    supplier TEXT DEFAULT '',
                    location TEXT DEFAULT '',
                    assigned_to TEXT DEFAULT '',
                    status TEXT NOT NULL DEFAULT 'Available',
                    condition TEXT DEFAULT 'Good',
                    warranty_expiry TEXT DEFAULT '',
                    notes TEXT DEFAULT '',
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                )"""
            )
            conn.execute(
                """CREATE TABLE IF NOT EXISTS asset_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    asset_id INTEGER NOT NULL,
                    event_type TEXT NOT NULL,
                    details TEXT DEFAULT '',
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY(asset_id) REFERENCES assets(id) ON DELETE CASCADE
                )"""
            )
            conn.execute(
                """CREATE TABLE IF NOT EXISTS departments (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    code TEXT NOT NULL UNIQUE,
                    name TEXT NOT NULL,
                    manager TEXT DEFAULT '',
                    notes TEXT DEFAULT ''
                )"""
            )
            conn.execute(
                """CREATE TABLE IF NOT EXISTS locations (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    code TEXT NOT NULL UNIQUE,
                    name TEXT NOT NULL,
                    building TEXT DEFAULT '',
                    floor TEXT DEFAULT '',
                    department_id INTEGER,
                    notes TEXT DEFAULT '',
                    FOREIGN KEY(department_id) REFERENCES departments(id)
                )"""
            )
            conn.execute(
                """CREATE TABLE IF NOT EXISTS employees (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    emp_code TEXT NOT NULL UNIQUE,
                    name TEXT NOT NULL,
                    department_id INTEGER,
                    designation TEXT DEFAULT '',
                    phone TEXT DEFAULT '',
                    status TEXT NOT NULL DEFAULT 'Active',
                    FOREIGN KEY(department_id) REFERENCES departments(id)
                )"""
            )
            conn.execute(
                """CREATE TABLE IF NOT EXISTS asset_categories (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL UNIQUE
                )"""
            )
            conn.execute(
                """CREATE TABLE IF NOT EXISTS asset_movements (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    asset_id INTEGER NOT NULL,
                    movement_type TEXT NOT NULL,
                    from_location_id INTEGER,
                    to_location_id INTEGER,
                    from_department_id INTEGER,
                    to_department_id INTEGER,
                    from_employee_id INTEGER,
                    to_employee_id INTEGER,
                    reason TEXT DEFAULT '',
                    moved_by TEXT DEFAULT '',
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY(asset_id) REFERENCES assets(id) ON DELETE CASCADE
                )"""
            )
            conn.execute(
                """CREATE TABLE IF NOT EXISTS verification_sessions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    location_id INTEGER,
                    status TEXT NOT NULL DEFAULT 'Open',
                    notes TEXT DEFAULT '',
                    started_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    completed_at TIMESTAMP
                )"""
            )
            conn.execute(
                """CREATE TABLE IF NOT EXISTS verification_items (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    session_id INTEGER NOT NULL,
                    asset_id INTEGER NOT NULL,
                    result TEXT NOT NULL,
                    notes TEXT DEFAULT '',
                    checked_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY(session_id) REFERENCES verification_sessions(id) ON DELETE CASCADE,
                    FOREIGN KEY(asset_id) REFERENCES assets(id) ON DELETE CASCADE,
                    UNIQUE(session_id, asset_id)
                )"""
            )
            self._ensure_asset_columns(conn)
            self._seed_asset_masters(conn)

    @staticmethod
    def _number(value, default=0):
        try:
            return float(value)
        except (TypeError, ValueError):
            return default
