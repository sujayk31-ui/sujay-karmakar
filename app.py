from functools import wraps
from flask import Flask, flash, redirect, render_template, request, session, url_for
from main import InventorySystem

app = Flask(__name__)
app.secret_key = "taddy-erp-local"
inventory = InventorySystem()
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
    return redirect(url_for("dashboard") if session.get("logged_in") else url_for("login"))


@app.route("/login", methods=["GET", "POST"])
def login():
    if request.method == "POST":
        if form_value("username") == LOGIN_USERNAME and request.form.get("password", "") == LOGIN_PASSWORD:
            session["logged_in"] = True
            return redirect(url_for("dashboard"))
        flash("Invalid username or password.", "error")
    return render_template("login.html")


@app.post("/logout")
def logout():
    session.clear()
    return redirect(url_for("login"))


@app.route("/dashboard")
def dashboard():
    return render_template(
        "dashboard.html",
        materials=inventory.get_all_materials(),
        stitching_jobs=inventory.get_stitching_jobs(),
        ready_stock=inventory.get_ready_stock(),
        sales=inventory.get_sales(),
        invoices=inventory.get_invoices(),
    )


@app.post("/materials/add")
def add_material():
    try:
        inventory.add_raw_material(
            form_value("item_name"), form_value("diameter"), form_value("category", "General"),
            form_value("length"), form_value("color"), form_value("quantity", "0"),
            form_value("supplier"), form_value("unit", "pcs"), form_value("batch_no"),
        )
        flash("Raw material added.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return redirect(url_for("dashboard") + "#raw-materials")


@app.post("/materials/<int:material_id>/adjust")
def adjust_material(material_id):
    try:
        amount = form_value("amount")
        if form_value("action") == "deduct":
            amount = -inventory._number(amount)
        inventory.adjust_material(material_id, amount)
        flash("Stock updated.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return redirect(url_for("dashboard") + "#raw-materials")


@app.post("/materials/<int:material_id>/delete")
def delete_material(material_id):
    try:
        inventory.delete_material(material_id)
        flash("Material deleted.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return redirect(url_for("dashboard") + "#raw-materials")


@app.post("/stitching/send")
def send_stitching():
    try:
        inventory.send_to_stitching(
            form_value("worker"), form_value("category"), form_value("size"),
            form_value("quantity"),
        )
        flash("Materials assigned to stitching.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return redirect(url_for("dashboard") + "#stitching")


@app.post("/stitching/<int:job_id>/receive")
def receive_stitching(job_id):
    try:
        inventory.receive_from_stitching(job_id, form_value("quantity"))
        flash("Finished goods received into ready stock.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return redirect(url_for("dashboard") + "#stitching")


@app.post("/stitching/<int:job_id>/delete")
def delete_stitching(job_id):
    try:
        inventory.delete_stitching_job(job_id)
        flash("Stitching worker record deleted.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return redirect(url_for("dashboard") + "#stitching")


@app.post("/ready-stock/add")
def add_ready_stock():
    try:
        inventory.add_ready_stock(
            form_value("product_name"), form_value("category"), form_value("size"),
            form_value("quantity"), form_value("price", "0"),
        )
        flash("Ready stock added.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return redirect(url_for("dashboard") + "#ready-stock")


@app.post("/ready-stock/<int:stock_id>/delete")
def delete_ready_stock(stock_id):
    try:
        inventory.delete_ready_stock(stock_id)
        flash("Ready stock item deleted.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return redirect(url_for("dashboard") + "#ready-stock")


@app.post("/sales")
def record_sale():
    try:
        stock_ids = request.form.getlist("stock_id")
        quantities = request.form.getlist("quantity")
        unit_prices = request.form.getlist("unit_price")
        if not (len(stock_ids) == len(quantities) == len(unit_prices)):
            raise ValueError("Each product must have a quantity and unit price.")
        items = [
            {"stock_id": int(stock_id), "quantity": quantity, "unit_price": unit_price}
            for stock_id, quantity, unit_price in zip(stock_ids, quantities, unit_prices)
        ]
        invoice_no = inventory.record_sale(
            form_value("customer_name"), form_value("phone"), form_value("address"),
            items,
        )
        flash("Sale recorded and invoice %s generated." % invoice_no, "success")
    except (ValueError, TypeError) as exc:
        flash(str(exc), "error")
    return redirect(url_for("dashboard") + "#sales")


@app.post("/sales/<int:sale_id>/delete")
def delete_sale(sale_id):
    try:
        inventory.delete_sale(sale_id)
        flash("Customer sale deleted and stock restored.", "success")
    except ValueError as exc:
        flash(str(exc), "error")
    return redirect(url_for("dashboard") + "#sales")


@app.get("/invoice/<invoice_no>")
def invoice(invoice_no):
    invoice_data = inventory.get_invoice(invoice_no)
    if not invoice_data:
        return "Invoice not found", 404
    return render_template("invoice.html", invoice=invoice_data)


# Backwards-compatible routes for the original prototype.
@app.route("/add_material", methods=["GET", "POST"])
def add_material_legacy():
    if request.method == "POST":
        return add_material()
    return render_template("add_raw_material.html")


@app.route("/stock")
def stock():
    return redirect(url_for("dashboard") + "#raw-materials")


if __name__ == "__main__":
    app.run(debug=True)
