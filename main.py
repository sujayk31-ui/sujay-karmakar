import sqlite3
import uuid
from pathlib import Path
from contextlib import contextmanager

DB_PATH = Path(__file__).with_name("main.db")


class InventorySystem:
    """SQLite-backed operations for TADDY ERP."""

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
                """CREATE TABLE IF NOT EXISTS materials (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    item_name TEXT NOT NULL,
                    diameter TEXT DEFAULT '',
                    category TEXT NOT NULL,
                    length TEXT DEFAULT '',
                    color TEXT DEFAULT '',
                    quantity REAL NOT NULL DEFAULT 0,
                    supplier TEXT DEFAULT '',
                    unit TEXT DEFAULT 'pcs',
                    batch_no TEXT DEFAULT ''
                )"""
            )
            conn.execute(
                """CREATE TABLE IF NOT EXISTS stitching_jobs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    worker TEXT NOT NULL, category TEXT NOT NULL,
                    size TEXT NOT NULL, sent_quantity INTEGER NOT NULL,
                    received_quantity INTEGER NOT NULL DEFAULT 0,
                    due_quantity INTEGER NOT NULL DEFAULT 0,
                    status TEXT NOT NULL DEFAULT 'In progress',
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                )"""
            )
            conn.execute(
                """CREATE TABLE IF NOT EXISTS ready_stock (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    product_name TEXT NOT NULL, category TEXT NOT NULL,
                    size TEXT NOT NULL, quantity INTEGER NOT NULL DEFAULT 0,
                    price REAL NOT NULL DEFAULT 0
                )"""
            )
            conn.execute(
                """CREATE TABLE IF NOT EXISTS sales (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    customer_name TEXT NOT NULL, phone TEXT DEFAULT '',
                    address TEXT DEFAULT '',
                    transaction_id TEXT,
                    product_name TEXT NOT NULL, category TEXT NOT NULL,
                    size TEXT NOT NULL, quantity INTEGER NOT NULL,
                    unit_price REAL NOT NULL, total REAL NOT NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                )"""
            )
            sales_columns = {row["name"] for row in conn.execute("PRAGMA table_info(sales)")}
            if "address" not in sales_columns:
                conn.execute("ALTER TABLE sales ADD COLUMN address TEXT DEFAULT ''")
            if "transaction_id" not in sales_columns:
                conn.execute("ALTER TABLE sales ADD COLUMN transaction_id TEXT")
            conn.execute(
                """CREATE TABLE IF NOT EXISTS invoices (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    sale_id INTEGER NOT NULL, invoice_no TEXT NOT NULL UNIQUE,
                    customer_name TEXT NOT NULL, total REAL NOT NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY(sale_id) REFERENCES sales(id)
                )"""
            )

    @staticmethod
    def _number(value, default=0):
        try:
            return float(value)
        except (TypeError, ValueError):
            return default

    def add_raw_material(self, item_name, diameter="", category="General",
                         length="", color="", quantity=0, supplier="",
                         unit="pcs", batch_no=""):
        quantity = self._number(quantity)
        if quantity < 0 or not item_name.strip():
            raise ValueError("Material name is required and quantity cannot be negative.")
        with self._db() as conn:
            conn.execute(
                """INSERT INTO materials
                (item_name, diameter, category, length, color, quantity, supplier, unit, batch_no)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)""",
                (item_name.strip(), diameter, category, length, color, quantity,
                 supplier, unit or "pcs", batch_no),
            )

    def get_all_materials(self):
        with self._db() as conn:
            rows = conn.execute("SELECT * FROM materials ORDER BY id DESC").fetchall()
        materials = [dict(row) for row in rows]
        for material in materials:
            material["quantity"] = self._number(material["quantity"])
        return materials

    def delete_material(self, material_id):
        with self._db() as conn:
            cursor = conn.execute("DELETE FROM materials WHERE id = ?", (material_id,))
            if cursor.rowcount == 0:
                raise ValueError("Material not found.")

    def adjust_material(self, material_id, amount):
        amount = self._number(amount)
        if amount == 0:
            raise ValueError("Adjustment must be greater than zero.")
        with self._db() as conn:
            row = conn.execute("SELECT quantity FROM materials WHERE id = ?", (material_id,)).fetchone()
            current_quantity = self._number(row["quantity"]) if row else None
            if current_quantity is None or current_quantity + amount < 0:
                raise ValueError("Material not found or stock cannot become negative.")
            conn.execute("UPDATE materials SET quantity = quantity + ? WHERE id = ?",
                         (amount, material_id))

    def send_to_stitching(self, worker, category, size, quantity):
        quantity = int(self._number(quantity))
        if quantity <= 0 or not worker.strip():
            raise ValueError("Worker and a positive quantity are required.")
        with self._db() as conn:
            conn.execute(
                """INSERT INTO stitching_jobs
                (worker, category, size, sent_quantity, due_quantity)
                VALUES (?, ?, ?, ?, ?)""",
                (worker.strip(), category, size, quantity, quantity),
            )

    def receive_from_stitching(self, job_id, quantity):
        quantity = int(self._number(quantity))
        with self._db() as conn:
            job = conn.execute("SELECT * FROM stitching_jobs WHERE id = ?", (job_id,)).fetchone()
            if not job or quantity <= 0 or job["received_quantity"] + quantity > job["sent_quantity"]:
                raise ValueError("Received quantity exceeds the outstanding stitching due.")
            received = job["received_quantity"] + quantity
            status = "Complete" if received == job["sent_quantity"] else "Partial"
            conn.execute(
                """UPDATE stitching_jobs SET received_quantity=?, due_quantity=?, status=?
                WHERE id=?""",
                (received, job["sent_quantity"] - received, status, job_id),
            )
            existing = conn.execute(
                "SELECT id FROM ready_stock WHERE product_name=? AND category=? AND size=?",
                (job["category"] + " finished goods", job["category"], job["size"]),
            ).fetchone()
            if existing:
                conn.execute("UPDATE ready_stock SET quantity=quantity+? WHERE id=?",
                             (quantity, existing["id"]))
            else:
                conn.execute(
                    """INSERT INTO ready_stock (product_name, category, size, quantity)
                    VALUES (?, ?, ?, ?)""",
                    (job["category"] + " finished goods", job["category"], job["size"], quantity),
                )

    def get_stitching_jobs(self):
        with self._db() as conn:
            return [dict(r) for r in conn.execute(
                "SELECT * FROM stitching_jobs ORDER BY id DESC").fetchall()]

    def delete_stitching_job(self, job_id):
        with self._db() as conn:
            cursor = conn.execute("DELETE FROM stitching_jobs WHERE id = ?", (job_id,))
            if cursor.rowcount == 0:
                raise ValueError("Stitching job not found.")

    def add_ready_stock(self, product_name, category, size, quantity, price=0):
        quantity, price = int(self._number(quantity)), self._number(price)
        if quantity < 0 or not product_name.strip():
            raise ValueError("Product name is required and quantity cannot be negative.")
        with self._db() as conn:
            conn.execute(
                """INSERT INTO ready_stock (product_name, category, size, quantity, price)
                VALUES (?, ?, ?, ?, ?)""",
                (product_name.strip(), category, size, quantity, price),
            )

    def get_ready_stock(self):
        with self._db() as conn:
            return [dict(r) for r in conn.execute(
                "SELECT * FROM ready_stock ORDER BY category, product_name, size").fetchall()]

    def delete_ready_stock(self, stock_id):
        with self._db() as conn:
            used = conn.execute("SELECT 1 FROM sales WHERE rowid IN "
                                "(SELECT rowid FROM sales) AND 0", ()).fetchone()
            if used:
                raise ValueError("This stock cannot be deleted.")
            cursor = conn.execute("DELETE FROM ready_stock WHERE id = ?", (stock_id,))
            if cursor.rowcount == 0:
                raise ValueError("Ready stock item not found.")

    def record_sale(self, customer_name, phone, address, items):
        if not customer_name.strip() or not items:
            raise ValueError("Customer and at least one product are required.")
        transaction_id = uuid.uuid4().hex
        with self._db() as conn:
            lines = []
            for item in items:
                stock = conn.execute("SELECT * FROM ready_stock WHERE id=?", (item["stock_id"],)).fetchone()
                quantity = int(self._number(item["quantity"]))
                unit_price = self._number(item["unit_price"], -1)
                if not stock or quantity <= 0 or stock["quantity"] < quantity or unit_price < 0:
                    raise ValueError("Invalid quantity, unit price, or insufficient ready stock.")
                total = quantity * unit_price
                conn.execute("UPDATE ready_stock SET quantity=quantity-? WHERE id=?",
                             (quantity, item["stock_id"]))
                sale = conn.execute(
                    """INSERT INTO sales
                    (customer_name, phone, address, transaction_id, product_name, category, size,
                     quantity, unit_price, total)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
                    (customer_name.strip(), phone, address, transaction_id, stock["product_name"],
                     stock["category"], stock["size"], quantity, unit_price, total),
                )
                lines.append((sale.lastrowid, total))
            sale_id = lines[0][0]
            grand_total = sum(line[1] for line in lines)
            invoice_no = "TADDY-" + str(sale_id).zfill(5)
            conn.execute(
                "INSERT INTO invoices (sale_id, invoice_no, customer_name, total) VALUES (?, ?, ?, ?)",
                (sale_id, invoice_no, customer_name.strip(), grand_total),
            )
            return invoice_no

    def delete_sale(self, sale_id):
        with self._db() as conn:
            sale = conn.execute("SELECT * FROM sales WHERE id = ?", (sale_id,)).fetchone()
            if not sale:
                raise ValueError("Customer sale not found.")
            transaction_id = sale["transaction_id"] or str(sale["id"])
            lines = conn.execute(
                "SELECT * FROM sales WHERE transaction_id=? OR (transaction_id IS NULL AND id=?)",
                (transaction_id, sale_id),
            ).fetchall()
            for line in lines:
                stock = conn.execute(
                    "SELECT id FROM ready_stock WHERE product_name=? AND category=? AND size=?",
                    (line["product_name"], line["category"], line["size"]),
                ).fetchone()
                if stock:
                    conn.execute("UPDATE ready_stock SET quantity=quantity+? WHERE id=?",
                                 (line["quantity"], stock["id"]))
            line_ids = [line["id"] for line in lines]
            placeholders = ",".join("?" for _ in line_ids)
            conn.execute("DELETE FROM invoices WHERE sale_id IN (" + placeholders + ")", line_ids)
            if sale["transaction_id"]:
                conn.execute("DELETE FROM sales WHERE transaction_id = ?", (transaction_id,))
            else:
                conn.execute("DELETE FROM sales WHERE id = ?", (sale_id,))

    def get_sales(self):
        with self._db() as conn:
            return [dict(r) for r in conn.execute(
                "SELECT * FROM sales ORDER BY id DESC").fetchall()]

    def get_invoices(self):
        with self._db() as conn:
            return [dict(r) for r in conn.execute(
                "SELECT * FROM invoices ORDER BY id DESC").fetchall()]

    def get_invoice(self, invoice_no):
        with self._db() as conn:
            row = conn.execute(
                """SELECT i.*, s.product_name, s.category, s.size, s.quantity,
                          s.unit_price, s.phone, s.address, s.transaction_id,
                          s.created_at AS sale_date
                   FROM invoices i JOIN sales s ON s.id = i.sale_id
                   WHERE i.invoice_no = ?""",
                (invoice_no,),
            ).fetchone()
            if not row:
                return None
            invoice = dict(row)
            if invoice["transaction_id"]:
                invoice["lines"] = [
                    dict(line) for line in conn.execute(
                        "SELECT product_name, category, size, quantity, unit_price, total "
                        "FROM sales WHERE transaction_id=? ORDER BY id",
                        (invoice["transaction_id"],),
                    ).fetchall()
                ]
            else:
                invoice["lines"] = [invoice]
        return invoice
