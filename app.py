from functools import wraps
from flask import Flask, Response, flash, redirect, render_template, request, session, url_for
from main import AssetSystem

app = Flask(__name__)
app.secret_key = "asset-tracking-local"
inventory = AssetSystem()
LOGIN_USERNAME = "admin"
LOGIN_PASSWORD = "admin"


def form_value(name, default=""):
    return request.form.get(name, default).strip()


def login_required(view):
    @wraps(view)
    def wrapped(*args, **kwargs):
        if not session.get("logged_in"):
            return redirect(url_for("login"))
        return view(*args, **kwargs)
    return wrapped


@app.before_request
def require_login():
    public_endpoints = {"login", "static"}
    if request.endpoint not in public_endpoints and not session.get("logged_in"):
        return redirect(url_for("login"))


@app.route("/")
def home():
    return redirect(url_for("assets_home") if session.get("logged_in") else url_for("login"))


@app.route("/login", methods=["GET", "POST"])
def login():
    if request.method == "POST":
        if form_value("username") == LOGIN_USERNAME and request.form.get("password", "") == LOGIN_PASSWORD:
            session["logged_in"] = True
            return redirect(url_for("assets_home"))
        flash("Invalid username or password.", "error")
    return render_template("login.html")


@app.post("/logout")
def logout():
    session.clear()
    return redirect(url_for("login"))


@app.route("/dashboard")
def dashboard():
    return redirect(url_for("assets_home"))


def asset_form_fields():
    return {
        "asset_tag": form_value("asset_tag"),
        "barcode": form_value("barcode"),
        "name": form_value("name"),
        "category": form_value("category"),
        "description": form_value("description"),
        "serial_no": form_value("serial_no"),
        "brand": form_value("brand"),
        "model": form_value("model"),
        "purchase_date": form_value("purchase_date"),
        "purchase_cost": form_value("purchase_cost", "0"),
        "supplier": form_value("supplier"),
        "location": form_value("location"),
        "assigned_to": form_value("assigned_to"),
        "status": form_value("status", "Available"),
        "condition": form_value("condition", "Good"),
        "warranty_expiry": form_value("warranty_expiry"),
        "notes": form_value("notes"),
        "location_id": form_value("location_id"),
        "department_id": form_value("department_id"),
        "employee_id": form_value("employee_id"),
    }


def assets_redirect(tab="register"):
    return redirect(url_for("assets_home") + "#" + tab)


def asset_page_context():
    asset_query = request.args.get("q", "").strip()
    asset_status = request.args.get("asset_status", "").strip()
    session_id = request.args.get("session", "").strip()
    verification = None
    if session_id:
        try:
            verification = inventory.get_verification(int(session_id))
        except (ValueError, TypeError):
            verification = None
    return {
        "assets": inventory.get_all_assets(query=asset_query, status=asset_status),
        "asset_summary": inventory.get_asset_summary(),
        "asset_query": asset_query,
        "asset_status": asset_status,
        "asset_statuses": inventory.ASSET_STATUSES,
        "asset_conditions": inventory.ASSET_CONDITIONS,
        "asset_categories": inventory.ASSET_CATEGORIES,
        "locations": inventory.get_locations(),
        "departments": inventory.get_departments(),
        "employees": inventory.get_employees(),
        "movements": inventory.get_movements(),
        "verifications": inventory.get_verification_sessions(),
        "verification": verification,
        "missing_assets": inventory.get_missing_assets(),
        "audit_report": inventory.get_audit_report(),
    }


def qr_markup(asset):
    return inventory.qr_svg(inventory.bartender_qr_payload(asset))


@app.get("/assets")
def assets_home():
    return render_template("assets.html", **asset_page_context())


@app.post("/assets/add")
def add_asset():
    try:
        tag = inventory.add_asset(**asset_form_fields())
        flash("Asset %s registered and QR code generated." % tag, "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return assets_redirect("register")


@app.post("/asset-categories/add")
def add_asset_category():
    try:
        inventory.add_asset_category(form_value("name"))
        flash("Asset category added.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return assets_redirect("master")


@app.post("/departments/add")
def add_department():
    try:
        inventory.add_department(
            form_value("code"), form_value("name"), form_value("manager"), form_value("notes"),
        )
        flash("Department added.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return assets_redirect("departments")


@app.post("/departments/<int:department_id>/delete")
def delete_department(department_id):
    try:
        inventory.delete_department(department_id)
        flash("Department deleted.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return assets_redirect("departments")


@app.post("/locations/add")
def add_location():
    try:
        inventory.add_location(
            form_value("code"), form_value("name"), form_value("building"),
            form_value("floor"), form_value("department_id"), form_value("notes"),
        )
        flash("Location added.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return assets_redirect("locations")


@app.post("/locations/<int:location_id>/delete")
def delete_location(location_id):
    try:
        inventory.delete_location(location_id)
        flash("Location deleted.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return assets_redirect("locations")


@app.post("/employees/add")
def add_employee():
    try:
        inventory.add_employee(
            form_value("emp_code"), form_value("name"), form_value("department_id"),
            form_value("designation"), form_value("phone"),
        )
        flash("Employee added.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return assets_redirect("employees")


@app.post("/employees/<int:employee_id>/delete")
def delete_employee(employee_id):
    try:
        inventory.delete_employee(employee_id)
        flash("Employee deleted.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return assets_redirect("employees")


@app.get("/assets/bartender.csv")
def bartender_csv():
    response = Response(inventory.bartender_csv().encode("utf-8-sig"), mimetype="text/csv")
    response.headers["Content-Disposition"] = "attachment; filename=AssetTracking_Bartender.csv"
    return response


@app.get("/assets/bartender.txt")
def bartender_txt():
    response = Response(
        inventory.bartender_txt().encode("cp1252", "replace"),
        mimetype="text/plain; charset=windows-1252",
    )
    response.headers["Content-Disposition"] = "attachment; filename=AssetTracking_Bartender.txt"
    return response


@app.get("/assets/<int:asset_id>")
def asset_detail(asset_id):
    try:
        asset = inventory.get_asset(asset_id)
        events, movements = inventory.get_asset_events(asset_id)
    except ValueError:
        flash("Asset not found.", "error")
        return assets_redirect("master")
    return render_template(
        "asset_detail.html",
        asset=asset,
        events=events,
        movements=movements,
        barcode_svg=qr_markup(asset),
        asset_statuses=inventory.ASSET_STATUSES,
        asset_conditions=inventory.ASSET_CONDITIONS,
        asset_categories=inventory.ASSET_CATEGORIES,
        locations=inventory.get_locations(),
        departments=inventory.get_departments(),
        employees=inventory.get_employees(),
    )


@app.get("/assets/<int:asset_id>/barcode.svg")
def asset_barcode_image(asset_id):
    try:
        asset = inventory.get_asset(asset_id)
    except ValueError:
        return "Asset not found", 404
    return Response(qr_markup(asset), mimetype="image/svg+xml")


@app.get("/assets/labels")
def asset_labels():
    assets = inventory.get_all_assets()
    return render_template(
        "asset_labels.html",
        assets=assets,
        barcodes={asset["id"]: qr_markup(asset) for asset in assets},
    )


@app.post("/assets/<int:asset_id>/update")
def update_asset(asset_id):
    try:
        inventory.update_asset(asset_id, **asset_form_fields())
        flash("Asset master record updated.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return redirect(url_for("asset_detail", asset_id=asset_id))


@app.post("/assets/<int:asset_id>/move")
def move_asset(asset_id):
    try:
        inventory.move_asset(
            asset_id, form_value("location_id"), form_value("reason"), form_value("moved_by"),
        )
        flash("Asset movement recorded.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return assets_redirect("movement")


@app.post("/assets/<int:asset_id>/transfer")
def transfer_asset(asset_id):
    try:
        inventory.transfer_asset(
            asset_id,
            location=form_value("location"),
            assigned_to=form_value("assigned_to"),
            location_id=form_value("location_id"),
            department_id=form_value("department_id"),
            employee_id=form_value("employee_id"),
            reason=form_value("reason"),
            moved_by=form_value("moved_by"),
        )
        flash("Asset transfer recorded.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return assets_redirect("transfer")


@app.post("/assets/<int:asset_id>/allocate")
def allocate_asset(asset_id):
    try:
        inventory.allocate_asset(
            asset_id, form_value("employee_id"), form_value("department_id"),
            form_value("reason"), form_value("moved_by"),
        )
        flash("Asset allocated to employee.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return assets_redirect("allocation")


@app.post("/assets/<int:asset_id>/status")
def set_asset_status(asset_id):
    try:
        inventory.set_asset_status(asset_id, form_value("status"), form_value("notes"))
        flash("Asset status updated.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return redirect(request.referrer or url_for("assets_home") + "#master")


@app.post("/assets/<int:asset_id>/maintenance")
def log_asset_maintenance(asset_id):
    try:
        inventory.log_asset_maintenance(
            asset_id, form_value("details"), form_value("set_status"),
        )
        flash("Maintenance recorded.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return redirect(url_for("asset_detail", asset_id=asset_id))


@app.post("/assets/verify/start")
def start_verification():
    try:
        session_id = inventory.start_verification(
            form_value("name"), form_value("location_id"), form_value("notes"),
        )
        flash("Physical verification started.", "success")
        return redirect(url_for("assets_home", session=session_id) + "#verify")
    except ValueError as exc:
        flash(str(exc), "error")
        return assets_redirect("verify")


@app.post("/assets/verify/<int:session_id>/mark")
def mark_verification(session_id):
    try:
        inventory.mark_verification(
            session_id,
            asset_id=form_value("asset_id") or None,
            asset_tag=form_value("asset_tag"),
            result=form_value("result", "Found"),
            notes=form_value("notes"),
        )
        flash("Verification result saved.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return redirect(url_for("assets_home", session=session_id) + "#verify")


@app.post("/assets/verify/<int:session_id>/complete")
def complete_verification(session_id):
    try:
        inventory.complete_verification(
            session_id, mark_missing_lost=form_value("mark_missing_lost") == "yes",
        )
        flash("Physical verification completed.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return redirect(url_for("assets_home", session=session_id) + "#verify")


@app.post("/assets/<int:asset_id>/delete")
def delete_asset(asset_id):
    try:
        inventory.delete_asset(asset_id)
        flash("Asset removed from the master.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return assets_redirect("master")


if __name__ == "__main__":
    app.run(debug=True)
