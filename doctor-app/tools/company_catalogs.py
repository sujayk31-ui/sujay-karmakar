#!/usr/bin/env python3
"""Turn public company composition tables into seed rows.

A row is kept only when the same public table prints an active ingredient.
A price is kept only when that source, or a second file from the same company
whose pack name matches, prints an MRP. Nothing here is invented.

Snapshots are downloaded separately and read from MED_SRC (default /tmp/companies).
The raw HTML and PDFs are not shipped in the app. Facts extracted from them are.
"""

from __future__ import annotations

import html
import os
import re
import subprocess
from html.parser import HTMLParser
from pathlib import Path

ROOT = Path(os.environ.get("MED_SRC", "/tmp/companies"))
RETRIEVED = "2026-10-27"

DOSE_ONLY = re.compile(
    r"^[\d\s./,%+\-x×]*(mg|mcg|µg|g|gm|ml|iu|i\.u\.|%|mg/ml|w/w|w/v|units?)?$",
    re.I,
)
FORM_WORDS = {
    "tablet", "tablets", "capsule", "capsules", "injection", "syrup", "cream",
    "ointment", "drops", "suspension", "gel", "sachet", "powder", "solution",
    "lotion", "spray", "respules", "inhaler", "kit", "vial", "ampoule",
}


def ingredientish(text: str) -> bool:
    text = re.sub(r"\s+", " ", (text or "")).strip(" -–,;/")
    if not text or text.lower() in {"(blank)", "na", "n/a", "nil", "-", "none"}:
        return False
    compact = text.replace(" ", "")
    if DOSE_ONLY.match(text) or DOSE_ONLY.match(compact):
        return False
    words = re.findall(r"[A-Za-z]{3,}", text)
    if not words:
        return False
    if all(word.lower() in FORM_WORDS for word in words):
        return False
    return True


def clean(text: str) -> str:
    text = html.unescape(text or "")
    text = text.replace("\xa0", " ")
    text = re.sub(r"\s+", " ", text).strip(" -–,;/")
    return text


def slug(text: str) -> str:
    value = re.sub(r"[^a-z0-9]+", "-", clean(text).lower()).strip("-")
    return value[:90] or "row"


def guess_brand(name: str) -> str:
    name = clean(name)
    cut = re.split(r"\s+\d", name, maxsplit=1)[0]
    cut = re.sub(r"\s*\(.*$", "", cut).strip(" -")
    if len(re.sub(r"[^A-Za-z]", "", cut)) < 3:
        return name
    return cut


def row(
    *,
    key: str,
    name: str,
    composition: str,
    company: str,
    source_name: str,
    source_url: str,
    source_detail: str,
    brand: str | None = None,
    form: str | None = None,
    pack: str | None = None,
    section: str | None = None,
    mrp: float | None = None,
    price_as_of: str | None = None,
    price_source: str | None = None,
) -> dict:
    name = clean(name)
    composition = clean(composition)
    item = {
        "key": key,
        "name": name,
        "brand": clean(brand or guess_brand(name)),
        "company": company,
        "form": clean(form) if form else None,
        "composition": composition,
        "pack": clean(pack) if pack else None,
        "care": None,
        "sectionCode": None,
        "sectionTitle": clean(section) if section else None,
        "kind": "company",
        "sourceName": source_name,
        "sourceUrl": source_url,
        "sourceDetail": source_detail,
        "retrieved": RETRIEVED,
        "mrp": round(mrp, 2) if mrp and mrp > 0 else None,
        "priceAsOf": price_as_of if mrp and mrp > 0 else None,
        "priceSource": price_source if mrp and mrp > 0 else None,
    }
    return item


class TableParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.depth = 0
        self.tables: list[list[list[str]]] = []
        self.cells: list[str] = []
        self.buf: list[str] = []
        self.in_cell = False

    def handle_starttag(self, tag, attrs):
        if tag == "table":
            self.depth += 1
            if self.depth == 1:
                self.tables.append([])
        elif self.depth == 1 and tag == "tr":
            self.cells = []
        elif self.depth == 1 and tag in ("td", "th"):
            self.in_cell = True
            self.buf = []
        elif self.in_cell and tag == "br":
            self.buf.append(" ")

    def handle_endtag(self, tag):
        if tag in ("td", "th") and self.in_cell:
            self.cells.append(clean("".join(self.buf)))
            self.in_cell = False
        elif tag == "tr" and self.depth == 1 and any(self.cells):
            self.tables[-1].append(self.cells)
        elif tag == "table" and self.depth:
            self.depth -= 1

    def handle_data(self, data):
        if self.in_cell:
            self.buf.append(data)


def tables_in(path: Path) -> list[list[list[str]]]:
    parser = TableParser()
    parser.feed(path.read_text(errors="replace"))
    return parser.tables


def heading_tables(path: Path) -> list[tuple[str, list[list[str]]]]:
    """Pair each HTML table with the nearest preceding heading."""
    text = path.read_text(errors="replace")
    text = re.sub(r"<script[\s\S]*?</script>", "", text, flags=re.I)
    parts = re.split(r"(<h[1-4][^>]*>[\s\S]*?</h[1-4]>|<table[\s\S]*?</table>)", text, flags=re.I)
    section = ""
    found: list[tuple[str, list[list[str]]]] = []
    for part in parts:
        if re.match(r"<h[1-4]", part, re.I):
            section = clean(re.sub(r"<[^>]+>", " ", part))
        elif part.lower().startswith("<table"):
            parser = TableParser()
            parser.feed(part)
            if parser.tables:
                found.append((section, parser.tables[0]))
    return found


def money(text: str) -> float | None:
    raw = text.replace(",", "")
    match = re.search(r"\d+(?:\.\d+)?", raw)
    if not match:
        return None
    value = float(match.group(0))
    if value <= 0:
        return None
    return round(value, 2)


# --- Sun -----------------------------------------------------------------

SUN_COMPANY = "Sun Pharmaceutical Industries Ltd."
SUN_PRODUCTS = "https://sunpharma.com/india-products/"
SUN_PRICES = "https://sunpharma.com/product-price-list/"
SUN_STOP = {
    "STRIP", "TABLET", "TABLETS", "TAB", "TABS", "CAPSULE", "CAPSULES", "CAP",
    "CAPS", "INJECTION", "INJ", "MG", "ML", "MCG", "GM", "G", "OF", "THE", "AND",
    "SR", "VIAL", "MD", "HFA", "ORAL", "SOLUTION", "SUSPENSION", "CREAM",
    "OINTMENT", "GEL", "DROPS", "NO", "NOS",
}


def sun_tokens(text: str) -> list[str]:
    text = text.upper().replace("'", " ").replace("’", " ")
    text = re.sub(r"(\d+)\s*S\b", r"\1", text)
    text = re.sub(r"[^A-Z0-9./]+", " ", text)
    tokens = []
    for token in text.split():
        if re.fullmatch(r"\d+S", token):
            token = token[:-1]
        pieces = [token]
        if re.search(r"\d", token) and re.search(r"[A-Z]", token) and "/" not in token:
            pieces = re.findall(r"[A-Z]+|\d+", token)
        for piece in pieces:
            if piece in SUN_STOP or not piece:
                continue
            tokens.append(piece)
    return tokens


def load_sun() -> tuple[list[dict], dict]:
    product_table = next(
        table for table in tables_in(ROOT / "sun.html")
        if table and "GENERIC" in table[0][3].upper()
    )
    price_table = next(
        table for table in tables_in(ROOT / "sun-price.html")
        if table and any("PRODUCT NAME" in cell.upper() for cell in table[0] + (table[1] if len(table) > 1 else []))
    )
    header_idx = next(i for i, cells in enumerate(price_table) if any("PRODUCT NAME" in c.upper() for c in cells))
    products = []
    for cells in product_table[1:]:
        if len(cells) < 7:
            continue
        therapy, pack, description, generic, strength, form, brand = cells[:7]
        if not ingredientish(generic):
            continue
        composition = clean(f"{generic} {strength} {form}")
        products.append({
            "therapy": therapy,
            "pack": pack,
            "description": description,
            "composition": composition,
            "form": form,
            "brand": brand,
            "tokens": sun_tokens(f"{description} {pack}"),
        })

    price_rows = []
    for cells in price_table[header_idx + 1:]:
        if len(cells) < 6:
            continue
        name = cells[2]
        pack = cells[3]
        mrp = money(cells[5])
        if not name or not mrp:
            continue
        price_rows.append((name, pack, mrp, sun_tokens(f"{name} {pack}")))

    matched = 0
    ambiguous = 0
    rows = []
    for index, product in enumerate(products, start=1):
        hits = []
        product_alpha = [token for token in product["tokens"] if not re.search(r"\d", token)]
        for name, pack, mrp, tokens in price_rows:
            alpha = [token for token in tokens if not re.search(r"\d", token)]
            numbers = [token for token in tokens if token.isdigit()]
            if not alpha or not numbers:
                continue
            if any(token not in product["tokens"] for token in alpha):
                continue
            if any(token not in product["tokens"] for token in numbers):
                continue
            # "D Rozavel" must not inherit the plain Rozavel price. Extra letters mean a different pack.
            if any(token not in tokens for token in product_alpha):
                continue
            hits.append((name, pack, mrp, tokens))
        mrp = None
        price_source = None
        price_as_of = None
        detail = (
            "Sun Pharma India products table, retrieved 2026-10-27. "
            "The therapy group is the heading Sun prints. It is not a disease indication."
        )
        # A shorter price name such as "PANTOCID (15's)" also fits the 40 mg pack.
        # Keep the most specific price whose every token is in this pack, and only when that winner is unique.
        if hits:
            best = max(len(set(tokens)) for _, _, _, tokens in hits)
            winners = [hit for hit in hits if len(set(hit[3])) == best]
        else:
            winners = []
        if len(winners) == 1:
            matched += 1
            mrp = winners[0][2]
            price_source = SUN_PRICES
            price_as_of = "2025-09-22"
            detail += (
                f" MRP ₹{mrp:.2f} is the GST-inclusive price from Sun's MRP list "
                f"({winners[0][0]}, {winners[0][1]}) because that pack name matched this row and no other Sun price row was as specific. "
                "The price page does not repeat the ingredient."
            )
        else:
            if len(hits) > 1:
                ambiguous += 1
            detail += " No MRP stored. Sun's GST list had no unique pack-name match, so the price was left empty."
        rows.append(row(
            key=f"sun-{index}-{slug(product['description'])}",
            name=product["description"],
            brand=product["brand"],
            composition=product["composition"],
            company=SUN_COMPANY,
            form=product["form"],
            pack=product["pack"],
            section=product["therapy"],
            source_name="Sun Pharma India products table",
            source_url=SUN_PRODUCTS,
            source_detail=detail,
            mrp=mrp,
            price_as_of=price_as_of,
            price_source=price_source,
        ))
    stats = {
        "company": SUN_COMPANY,
        "rows": len(rows),
        "withComposition": len(rows),
        "withPrice": matched,
        "priceRowsInSource": len(price_rows),
        "ambiguousPriceMatchesLeftEmpty": ambiguous,
        "brands": len({item["brand"] for item in rows}),
    }
    return rows, stats


# --- Mankind -------------------------------------------------------------

def load_mankind() -> tuple[list[dict], dict]:
    table = next(
        item for item in tables_in(ROOT / "mankind.html")
        if item and "MOLECULE" in item[0][1].upper()
    )
    rows = []
    skipped = 0
    for cells in table[1:]:
        if len(cells) < 2:
            continue
        brand, molecule = cells[0], cells[1]
        if not brand or not ingredientish(molecule):
            skipped += 1
            continue
        rows.append(row(
            key=f"mankind-{slug(brand)}-{slug(molecule)}",
            name=brand,
            brand=brand,
            composition=molecule,
            company="Mankind Pharma Limited",
            source_name="Mankind all-products page",
            source_url="https://www.mankindpharma.com/products/all-products/",
            source_detail=(
                "Mankind public all-products table, retrieved 2026-10-27. "
                "The molecule cell is the composition. Strength is usually absent, and there is no price column, so both are left empty."
            ),
        ))
    stats = {
        "company": "Mankind Pharma Limited",
        "rows": len(rows),
        "withComposition": len(rows),
        "withPrice": 0,
        "skippedWithoutIngredient": skipped,
        "brands": len({item["brand"] for item in rows}),
    }
    return rows, stats


# --- Cipla ---------------------------------------------------------------

def load_cipla() -> tuple[list[dict], dict]:
    rows = []
    skipped = 0
    for section, table in heading_tables(ROOT / "cipla-dom.html"):
        if not table or "BRAND" not in table[0][0].upper():
            continue
        for cells in table[1:]:
            if len(cells) < 2:
                continue
            brand = cells[0]
            molecule = cells[1]
            form = cells[2] if len(cells) > 2 else None
            if not brand or not ingredientish(molecule):
                skipped += 1
                continue
            rows.append(row(
                key=f"cipla-{slug(section)}-{slug(brand)}-{slug(molecule)}",
                name=brand,
                brand=brand,
                composition=molecule,
                company="Cipla Limited",
                form=form,
                section=section,
                source_name="Cipla generic product list",
                source_url="https://www.cipla.com/generic-product-list",
                source_detail=(
                    "Cipla generic product list, retrieved 2026-10-27. "
                    f"Section heading: {section or 'not printed'}. "
                    "This is the generic list, not Cipla's full branded portfolio. No price column, so MRP is empty."
                ),
            ))
    # The live page repeats some sections in the DOM. Keep one row per brand, molecule, and form.
    unique = []
    seen = set()
    for item in rows:
        identity = (item["brand"].lower(), item["composition"].lower(), (item["form"] or "").lower())
        if identity in seen:
            continue
        seen.add(identity)
        unique.append(item)
    rows = unique
    stats = {
        "company": "Cipla Limited",
        "rows": len(rows),
        "withComposition": len(rows),
        "withPrice": 0,
        "skippedWithoutIngredient": skipped,
        "brands": len({item["brand"] for item in rows}),
        "note": "Generic product list only. Foracort, Seroflo, and the rest of the branded portfolio are not on this page.",
    }
    return rows, stats


# --- Alkem ---------------------------------------------------------------

def sku_has_other_ingredient(sku: str, brand: str) -> bool:
    rest = sku
    for part in re.findall(r"[A-Za-z0-9]+", brand):
        if len(part) >= 3:
            rest = re.sub(rf"\b{re.escape(part)}\b", " ", rest, flags=re.I)
    return ingredientish(rest)


def load_alkem() -> tuple[list[dict], dict]:
    import json
    text = (ROOT / "alkem.html").read_text(errors="replace")
    match = re.search(r'<script id="__NEXT_DATA__" type="application/json">(.*?)</script>', text)
    data = json.loads(match.group(1))
    rows = []
    skipped = 0
    seen_menus = {}
    for menu in data["props"]["pageProps"]["rxProductMenu"]:
        name = menu.get("menu_name") or ""
        if name not in {"India Formulations", "India Generics"}:
            continue
        values = menu.get("rx_formula_values") or []
        seen_menus[name] = len(values)
        for item in values:
            brand = clean(item.get("field1") or "")
            therapy = clean(item.get("field2") or "")
            sku = clean(item.get("field3") or "")
            strength = clean(item.get("field4") or "")
            if ingredientish(strength):
                composition = strength
            elif sku_has_other_ingredient(sku, brand):
                composition = sku
            else:
                skipped += 1
                continue
            display = sku or brand
            rows.append(row(
                key=f"alkem-{slug(name)}-{slug(display)}-{slug(composition)}",
                name=display,
                brand=brand or guess_brand(display),
                composition=composition,
                company="Alkem Laboratories Ltd.",
                section=therapy,
                source_name=f"Alkem Rx products, {name}",
                source_url="https://www.alkemlabs.com/our-offerings/rx-products",
                source_detail=(
                    f"Alkem public Rx page, {name} tab, retrieved 2026-10-27. "
                    "Composition is the strength cell when that cell names the ingredient, "
                    "otherwise the SKU cell when the ingredient is written there and is not just the brand. "
                    "There is no price column, so MRP is empty. The US formulations tab is not an India brand list and was not loaded."
                ),
            ))
    stats = {
        "company": "Alkem Laboratories Ltd.",
        "rows": len(rows),
        "withComposition": len(rows),
        "withPrice": 0,
        "skippedWithoutIngredient": skipped,
        "sourceMenus": seen_menus,
        "brands": len({item["brand"] for item in rows}),
    }
    return rows, stats


# --- Torrent -------------------------------------------------------------

def load_torrent() -> tuple[list[dict], dict]:
    text = (ROOT / "torrent.md").read_text(errors="replace")
    rows = []
    skipped = 0
    for line in text.splitlines():
        if not line.startswith("|"):
            continue
        cells = [clean(cell) for cell in line.strip().strip("|").split("|")]
        if not cells or cells[0] in {"Products", "---"} or set(cells[0]) <= {"-"}:
            continue
        if re.fullmatch(r"-+", cells[0].replace(" ", "")):
            continue
        brand = cells[0]
        drug = cells[1] if len(cells) > 1 else ""
        if brand.lower() == "products":
            continue
        if not ingredientish(drug):
            skipped += 1
            continue
        rows.append(row(
            key=f"torrent-{slug(brand)}-{slug(drug)}",
            name=brand,
            brand=brand,
            composition=drug,
            company="Torrent Pharmaceuticals Ltd.",
            source_name="Torrent India product index",
            source_url="https://www.torrentpharma.com/pi/IN/products/",
            source_detail=(
                "Torrent India prescribing-information index, retrieved 2026-10-27. "
                "The drug-name cell is the composition. The index has no therapy column and no price, so MRP is empty."
            ),
        ))
    stats = {
        "company": "Torrent Pharmaceuticals Ltd.",
        "rows": len(rows),
        "withComposition": len(rows),
        "withPrice": 0,
        "skippedWithoutIngredient": skipped,
        "brands": len({item["brand"] for item in rows}),
    }
    return rows, stats


# --- Intas ---------------------------------------------------------------

def load_intas() -> tuple[list[dict], dict]:
    rows = []
    skipped = 0
    tables = 0
    for section, table in heading_tables(ROOT / "intas.html"):
        if not table or "BRAND" not in table[0][0].upper() or "INGREDIENT" not in " ".join(table[0]).upper():
            continue
        tables += 1
        for cells in table[1:]:
            if len(cells) < 2:
                continue
            brand, ingredient = cells[0], cells[1]
            therapy = cells[2] if len(cells) > 2 else section
            pack = cells[3] if len(cells) > 3 else None
            if not brand or not ingredientish(ingredient):
                skipped += 1
                continue
            rows.append(row(
                key=f"intas-{slug(brand)}-{slug(ingredient)}-{slug(pack or '')}",
                name=brand,
                brand=brand,
                composition=ingredient,
                company="Intas Pharmaceuticals Ltd.",
                pack=pack,
                section=therapy,
                source_name="Intas products table",
                source_url="https://www.intaspharma.com/products/",
                source_detail=(
                    "Intas public products table, retrieved 2026-10-27. "
                    "Active ingredient and strength are the composition. Therapy segment is the company's heading. "
                    "There is no price column, so MRP is empty. Animal-health and biological rows stay because they are on this table; the therapy segment says which."
                ),
            ))
    stats = {
        "company": "Intas Pharmaceuticals Ltd.",
        "rows": len(rows),
        "withComposition": len(rows),
        "withPrice": 0,
        "tables": tables,
        "skippedWithoutIngredient": skipped,
        "brands": len({item["brand"] for item in rows}),
    }
    return rows, stats


# --- Form V (Ipca, Centaur) ----------------------------------------------

WORD_RE = re.compile(r'<word xMin="([\d.]+)" yMin="([\d.]+)"[^>]*>([^<]*)</word>')
NOISE = (
    "scheduled formulation", "own manufactured", "purchased formulation",
    "imported formulation", "table-a", "table-b", "name of the product",
    "composition as", "maximum retail", "proforma", "form - v", "form v",
    "authorized signatory", "notes:-", "place :", "form ref", "production capacity",
    "price to distributor", "price to retailer", "pack size", "gst rate",
)


def pdf_pages(path: Path) -> list[list[tuple[float, float, str]]]:
    bbox = path.with_suffix(path.suffix + ".bbox")
    if not bbox.exists() or bbox.stat().st_mtime < path.stat().st_mtime:
        subprocess.check_call(["pdftotext", "-bbox", str(path), str(bbox)], stdout=subprocess.DEVNULL)
    html_text = bbox.read_text(errors="replace")
    pages = []
    for page in re.findall(r"<page\b[^>]*>(.*?)</page>", html_text, re.S):
        words = []
        for match in WORD_RE.finditer(page):
            words.append((float(match.group(2)), float(match.group(1)), html.unescape(match.group(3))))
        pages.append(words)
    return pages


def column_text(words: list[tuple[float, float, str]], lo: float, hi: float) -> str:
    chosen = [(y, x, text) for y, x, text in words if lo <= x < hi]
    chosen.sort()
    return clean(" ".join(text for _, _, text in chosen))


def marketing_company(words: list[tuple[float, float, str]], fallback: str) -> str:
    header = [(y, x, text) for y, x, text in words if y < 210]
    header.sort()
    text = clean(" ".join(item[2] for item in header))
    match = re.search(r"marketing company, if any\s*:?\s*(.+?)\s+Add\b", text, re.I)
    if not match:
        return fallback
    name = clean(match.group(1))
    name = re.sub(r"^[:\s]+", "", name)
    if len(name) < 4:
        return fallback
    return name


def header_marks(words: list[tuple[float, float, str]]) -> dict[int, float] | None:
    marks = [(y, x, text) for y, x, text in words if re.fullmatch(r"\([1-9]\)|\(1[01]\)", text)]
    if not any(text == "(9)" for _, _, text in marks):
        return None
    anchor = min(y for y, _, text in marks if text == "(1)") if any(text == "(1)" for _, _, text in marks) else marks[0][0]
    found: dict[int, float] = {}
    for y, x, text in marks:
        if abs(y - anchor) > 4:
            continue
        found[int(text.strip("()"))] = x
    if 2 not in found or 3 not in found or 4 not in found or 9 not in found:
        return None
    return found


def column_edges(marks: dict[int, float]) -> dict[str, float]:
    # Header numerals sit in the middle of each column. The ingredient text starts
    # left of the "(3)" mark, so the name/composition split is just past "(2)".
    return {
        "name_hi": marks[2] + 32,
        "comp_hi": (marks[3] + marks[4]) / 2,
        "pack_hi": (marks[4] + marks.get(5, marks[4] + 30)) / 2,
        "mrp_x": marks[9],
    }


def tidy_form_name(name: str) -> str:
    name = clean(name)
    name = re.sub(
        r"^(?:non-)?scheduled formulations\s+|^(?:own manufactured|purchased|imported) formulations\s+|^formulations\s+",
        "",
        name,
        flags=re.I,
    )
    return clean(name)


def load_form_v(path: Path, fallback_company: str, source_name: str, source_url: str, key_prefix: str, default_date: str) -> tuple[list[dict], dict]:
    pages = pdf_pages(path)
    rows = []
    skipped = 0
    edges = {
        "name_hi": 156,
        "comp_hi": 245,
        "pack_hi": 278,
        "mrp_x": 414,
    }
    for page_no, words in enumerate(pages, start=1):
        company = marketing_company(words, fallback_company)
        detected = header_marks(words)
        if detected:
            edges = column_edges(detected)
        serials = sorted(
            (y, text)
            for y, x, text in words
            if x < 52 and y > 30 and re.fullmatch(r"\d+", text)
        )
        for index, (y, serial) in enumerate(serials):
            previous = serials[index - 1][0] if index else max(0, y - 24)
            nxt = serials[index + 1][0] if index + 1 < len(serials) else y + 24
            y0 = (previous + y) / 2
            y1 = (y + nxt) / 2
            group = [(yy, x, text) for yy, x, text in words if y0 < yy <= y1]
            blob = " ".join(text for _, _, text in group).lower()
            if "name of the product" in blob or "composition as approved" in blob:
                continue
            name = tidy_form_name(column_text(group, 52, edges["name_hi"]))
            composition = column_text(group, edges["name_hi"], edges["comp_hi"])
            composition = re.sub(r"\([^)]*\)", " ", composition)
            composition = re.sub(r"^(?:No\)\s*)+", "", composition)
            composition = clean(composition)
            pack = column_text(group, edges["comp_hi"], edges["pack_hi"])
            mrp_words = [
                (x, text)
                for yy, x, text in group
                if abs(x - edges["mrp_x"]) <= 22 and re.search(r"\d", text) and not re.search(r"[A-Za-z]", text)
            ]
            mrp = None
            if mrp_words:
                mrp_words.sort(key=lambda item: abs(item[0] - edges["mrp_x"]))
                mrp = money(mrp_words[0][1])
                if mrp and mrp >= 1_000_000:
                    mrp = None
            if not name or not ingredientish(composition):
                skipped += 1
                continue
            date = default_date
            wef = re.search(r"WEF(\d{2})(\d{2})(\d{4})", " ".join(text for _, _, text in group))
            if wef:
                date = f"{wef.group(3)}-{wef.group(2)}-{wef.group(1)}"
            detail = (
                f"{source_name}, retrieved 2026-10-27. "
                "Composition is the Drug Control Authorities column. "
                + (
                    f"MRP ₹{mrp:.2f} is column 9, inclusive of taxes, as of {date}."
                    if mrp else
                    "This row did not yield a readable MRP in column 9, so the price was left empty rather than guessed."
                )
            )
            if company != fallback_company:
                detail += f" Marketing company on the page: {company}."
            rows.append(row(
                key=f"{key_prefix}-p{page_no}-{serial}-{slug(name)}",
                name=name,
                composition=composition,
                company=company,
                pack=pack,
                source_name=source_name,
                source_url=source_url,
                source_detail=detail,
                mrp=mrp,
                price_as_of=date if mrp else None,
                price_source=source_url if mrp else None,
            ))
    stats = {
        "company": fallback_company,
        "rows": len(rows),
        "withComposition": len(rows),
        "withPrice": sum(1 for item in rows if item["mrp"]),
        "skippedWithoutIngredient": skipped,
        "brands": len({item["brand"] for item in rows}),
        "companiesOnTheForm": sorted({item["company"] for item in rows}),
    }
    return rows, stats


def load_ipca() -> tuple[list[dict], dict]:
    return load_form_v(
        ROOT / "ipca.pdf",
        "Ipca Laboratories Limited",
        "Ipca Form V domestic price list effective 22 September 2025",
        "https://www.ipca.com/wp-content/pdf/revised-pricelist-of-all-domestic-formulations-applicable-22-09-2025.pdf",
        "ipca",
        "2025-09-22",
    )


def load_centaur() -> tuple[list[dict], dict]:
    rows, stats = load_form_v(
        ROOT / "more" / "centaur-gst.pdf",
        "Centaur Pharmaceuticals Private Limited",
        "Centaur Form V, Ref/IPDMS/Form/5/206, dated 20 September 2025",
        "https://www.centaurpharma.com/downloads/Form-5-Scheduled-Formulations-with-revised-GST-5.pdf",
        "centaur",
        "2025-09-20",
    )
    stats["note"] = (
        "One public Form V file (9 pages). Centaur's downloads index did not return a full file list, "
        "so this is not every Centaur brand."
    )
    return rows, stats


def count_zydus_india_dose_rows() -> dict:
    path = ROOT / "other" / "zydus.html"
    if not path.exists():
        return {"seen": None, "loaded": 0, "reason": "Zydus page was not in the snapshot."}
    text = path.read_text(errors="replace")
    text = re.sub(r"<script[\s\S]*?</script>", "", text, flags=re.I)
    parts = re.split(r"(<h2[^>]*>[\s\S]*?</h2>|<table[\s\S]*?</table>)", text, flags=re.I)
    in_india = False
    dose_rows = 0
    ingredient_rows = 0
    for part in parts:
        if re.match(r"<h2", part, re.I):
            heading = clean(re.sub(r"<[^>]+>", " ", part))
            in_india = "India Formulations" in heading
            if heading.startswith("Zydus Biologics") or "GENERICS" in heading.upper() or heading == "International":
                in_india = False
        elif in_india and part.lower().startswith("<table"):
            parser = TableParser()
            parser.feed(part)
            if not parser.tables:
                continue
            table = parser.tables[0]
            if not table or "STRENGTH" not in " ".join(table[0]).upper():
                continue
            for cells in table[1:]:
                strength = cells[3] if len(cells) > 3 else ""
                if ingredientish(strength):
                    ingredient_rows += 1
                else:
                    dose_rows += 1
    return {
        "indiaFormulationRowsWhoseStrengthIsOnlyADose": dose_rows,
        "indiaFormulationRowsWhoseStrengthNamesAnIngredient": ingredient_rows,
        "loaded": 0,
        "reason": (
            "Zydus Lifesciences' India formulations table prints brand, SKU, and a strength that is a dose "
            "(for example 20 MG), not the ingredient. Those rows were not loaded. "
            "Export tables that do name a generic were not copied onto the India brands. "
            "Zydus and Glenmark GST MRP lists have no composition column, so no price was invented."
        ),
    }


def load_company_products() -> tuple[list[dict], dict]:
    loaders = [load_sun, load_mankind, load_cipla, load_alkem, load_torrent, load_intas, load_ipca, load_centaur]
    products: list[dict] = []
    by_company = []
    for loader in loaders:
        rows, stats = loader()
        products.extend(rows)
        by_company.append(stats)
    # Last line of defence: drop anything that still has no ingredient or a made-up price flag.
    kept = []
    seen_keys = set()
    for item in products:
        if not ingredientish(item["composition"]):
            continue
        if item["mrp"] is not None and not item["priceSource"]:
            item["mrp"] = None
        if item["key"] in seen_keys:
            continue
        seen_keys.add(item["key"])
        kept.append(item)
    notes = {stats["company"]: stats for stats in by_company}
    refreshed = []
    seen_companies = []
    for item in kept:
        if item["company"] not in seen_companies:
            seen_companies.append(item["company"])
    for company in seen_companies:
        items = [item for item in kept if item["company"] == company]
        base = dict(notes.get(company, {}))
        base["company"] = company
        base["rows"] = len(items)
        base["withComposition"] = len(items)
        base["withPrice"] = sum(1 for item in items if item["mrp"])
        base["brands"] = len({item["brand"] for item in items})
        refreshed.append(base)
    report = {
        "byCompany": refreshed,
        "companyRows": len(kept),
        "companyRowsWithComposition": len(kept),
        "companyRowsWithPrice": sum(1 for item in kept if item["mrp"]),
        "distinctCompanies": len(seen_companies),
        "distinctBrands": len({(item["company"], item["brand"]) for item in kept}),
        "zydusNotLoaded": count_zydus_india_dose_rows(),
        "checkedButNotLoaded": [
            "Dr. Reddy's September 2026 finished-formulations PDF is a generic-name list (abiraterone tablets, and so on), not India brands. No brand was invented.",
            "Lupin's public product finder is a search page. The HTML retrieved on 2026-10-27 did not contain a composition table.",
            "Glenmark's GST 2.0 PDF prints pack and MRP and does not print composition. Those prices were not given an invented ingredient.",
            "Abbott's A–Z index is one page per product, not a composition table. It was not row-counted.",
            "Macleods' public home page did not expose a composition table in this pass.",
            "Zydus India formulations: strength is a dose, not an ingredient. See zydusNotLoaded.",
            "NPPA Pharma Sahi Daam, IPDMS, MIMS/CIMS, and retail pharmacies were not scraped.",
        ],
    }
    return kept, report


if __name__ == "__main__":
    import json
    rows, report = load_company_products()
    print(json.dumps(report, indent=2))
