import csv
import io

import qrcode
from qrcode.image.svg import SvgPathImage


class AssetTrackingMixin:
    """Asset register, masters, barcodes, movement, verification, and reports."""

    DEFAULT_ASSET_CATEGORIES = (
        "Machinery", "Tools", "Furniture", "IT Equipment", "Vehicle", "Electrical", "Other",
    )
    ASSET_STATUSES = ("Available", "In Use", "Under Maintenance", "Retired", "Lost")
    ASSET_CONDITIONS = ("New", "Good", "Fair", "Poor")
    BARTENDER_FIELDS = (
        "AssetTag", "QRData", "AssetName", "SerialNo", "Category", "Brand", "Model",
        "LocationCode", "LocationName", "DepartmentCode", "DepartmentName",
        "EmployeeCode", "EmployeeName", "Status", "Condition", "PurchaseDate",
        "PurchaseCost", "Supplier",
    )

    @property
    def ASSET_CATEGORIES(self):
        names = tuple(row["name"] for row in self.get_asset_categories())
        return names or self.DEFAULT_ASSET_CATEGORIES

    def _ensure_asset_columns(self, conn):
        columns = {row["name"] for row in conn.execute("PRAGMA table_info(assets)")}
        extras = (
            ("barcode", "TEXT DEFAULT ''"),
            ("department_id", "INTEGER"),
            ("location_id", "INTEGER"),
            ("employee_id", "INTEGER"),
        )
        for name, ddl in extras:
            if name not in columns:
                conn.execute("ALTER TABLE assets ADD COLUMN %s %s" % (name, ddl))
        conn.execute(
            "UPDATE assets SET barcode = asset_tag WHERE barcode IS NULL OR barcode = ''"
        )

    def _seed_asset_masters(self, conn):
        count = conn.execute("SELECT COUNT(*) AS n FROM asset_categories").fetchone()["n"]
        if not count:
            conn.executemany(
                "INSERT INTO asset_categories (name) VALUES (?)",
                [(name,) for name in self.DEFAULT_ASSET_CATEGORIES],
            )
        if not conn.execute("SELECT COUNT(*) AS n FROM departments").fetchone()["n"]:
            conn.executemany(
                "INSERT INTO departments (code, name, manager) VALUES (?, ?, ?)",
                [
                    ("PROD", "Production", ""),
                    ("ADMIN", "Administration", ""),
                    ("STORES", "Stores", ""),
                ],
            )
        if not conn.execute("SELECT COUNT(*) AS n FROM locations").fetchone()["n"]:
            conn.executemany(
                "INSERT INTO locations (code, name, building, floor) VALUES (?, ?, ?, ?)",
                [
                    ("FL1", "Floor 1", "Factory", "1"),
                    ("CUT", "Cutting room", "Factory", "1"),
                    ("PACK", "Packing room", "Factory", "1"),
                    ("OFF", "Office", "Admin block", "G"),
                ],
            )

    def _log_asset_event(self, conn, asset_id, event_type, details=""):
        conn.execute(
            "INSERT INTO asset_events (asset_id, event_type, details) VALUES (?, ?, ?)",
            (asset_id, event_type, details),
        )

    def _next_asset_tag(self, conn):
        row = conn.execute("SELECT MAX(id) AS n FROM assets").fetchone()
        return "AST-%05d" % ((row["n"] or 0) + 1)

    def _optional_id(self, value):
        text = str(value or "").strip()
        if not text:
            return None
        try:
            return int(text)
        except (TypeError, ValueError):
            raise ValueError("Invalid linked record.")

    def _row_name(self, conn, table, record_id):
        if not record_id:
            return ""
        allowed = {"departments", "locations", "employees", "assets"}
        if table not in allowed:
            return ""
        row = conn.execute("SELECT name FROM %s WHERE id=?" % table, (record_id,)).fetchone()
        return row["name"] if row else ""

    def _require_asset(self, conn, asset_id):
        asset = conn.execute("SELECT * FROM assets WHERE id=?", (asset_id,)).fetchone()
        if not asset:
            raise ValueError("Asset not found.")
        return dict(asset)

    def _asset_select(self):
        return """
            SELECT a.*,
                   loc.name AS location_name, loc.code AS location_code,
                   dep.name AS department_name, dep.code AS department_code,
                   emp.name AS employee_name, emp.emp_code AS employee_code
            FROM assets a
            LEFT JOIN locations loc ON loc.id = a.location_id
            LEFT JOIN departments dep ON dep.id = a.department_id
            LEFT JOIN employees emp ON emp.id = a.employee_id
        """

    def _hydrate_asset(self, row):
        asset = dict(row)
        asset["display_location"] = asset.get("location_name") or asset.get("location") or ""
        asset["display_department"] = asset.get("department_name") or ""
        asset["display_employee"] = asset.get("employee_name") or asset.get("assigned_to") or ""
        asset["barcode"] = asset.get("barcode") or asset.get("asset_tag") or ""
        asset["qr_payload"] = self.bartender_qr_payload(asset)
        return asset

    def _sync_labels(self, conn, location_id=None, department_id=None, employee_id=None):
        location = self._row_name(conn, "locations", location_id)
        assigned = self._row_name(conn, "employees", employee_id)
        return location, assigned, location_id, department_id, employee_id

    @staticmethod
    def _bt_field(value):
        text = str(value or "").replace("\r", " ").replace("\n", " ").strip()
        return text.replace(";", ",").replace("=", "-")[:80]

    def bartender_qr_payload(self, asset):
        """ASCII key=value payload BarTender 6.12 can print as a QR data source."""
        pairs = (
            ("TAG", asset.get("asset_tag")),
            ("NAME", asset.get("name")),
            ("SN", asset.get("serial_no")),
            ("CAT", asset.get("category")),
            ("LOC", asset.get("location_code") or asset.get("display_location")),
            ("DEPT", asset.get("department_code") or asset.get("display_department")),
            ("EMP", asset.get("employee_code") or asset.get("display_employee")),
        )
        return ";".join("%s=%s" % (key, self._bt_field(value)) for key, value in pairs)

    def bartender_record(self, asset):
        payload = self.bartender_qr_payload(asset)
        return {
            "AssetTag": asset.get("asset_tag") or "",
            "QRData": payload,
            "AssetName": asset.get("name") or "",
            "SerialNo": asset.get("serial_no") or "",
            "Category": asset.get("category") or "",
            "Brand": asset.get("brand") or "",
            "Model": asset.get("model") or "",
            "LocationCode": asset.get("location_code") or "",
            "LocationName": asset.get("display_location") or asset.get("location") or "",
            "DepartmentCode": asset.get("department_code") or "",
            "DepartmentName": asset.get("display_department") or "",
            "EmployeeCode": asset.get("employee_code") or "",
            "EmployeeName": asset.get("display_employee") or "",
            "Status": asset.get("status") or "",
            "Condition": asset.get("condition") or "",
            "PurchaseDate": asset.get("purchase_date") or "",
            "PurchaseCost": "%.2f" % self._number(asset.get("purchase_cost")),
            "Supplier": asset.get("supplier") or "",
        }

    def bartender_csv(self, assets=None):
        assets = assets if assets is not None else self.get_all_assets()
        buffer = io.StringIO()
        writer = csv.DictWriter(
            buffer, fieldnames=self.BARTENDER_FIELDS, lineterminator="\r\n", extrasaction="ignore"
        )
        writer.writeheader()
        for asset in assets:
            writer.writerow(self.bartender_record(asset))
        return buffer.getvalue()

    def bartender_txt(self, assets=None):
        """Tab-delimited text file for BarTender 6.12 Text File data sources."""
        assets = assets if assets is not None else self.get_all_assets()
        lines = ["\t".join(self.BARTENDER_FIELDS)]
        for asset in assets:
            record = self.bartender_record(asset)
            lines.append("\t".join(str(record[field]).replace("\t", " ") for field in self.BARTENDER_FIELDS))
        return "\r\n".join(lines) + "\r\n"

    def qr_svg(self, data):
        qr = qrcode.QRCode(
            version=None,
            error_correction=qrcode.constants.ERROR_CORRECT_M,
            box_size=5,
            border=2,
        )
        qr.add_data(data)
        qr.make(fit=True)
        image = qr.make_image(image_factory=SvgPathImage)
        buffer = io.BytesIO()
        image.save(buffer)
        return buffer.getvalue().decode("utf-8")

    def parse_scanned_tag(self, raw):
        text = (raw or "").strip()
        for part in text.split(";"):
            if part.upper().startswith("TAG="):
                return part.split("=", 1)[1].strip()
        return text

    def add_asset(self, name, category, asset_tag="", description="", serial_no="",
                  brand="", model="", purchase_date="", purchase_cost=0, supplier="",
                  location="", assigned_to="", status="Available", condition="Good",
                  warranty_expiry="", notes="", location_id=None, department_id=None,
                  employee_id=None, barcode=""):
        name, category = name.strip(), category.strip()
        if not name or not category:
            raise ValueError("Asset name and category are required.")
        status = status if status in self.ASSET_STATUSES else "Available"
        condition = condition if condition in self.ASSET_CONDITIONS else "Good"
        cost = self._number(purchase_cost)
        if cost < 0:
            raise ValueError("Purchase cost cannot be negative.")
        location_id = self._optional_id(location_id)
        department_id = self._optional_id(department_id)
        employee_id = self._optional_id(employee_id)
        with self._db() as conn:
            tag = asset_tag.strip() or self._next_asset_tag(conn)
            barcode = (barcode or tag).strip()
            existing = conn.execute("SELECT id FROM assets WHERE asset_tag=?", (tag,)).fetchone()
            if existing:
                raise ValueError("Asset tag %s is already in use." % tag)
            loc_label, emp_label, location_id, department_id, employee_id = self._sync_labels(
                conn, location_id, department_id, employee_id
            )
            location = loc_label or location.strip()
            assigned_to = emp_label or assigned_to.strip()
            if employee_id and status == "Available":
                status = "In Use"
            cursor = conn.execute(
                """INSERT INTO assets
                (asset_tag, barcode, name, category, description, serial_no, brand, model,
                 purchase_date, purchase_cost, supplier, location, assigned_to,
                 status, condition, warranty_expiry, notes, location_id, department_id, employee_id)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
                (tag, barcode, name, category, description, serial_no, brand, model,
                 purchase_date, cost, supplier, location, assigned_to,
                 status, condition, warranty_expiry, notes, location_id, department_id, employee_id),
            )
            details = "Registered %s (%s)" % (name, tag)
            if location:
                details += " at %s" % location
            if assigned_to:
                details += ", allocated to %s" % assigned_to
            self._log_asset_event(conn, cursor.lastrowid, "Registered", details)
            return tag

    def get_all_assets(self, query="", status="", location_id=None, department_id=None,
                       employee_id=None):
        sql = self._asset_select() + " WHERE 1=1"
        params = []
        if query:
            like = "%" + query + "%"
            sql += """ AND (a.asset_tag LIKE ? OR a.barcode LIKE ? OR a.name LIKE ?
                       OR a.category LIKE ? OR a.serial_no LIKE ? OR a.location LIKE ?
                       OR a.assigned_to LIKE ? OR a.brand LIKE ? OR a.model LIKE ?
                       OR loc.name LIKE ? OR dep.name LIKE ? OR emp.name LIKE ?)"""
            params.extend([like] * 12)
        if status:
            sql += " AND a.status=?"
            params.append(status)
        location_id = self._optional_id(location_id) if location_id not in (None, "") else None
        department_id = self._optional_id(department_id) if department_id not in (None, "") else None
        employee_id = self._optional_id(employee_id) if employee_id not in (None, "") else None
        if location_id:
            sql += " AND a.location_id=?"
            params.append(location_id)
        if department_id:
            sql += " AND a.department_id=?"
            params.append(department_id)
        if employee_id:
            sql += " AND a.employee_id=?"
            params.append(employee_id)
        sql += " ORDER BY a.id DESC"
        with self._db() as conn:
            return [self._hydrate_asset(row) for row in conn.execute(sql, params).fetchall()]

    def get_asset(self, asset_id):
        with self._db() as conn:
            row = conn.execute(self._asset_select() + " WHERE a.id=?", (asset_id,)).fetchone()
            if not row:
                raise ValueError("Asset not found.")
            return self._hydrate_asset(row)

    def get_asset_by_tag(self, tag):
        tag = self.parse_scanned_tag(tag)
        with self._db() as conn:
            row = conn.execute(
                self._asset_select() + " WHERE a.asset_tag=? OR a.barcode=?",
                (tag, tag),
            ).fetchone()
            if not row:
                raise ValueError("Asset tag %s was not found." % tag)
            return self._hydrate_asset(row)

    def get_asset_events(self, asset_id):
        with self._db() as conn:
            self._require_asset(conn, asset_id)
            events = [dict(row) for row in conn.execute(
                "SELECT * FROM asset_events WHERE asset_id=? ORDER BY id DESC",
                (asset_id,),
            ).fetchall()]
            movements = [dict(row) for row in conn.execute(
                """SELECT m.*, loc_from.name AS from_location, loc_to.name AS to_location,
                          dep_from.name AS from_department, dep_to.name AS to_department,
                          emp_from.name AS from_employee, emp_to.name AS to_employee
                   FROM asset_movements m
                   LEFT JOIN locations loc_from ON loc_from.id = m.from_location_id
                   LEFT JOIN locations loc_to ON loc_to.id = m.to_location_id
                   LEFT JOIN departments dep_from ON dep_from.id = m.from_department_id
                   LEFT JOIN departments dep_to ON dep_to.id = m.to_department_id
                   LEFT JOIN employees emp_from ON emp_from.id = m.from_employee_id
                   LEFT JOIN employees emp_to ON emp_to.id = m.to_employee_id
                   WHERE m.asset_id=? ORDER BY m.id DESC""",
                (asset_id,),
            ).fetchall()]
        return events, movements

    def get_asset_summary(self):
        with self._db() as conn:
            total = conn.execute("SELECT COUNT(*) AS n FROM assets").fetchone()["n"]
            rows = conn.execute(
                "SELECT status, COUNT(*) AS n FROM assets GROUP BY status"
            ).fetchall()
            allocated = conn.execute(
                "SELECT COUNT(*) AS n FROM assets WHERE employee_id IS NOT NULL"
            ).fetchone()["n"]
            open_verifications = conn.execute(
                "SELECT COUNT(*) AS n FROM verification_sessions WHERE status='Open'"
            ).fetchone()["n"]
        counts = {status: 0 for status in self.ASSET_STATUSES}
        for row in rows:
            counts[row["status"]] = row["n"]
        counts["Total"] = total
        counts["Allocated"] = allocated
        counts["Open verifications"] = open_verifications
        return counts

    def update_asset(self, asset_id, **fields):
        allowed = (
            "asset_tag", "barcode", "name", "category", "description", "serial_no", "brand",
            "model", "purchase_date", "purchase_cost", "supplier", "location",
            "assigned_to", "status", "condition", "warranty_expiry", "notes",
            "location_id", "department_id", "employee_id",
        )
        with self._db() as conn:
            current = self._require_asset(conn, asset_id)
            updates = {}
            for key in allowed:
                if key not in fields:
                    continue
                value = fields[key]
                if key == "purchase_cost":
                    value = self._number(value)
                    if value < 0:
                        raise ValueError("Purchase cost cannot be negative.")
                elif key in ("location_id", "department_id", "employee_id"):
                    value = self._optional_id(value)
                elif key == "status":
                    if value not in self.ASSET_STATUSES:
                        raise ValueError("Invalid asset status.")
                elif key == "condition":
                    if value not in self.ASSET_CONDITIONS:
                        raise ValueError("Invalid asset condition.")
                elif isinstance(value, str):
                    value = value.strip()
                if key in ("name", "category") and not value:
                    raise ValueError("Asset name and category are required.")
                if key in ("asset_tag", "barcode"):
                    value = value or current.get(key) or current["asset_tag"]
                    clash = conn.execute(
                        "SELECT id FROM assets WHERE %s=? AND id<>?" % key,
                        (value, asset_id),
                    ).fetchone()
                    if clash:
                        raise ValueError("%s %s is already in use." % (key.replace("_", " ").title(), value))
                if str(current.get(key, "") or "") != str(value or ""):
                    updates[key] = value
            if "location_id" in updates or "employee_id" in updates:
                loc_id = updates.get("location_id", current.get("location_id"))
                emp_id = updates.get("employee_id", current.get("employee_id"))
                loc_label, emp_label, _, _, _ = self._sync_labels(
                    conn, loc_id, updates.get("department_id", current.get("department_id")), emp_id
                )
                if loc_label:
                    updates["location"] = loc_label
                if emp_label:
                    updates["assigned_to"] = emp_label
            if not updates:
                return
            assignments = ", ".join("%s=?" % key for key in updates)
            conn.execute(
                "UPDATE assets SET %s, updated_at=CURRENT_TIMESTAMP WHERE id=?" % assignments,
                list(updates.values()) + [asset_id],
            )
            changed = ", ".join("%s: %s -> %s" % (key, current.get(key) or "-", value)
                                for key, value in updates.items())
            self._log_asset_event(conn, asset_id, "Updated", changed)

    def _record_movement(self, conn, asset, movement_type, location_id=None, department_id=None,
                         employee_id=None, reason="", moved_by=""):
        location_id = self._optional_id(location_id) if location_id not in (None, "") else asset.get("location_id")
        department_id = self._optional_id(department_id) if department_id not in (None, "") else asset.get("department_id")
        if employee_id == "":
            employee_id = None
        elif employee_id is None:
            employee_id = asset.get("employee_id")
        else:
            employee_id = self._optional_id(employee_id)
        loc_label, emp_label, location_id, department_id, employee_id = self._sync_labels(
            conn, location_id, department_id, employee_id
        )
        status = asset["status"]
        if status not in ("Retired", "Lost"):
            status = "In Use" if employee_id else ("Available" if movement_type != "Transfer" else status)
        conn.execute(
            """UPDATE assets SET location_id=?, department_id=?, employee_id=?,
               location=?, assigned_to=?, status=?, updated_at=CURRENT_TIMESTAMP WHERE id=?""",
            (location_id, department_id, employee_id, loc_label or asset.get("location") or "",
             emp_label, status, asset["id"]),
        )
        conn.execute(
            """INSERT INTO asset_movements
            (asset_id, movement_type, from_location_id, to_location_id, from_department_id,
             to_department_id, from_employee_id, to_employee_id, reason, moved_by)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
            (asset["id"], movement_type, asset.get("location_id"), location_id,
             asset.get("department_id"), department_id, asset.get("employee_id"), employee_id,
             reason.strip(), moved_by.strip()),
        )
        details = "%s: %s -> %s" % (
            movement_type,
            asset.get("location") or asset.get("display_location") or "-",
            loc_label or "-",
        )
        if emp_label or asset.get("assigned_to"):
            details += "; %s -> %s" % (asset.get("assigned_to") or "-", emp_label or "-")
        if reason.strip():
            details += ". " + reason.strip()
        self._log_asset_event(conn, asset["id"], movement_type, details)

    def move_asset(self, asset_id, location_id, reason="", moved_by=""):
        if not str(location_id or "").strip():
            raise ValueError("Choose the destination location.")
        with self._db() as conn:
            asset = self._require_asset(conn, asset_id)
            self._record_movement(
                conn, asset, "Movement", location_id=location_id,
                department_id=asset.get("department_id"), employee_id=asset.get("employee_id"),
                reason=reason, moved_by=moved_by,
            )

    def transfer_asset(self, asset_id, location="", assigned_to="", location_id=None,
                       department_id=None, employee_id=None, reason="", moved_by=""):
        with self._db() as conn:
            asset = self._require_asset(conn, asset_id)
            if any(str(value or "").strip() for value in (location_id, department_id, employee_id)):
                self._record_movement(
                    conn, asset, "Transfer", location_id=location_id or asset.get("location_id"),
                    department_id=department_id, employee_id=employee_id,
                    reason=reason, moved_by=moved_by,
                )
                return
            if not location.strip() and not assigned_to.strip():
                raise ValueError("Choose a destination location, department, or employee.")
            location, assigned_to = location.strip(), assigned_to.strip()
            conn.execute(
                """UPDATE assets SET location=?, assigned_to=?,
                   status=CASE WHEN status IN ('Retired', 'Lost') THEN status
                               WHEN ? <> '' THEN 'In Use' ELSE 'Available' END,
                   updated_at=CURRENT_TIMESTAMP WHERE id=?""",
                (location, assigned_to, assigned_to, asset_id),
            )
            details = "Location %s -> %s; assigned %s -> %s" % (
                asset["location"] or "-", location or "-",
                asset["assigned_to"] or "-", assigned_to or "-",
            )
            self._log_asset_event(conn, asset_id, "Transferred", details)

    def allocate_asset(self, asset_id, employee_id, department_id=None, reason="", moved_by=""):
        if not str(employee_id or "").strip():
            raise ValueError("Choose an employee to allocate the asset.")
        with self._db() as conn:
            asset = self._require_asset(conn, asset_id)
            employee = conn.execute("SELECT * FROM employees WHERE id=?", (employee_id,)).fetchone()
            if not employee:
                raise ValueError("Employee not found.")
            self._record_movement(
                conn, asset, "Allocation", location_id=asset.get("location_id"),
                department_id=department_id or employee["department_id"],
                employee_id=employee_id, reason=reason, moved_by=moved_by,
            )

    def set_asset_status(self, asset_id, status, notes=""):
        if status not in self.ASSET_STATUSES:
            raise ValueError("Invalid asset status.")
        with self._db() as conn:
            current = self._require_asset(conn, asset_id)
            conn.execute(
                "UPDATE assets SET status=?, updated_at=CURRENT_TIMESTAMP WHERE id=?",
                (status, asset_id),
            )
            details = "%s -> %s" % (current["status"], status)
            if notes.strip():
                details += ". " + notes.strip()
            self._log_asset_event(conn, asset_id, "Status change", details)

    def log_asset_maintenance(self, asset_id, details, set_status=""):
        details = details.strip()
        if not details:
            raise ValueError("Maintenance notes are required.")
        if set_status and set_status not in self.ASSET_STATUSES:
            raise ValueError("Invalid asset status.")
        with self._db() as conn:
            current = self._require_asset(conn, asset_id)
            if set_status:
                conn.execute(
                    "UPDATE assets SET status=?, updated_at=CURRENT_TIMESTAMP WHERE id=?",
                    (set_status, asset_id),
                )
                details = "%s (status %s -> %s)" % (details, current["status"], set_status)
            else:
                conn.execute(
                    "UPDATE assets SET updated_at=CURRENT_TIMESTAMP WHERE id=?",
                    (asset_id,),
                )
            self._log_asset_event(conn, asset_id, "Maintenance", details)

    def delete_asset(self, asset_id):
        with self._db() as conn:
            self._require_asset(conn, asset_id)
            conn.execute("DELETE FROM verification_items WHERE asset_id=?", (asset_id,))
            conn.execute("DELETE FROM asset_movements WHERE asset_id=?", (asset_id,))
            conn.execute("DELETE FROM asset_events WHERE asset_id=?", (asset_id,))
            conn.execute("DELETE FROM assets WHERE id=?", (asset_id,))

    def get_departments(self):
        with self._db() as conn:
            return [dict(row) for row in conn.execute(
                "SELECT * FROM departments ORDER BY name").fetchall()]

    def add_department(self, code, name, manager="", notes=""):
        code, name = code.strip().upper(), name.strip()
        if not code or not name:
            raise ValueError("Department code and name are required.")
        with self._db() as conn:
            try:
                conn.execute(
                    "INSERT INTO departments (code, name, manager, notes) VALUES (?, ?, ?, ?)",
                    (code, name, manager.strip(), notes.strip()),
                )
            except Exception:
                raise ValueError("Department code %s already exists." % code)

    def delete_department(self, department_id):
        with self._db() as conn:
            used = conn.execute(
                """SELECT (
                    (SELECT COUNT(*) FROM assets WHERE department_id=?) +
                    (SELECT COUNT(*) FROM employees WHERE department_id=?) +
                    (SELECT COUNT(*) FROM locations WHERE department_id=?)
                ) AS n""",
                (department_id, department_id, department_id),
            ).fetchone()["n"]
            if used:
                raise ValueError("Department is in use and cannot be deleted.")
            cursor = conn.execute("DELETE FROM departments WHERE id=?", (department_id,))
            if cursor.rowcount == 0:
                raise ValueError("Department not found.")

    def get_locations(self):
        with self._db() as conn:
            return [dict(row) for row in conn.execute(
                """SELECT loc.*, dep.name AS department_name
                   FROM locations loc LEFT JOIN departments dep ON dep.id = loc.department_id
                   ORDER BY loc.name"""
            ).fetchall()]

    def add_location(self, code, name, building="", floor="", department_id=None, notes=""):
        code, name = code.strip().upper(), name.strip()
        if not code or not name:
            raise ValueError("Location code and name are required.")
        with self._db() as conn:
            try:
                conn.execute(
                    """INSERT INTO locations (code, name, building, floor, department_id, notes)
                    VALUES (?, ?, ?, ?, ?, ?)""",
                    (code, name, building.strip(), floor.strip(),
                     self._optional_id(department_id), notes.strip()),
                )
            except Exception:
                raise ValueError("Location code %s already exists." % code)

    def delete_location(self, location_id):
        with self._db() as conn:
            used = conn.execute(
                "SELECT COUNT(*) AS n FROM assets WHERE location_id=?", (location_id,)
            ).fetchone()["n"]
            if used:
                raise ValueError("Location is assigned to assets and cannot be deleted.")
            cursor = conn.execute("DELETE FROM locations WHERE id=?", (location_id,))
            if cursor.rowcount == 0:
                raise ValueError("Location not found.")

    def get_employees(self, active_only=False):
        sql = """SELECT emp.*, dep.name AS department_name
                 FROM employees emp LEFT JOIN departments dep ON dep.id = emp.department_id"""
        if active_only:
            sql += " WHERE emp.status='Active'"
        sql += " ORDER BY emp.name"
        with self._db() as conn:
            return [dict(row) for row in conn.execute(sql).fetchall()]

    def add_employee(self, emp_code, name, department_id=None, designation="", phone=""):
        emp_code, name = emp_code.strip().upper(), name.strip()
        if not emp_code or not name:
            raise ValueError("Employee code and name are required.")
        with self._db() as conn:
            try:
                conn.execute(
                    """INSERT INTO employees (emp_code, name, department_id, designation, phone)
                    VALUES (?, ?, ?, ?, ?)""",
                    (emp_code, name, self._optional_id(department_id),
                     designation.strip(), phone.strip()),
                )
            except Exception:
                raise ValueError("Employee code %s already exists." % emp_code)

    def delete_employee(self, employee_id):
        with self._db() as conn:
            used = conn.execute(
                "SELECT COUNT(*) AS n FROM assets WHERE employee_id=?", (employee_id,)
            ).fetchone()["n"]
            if used:
                raise ValueError("Employee has allocated assets and cannot be deleted.")
            cursor = conn.execute("DELETE FROM employees WHERE id=?", (employee_id,))
            if cursor.rowcount == 0:
                raise ValueError("Employee not found.")

    def get_asset_categories(self):
        with self._db() as conn:
            return [dict(row) for row in conn.execute(
                "SELECT * FROM asset_categories ORDER BY name").fetchall()]

    def add_asset_category(self, name):
        name = name.strip()
        if not name:
            raise ValueError("Category name is required.")
        with self._db() as conn:
            try:
                conn.execute("INSERT INTO asset_categories (name) VALUES (?)", (name,))
            except Exception:
                raise ValueError("Category already exists.")

    def get_movements(self, limit=200):
        with self._db() as conn:
            return [dict(row) for row in conn.execute(
                """SELECT m.*, a.asset_tag, a.name AS asset_name,
                          loc_to.name AS to_location, emp_to.name AS to_employee,
                          dep_to.name AS to_department
                   FROM asset_movements m
                   JOIN assets a ON a.id = m.asset_id
                   LEFT JOIN locations loc_to ON loc_to.id = m.to_location_id
                   LEFT JOIN employees emp_to ON emp_to.id = m.to_employee_id
                   LEFT JOIN departments dep_to ON dep_to.id = m.to_department_id
                   ORDER BY m.id DESC LIMIT ?""",
                (limit,),
            ).fetchall()]

    def start_verification(self, name, location_id=None, notes=""):
        name = name.strip() or "Physical verification"
        with self._db() as conn:
            cursor = conn.execute(
                """INSERT INTO verification_sessions (name, location_id, notes)
                VALUES (?, ?, ?)""",
                (name, self._optional_id(location_id), notes.strip()),
            )
            return cursor.lastrowid

    def get_verification_sessions(self):
        with self._db() as conn:
            return [dict(row) for row in conn.execute(
                """SELECT s.*, loc.name AS location_name,
                          (SELECT COUNT(*) FROM verification_items i WHERE i.session_id=s.id) AS checked_count,
                          (SELECT COUNT(*) FROM verification_items i
                           WHERE i.session_id=s.id AND i.result='Missing') AS missing_count
                   FROM verification_sessions s
                   LEFT JOIN locations loc ON loc.id = s.location_id
                   ORDER BY s.id DESC"""
            ).fetchall()]

    def get_verification(self, session_id):
        with self._db() as conn:
            session = conn.execute(
                """SELECT s.*, loc.name AS location_name
                   FROM verification_sessions s
                   LEFT JOIN locations loc ON loc.id = s.location_id
                   WHERE s.id=?""",
                (session_id,),
            ).fetchone()
            if not session:
                raise ValueError("Verification session not found.")
            session = dict(session)
            expected_sql = self._asset_select() + " WHERE a.status NOT IN ('Retired')"
            params = []
            if session["location_id"]:
                expected_sql += " AND a.location_id=?"
                params.append(session["location_id"])
            expected = [self._hydrate_asset(row) for row in conn.execute(expected_sql, params).fetchall()]
            items = {
                row["asset_id"]: dict(row)
                for row in conn.execute(
                    "SELECT * FROM verification_items WHERE session_id=?", (session_id,)
                ).fetchall()
            }
        for asset in expected:
            asset["verification"] = items.get(asset["id"])
        session["expected"] = expected
        session["items"] = list(items.values())
        session["found_count"] = sum(1 for item in items.values() if item["result"] == "Found")
        session["missing_count"] = sum(1 for item in items.values() if item["result"] == "Missing")
        session["wrong_count"] = sum(1 for item in items.values() if item["result"] == "Wrong location")
        return session

    def mark_verification(self, session_id, asset_id=None, asset_tag="", result="Found", notes=""):
        if result not in ("Found", "Missing", "Wrong location"):
            raise ValueError("Invalid verification result.")
        with self._db() as conn:
            session = conn.execute(
                "SELECT * FROM verification_sessions WHERE id=?", (session_id,)
            ).fetchone()
            if not session:
                raise ValueError("Verification session not found.")
            if session["status"] != "Open":
                raise ValueError("This verification session is already completed.")
            if asset_tag and not asset_id:
                scanned = self.parse_scanned_tag(asset_tag)
                asset = conn.execute(
                    "SELECT id FROM assets WHERE asset_tag=? OR barcode=?",
                    (scanned, scanned),
                ).fetchone()
                if not asset:
                    raise ValueError("Scanned tag %s is not in the asset master." % scanned)
                asset_id = asset["id"]
            if not asset_id:
                raise ValueError("Scan or select an asset.")
            self._require_asset(conn, asset_id)
            conn.execute(
                """INSERT INTO verification_items (session_id, asset_id, result, notes)
                   VALUES (?, ?, ?, ?)
                   ON CONFLICT(session_id, asset_id)
                   DO UPDATE SET result=excluded.result, notes=excluded.notes,
                                 checked_at=CURRENT_TIMESTAMP""",
                (session_id, asset_id, result, notes.strip()),
            )
            self._log_asset_event(conn, asset_id, "Physical verification", "%s (%s)" % (result, session["name"]))
            return asset_id

    def complete_verification(self, session_id, mark_missing_lost=False):
        session = self.get_verification(session_id)
        with self._db() as conn:
            current = conn.execute(
                "SELECT * FROM verification_sessions WHERE id=?", (session_id,)
            ).fetchone()
            if not current or current["status"] != "Open":
                raise ValueError("Verification session is not open.")
            for asset in session["expected"]:
                if not asset.get("verification"):
                    conn.execute(
                        """INSERT INTO verification_items (session_id, asset_id, result, notes)
                           VALUES (?, ?, 'Missing', 'Not presented during physical verification')""",
                        (session_id, asset["id"]),
                    )
                    self._log_asset_event(
                        conn, asset["id"], "Physical verification",
                        "Missing during %s" % current["name"],
                    )
                    if mark_missing_lost:
                        conn.execute(
                            "UPDATE assets SET status='Lost', updated_at=CURRENT_TIMESTAMP WHERE id=?",
                            (asset["id"],),
                        )
                elif mark_missing_lost and asset["verification"]["result"] == "Missing":
                    conn.execute(
                        "UPDATE assets SET status='Lost', updated_at=CURRENT_TIMESTAMP WHERE id=?",
                        (asset["id"],),
                    )
            conn.execute(
                """UPDATE verification_sessions
                   SET status='Completed', completed_at=CURRENT_TIMESTAMP WHERE id=?""",
                (session_id,),
            )

    def get_missing_assets(self):
        with self._db() as conn:
            lost = [self._hydrate_asset(row) for row in conn.execute(
                self._asset_select() + " WHERE a.status='Lost' ORDER BY a.asset_tag"
            ).fetchall()]
            verification_missing = [dict(row) for row in conn.execute(
                """SELECT a.asset_tag, a.name, a.status, loc.name AS location_name,
                          s.name AS session_name, s.started_at, i.notes, i.result
                   FROM verification_items i
                   JOIN verification_sessions s ON s.id = i.session_id
                   JOIN assets a ON a.id = i.asset_id
                   LEFT JOIN locations loc ON loc.id = a.location_id
                   WHERE i.result='Missing'
                   ORDER BY i.id DESC"""
            ).fetchall()]
        return {"lost": lost, "verification_missing": verification_missing}

    def get_audit_report(self, limit=250):
        with self._db() as conn:
            events = [dict(row) for row in conn.execute(
                """SELECT e.created_at, e.event_type AS activity, e.details,
                          a.asset_tag, a.name AS asset_name
                   FROM asset_events e JOIN assets a ON a.id = e.asset_id
                   ORDER BY e.id DESC LIMIT ?""",
                (limit,),
            ).fetchall()]
            movements = self.get_movements(limit)
            sessions = self.get_verification_sessions()
        return {"events": events, "movements": movements, "verifications": sessions}
