#!/usr/bin/env python3
"""Parse public-core Indian medicine lists into seed JSON for Vaidya.

Sources, retrieved 27 October 2026:
- CDSCO National List of Essential Medicines 2022 PDF (facts extracted, PDF not shipped)
- PIB annexure of 2,110 PMBJP generic medicines as of 31 December 2025
- Three brand samples copied from the project source note (Sun Pantocid, Mankind Moxikind-CV, Zady)

This is not the ~60,000-brand retail market. Company rows are added only when a
public table prints the active ingredient. A price is copied only when that
source, or a same-company price list with a unique pack-name match, prints an
MRP. Disease tags other than a printed section heading are inferred.
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from company_catalogs import load_company_products

NLEM_TEXT = Path("/tmp/medsrc/nlem2022.txt")
PMBJP_TEXT = Path("/tmp/medsrc/pmbjp.txt")
OUT = Path("/workspace/doctor-app/DoctorApp/Data/Seed/catalog.json")

RETRIEVED = "2026-10-27"
NLEM_URL = "https://cdsco.gov.in/opencms/resources/UploadCDSCOWeb/2018/UploadConsumer/nlem2022.pdf"
PMBJP_URL = "https://static.pib.gov.in/WriteReadData/specificdocs/documents/2026/feb/doc202626781701.pdf"
PMBJP_COMPANY = "Pradhan Mantri Bhartiya Janaushadhi Pariyojana (generic; not a commercial brand)"

TOP_SECTIONS = {
    "1": "Medicines used in anaesthesia",
    "2": "Analgesics, antipyretics, NSAIDs, gout and rheumatoid disorders",
    "3": "Antiallergics and medicines used in anaphylaxis",
    "4": "Antidotes and other substances used in poisonings",
    "5": "Medicines used in neurological disorders",
    "6": "Anti-infective medicines",
    "7": "Anti-cancer agents, immunosuppressives and palliative care",
    "8": "Medicines affecting the blood",
    "9": "Blood products and plasma substitutes",
    "10": "Cardiovascular medicines",
    "11": "Dermatological medicines (topical)",
    "12": "Diagnostic agents",
    "13": "Dialysis components",
    "14": "Antiseptics and disinfectants",
    "15": "Diuretics",
    "16": "Ear, nose and throat medicines",
    "17": "Gastrointestinal medicines",
    "18": "Hormones, other endocrine medicines and contraceptives",
    "19": "Immunologicals",
    "20": "Medicines for neonatal care",
    "21": "Ophthalmological medicines",
    "22": "Oxytocics and antioxytocics",
    "23": "Medicines used in psychiatric disorders",
    "24": "Medicines acting on the respiratory tract",
    "25": "Solutions correcting water, electrolyte and acid-base disturbances",
    "26": "Vitamins and minerals",
    "27": "Medicines for COVID-19 management",
}

CODE_RE = re.compile(r"^(\d+(?:\.\d+)+)\b\s*(.*)$")
HEADING_RE = re.compile(r"^(\d+(?:\.\d+)*)\s*[-–]\s*(.+)$")
CARE_RE = re.compile(r"(?:P,S,T|S,T|P,S|(?<![A-Za-z])T(?![A-Za-z]))")
DOSAGE_RE = re.compile(
    r"^(Tablet|Tablets|Capsule|Capsules|Injection|Oral|Powder|Cream|Ointment|Drops|"
    r"Drop|Liquid|Solution|Suppository|Inhalation|Spray|Gel|Lotion|Pessary|Patch|"
    r"Granules|Syrup|Suspension|Chewable|Modified|Effervescent|Enteric|As licensed|"
    r"Combi|Dry |Nasal|Eye|Ear|Respule|Infusion|Mouth|Dental|Elixir|Emulsion|"
    r"Liniment|Shampoo|Aerosol|Nebul|Rectal|Vaginal|Implant|Lozenge|Topical|"
    r"Sachet|Enema|Paint|b\)|a\)|IV |Intravenous|Dispersible|Enteric|coated|"
    r"Liquid for|Powder for|Capsule/Tablet|Only for|for prophylaxis)",
    re.I,
)


def clean_name(name: str) -> str:
    name = name.replace("\n", " ")
    name = re.sub(r"\*+", "", name)
    name = re.sub(r"\s+", " ", name).strip(" -–,")
    name = re.sub(r"\s+\(p\)$", "", name, flags=re.I)
    return name


def is_name_fragment(raw: str) -> bool:
    if not raw.strip():
        return False
    indent = len(raw) - len(raw.lstrip(" "))
    text = raw.strip()
    if text.startswith("*") or text.startswith("•"):
        return False
    if indent > 28 or indent < 1:
        return False
    if len(text) > 70:
        return False
    if CODE_RE.match(text):
        return False
    if HEADING_RE.match(text) and "P,S,T" not in text:
        return False
    lowered = text.lower()
    if any(w in lowered for w in ("section ", "also listed", "level of", "dosage form", "healthcare", "alphabetical")):
        return False
    if DOSAGE_RE.match(text):
        return False
    if not re.search(r"[A-Za-z]", text):
        return False
    return True


def is_dosage_line(raw: str) -> bool:
    text = raw.strip()
    if not text or text.startswith("*"):
        return False
    indent = len(raw) - len(raw.lstrip(" "))
    if CODE_RE.match(text) or HEADING_RE.match(text):
        return False
    if indent >= 36 and re.search(r"[A-Za-z0-9]", text):
        return True
    return bool(DOSAGE_RE.match(text))


def parse_nlem(_text: str) -> list[dict]:
    """Read word positions so name, care level, and strength stay in their columns."""
    html = Path("/tmp/medsrc/nlem.bbox.html").read_text(errors="replace")
    pages = re.findall(r"<page\b[^>]*>(.*?)</page>", html, re.S)
    word_re = re.compile(r'<word xMin="([\d.]+)" yMin="([\d.]+)"[^>]*>([^<]*)</word>')
    lines = []
    for pi, page in enumerate(pages):
        words = []
        for match in word_re.finditer(page):
            words.append((float(match.group(2)), float(match.group(1)), match.group(3).replace("&amp;", "&")))
        words.sort()
        current = None
        for y, x, text in words:
            if current and abs(y - current[0]) <= 2.2:
                current[1].append((x, text))
            else:
                current = [y, [(x, text)]]
                lines.append((pi, y, current[1]))
    # lines may have duplicated list refs; copy
    norm = []
    for pi, y, ws in lines:
        ws = sorted(ws, key=lambda item: item[0])
        norm.append((pi, y, ws))

    headings: dict[str, str] = {}
    entries = []
    started = False
    bucket = None

    def words_in(ws, lo, hi):
        return [t for x, t in ws if lo <= x < hi]

    def flush():
        nonlocal bucket
        if not bucket:
            return
        name = clean_name(" ".join(bucket["names"]))
        name = re.split(r"\bformulations\b", name, flags=re.I)[0]
        name = re.sub(r"\b(Medicine|Healthcare|Level of)\b", "", name, flags=re.I)
        name = clean_name(name)
        strengths = []
        for part in bucket["doses"]:
            part = re.sub(r"\s+", " ", part).strip(" ;,")
            if part and part.lower() not in {"level of", "healthcare", "medicine", "dosage form(s) and strength(s)"} and part not in strengths:
                strengths.append(part)
        code = bucket["code"]
        if name and len(name) >= 2 and not name.lower().startswith("section "):
            top = code.split(".")[0]
            bits = []
            if top in TOP_SECTIONS:
                bits.append(TOP_SECTIONS[top])
            parts = code.split(".")
            for i in range(len(parts)):
                key = ".".join(parts[: i + 1])
                title = headings.get(key)
                if title and title not in bits:
                    bits.append(title)
            specific = bits[-1] if bits else ""
            entries.append(
                {
                    "key": f"nlem2022-{code}",
                    "name": name,
                    "brand": None,
                    "company": None,
                    "form": strengths[0].split(" ")[0] if strengths else None,
                    "composition": (name + ". " + "; ".join(strengths)) if strengths else name,
                    "pack": None,
                    "care": bucket["care"],
                    "sectionCode": code,
                    "sectionTitle": specific,
                    "kind": "nlem",
                    "sourceName": "National List of Essential Medicines 2022",
                    "sourceUrl": NLEM_URL,
                    "sourceDetail": (
                        f"CDSCO NLEM 2022, item {code}"
                        + (f", section {specific}" if specific else "")
                        + f". Care level {bucket['care'] or 'not on the row'}. "
                        f"Name, dose form and strength extracted on {RETRIEVED}. The PDF is not redistributed."
                    ),
                    "retrieved": RETRIEVED,
                }
            )
        bucket = None

    code_only = re.compile(r"^\d+(?:\.\d+)+$")
    care_token = re.compile(r"^(?:P,S,T|S,T|P,S|T)$")
    loose_names: list[tuple[float, list[str]]] = []

    def is_footnote(texts: str, ws) -> bool:
        low = texts.lower()
        if "also listed" in low or "formulations are" in low:
            return True
        if ws and ws[0][1].startswith("*"):
            return True
        return False

    def name_only(ws) -> bool:
        if not ws:
            return False
        return all(145 <= x < 280 for x, _ in ws)

    for pi, y, ws in norm:
        texts = " ".join(t for _, t in ws)
        if "Alphabetical List" in texts and started:
            break
        if is_footnote(texts, ws):
            loose_names.clear()
            continue
        first = ws[0][1] if ws else ""
        heading_code = None
        if re.match(r"^\d+(?:\.\d+)*[-–]", first):
            heading_code = re.match(r"^(\d+(?:\.\d+)*)", first).group(1)
        elif re.match(r"^Section\s+(\d+)", texts):
            heading_code = re.match(r"^Section\s+(\d+)", texts).group(1)
        if heading_code and not any(code_only.match(t) for _, t in ws):
            title = clean_name(re.sub(r"^Section\s+\d+\s*", "", texts))
            title = re.sub(r"^\d+(?:\.\d+)*\s*[-–]\s*", "", title)
            if title:
                headings[heading_code] = title
            loose_names.clear()
            continue
        code_words = [(x, t) for x, t in ws if x < 145 and code_only.match(t)]
        care_words = [t for x, t in ws if 270 <= x < 370 and care_token.match(t)]
        dose_here = " ".join(words_in(ws, 365, 999)).strip()
        if code_words and (care_words or dose_here):
            if not started and code_words[0][1] != "1.1.1":
                if code_words[0][1].count(".") == 0:
                    continue
            started = True
            flush()
            code_y = y
            names = []
            kept = []
            for name_y, bits in loose_names:
                if 0 <= code_y - name_y <= 22:
                    names.extend(bits)
                else:
                    kept.append((name_y, bits))
            loose_names[:] = kept
            names += [t for t in words_in(ws, 145, 280) if t.lower() != "medicine"]
            bucket = {
                "code": code_words[0][1],
                "y": code_y,
                "names": names,
                "care": " ".join(care_words),
                "doses": [dose_here] if dose_here else [],
            }
            continue
        if name_only(ws) and not any(code_only.match(t) for _, t in ws):
            bits = [t for _, t in ws if t.lower() not in {"medicine", "level", "of", "healthcare"}]
            if bucket and started and abs(y - bucket["y"]) <= 22:
                bucket["names"].extend(bits)
            elif bits:
                loose_names.append((y, bits))
            continue
        if bucket and started:
            dose_bits = words_in(ws, 365, 999)
            if dose_bits:
                joined = " ".join(dose_bits).strip()
                if joined and joined.lower() not in {"dosage form(s) and strength(s)"}:
                    bucket["doses"].append(joined)
    flush()
    seen = set()
    unique = []
    for entry in entries:
        if entry["key"] in seen:
            continue
        var_name = entry["name"].strip().lower()
        if len(var_name) < 4 or var_name in {"and", "salts"} or var_name.startswith("analgesics,"):
            continue
        seen.add(entry["key"])
        unique.append(entry)
    return unique


def parse_pmbjp(text: str) -> list[dict]:
    items = []
    current = None

    def flush():
        nonlocal current
        if not current:
            return
        name = re.sub(r"\s+", " ", current["name"]).strip()
        pack = re.sub(r"\s+", " ", current["pack"]).strip()
        if name:
            items.append(
                {
                    "key": f"pmbjp-{current['code']}",
                    "name": name,
                    "brand": None,
                    "company": PMBJP_COMPANY,
                    "form": None,
                    "composition": name,
                    "pack": pack or None,
                    "care": None,
                    "sectionCode": None,
                    "sectionTitle": None,
                    "kind": "janaushadhi",
                    "sourceName": "PMBJP generic medicine annexure as of 31 December 2025",
                    "sourceUrl": PMBJP_URL,
                    "sourceDetail": (
                        f"PIB annexure, drug code {current['code']}, serial {current['serial']}. "
                        "The December 2025 name list has no therapeutic-category column, so class and disease tags are inferred. "
                        f"Retrieved {RETRIEVED}. Not a commercial brand and not the full Indian market."
                    ),
                    "retrieved": RETRIEVED,
                }
            )
        current = None

    row_re = re.compile(r"^(\d+)\s+(\d+)\s+(.+)$")
    for raw in text.splitlines():
        line = raw.replace("\f", "").rstrip()
        if not line.strip():
            continue
        if "Generic Name of Item" in line or line.strip().startswith("S. No.") or line.strip() in {"Code", "Drug"}:
            continue
        if line.strip().startswith("This information was given"):
            break
        match = row_re.match(line.strip())
        if match and len(match.group(1)) <= 4:
            flush()
            rest = match.group(3)
            # Split name and pack on a wide gap if present in the raw line.
            wide = re.split(r"\s{2,}", line.strip(), maxsplit=3)
            # wide: serial, code, name, maybe pack — but regex already ate single spaces.
            # Use the original spacing after the code.
            after = line.strip()
            after = re.sub(r"^\d+\s+\d+\s+", "", after)
            parts = re.split(r"\s{2,}", after, maxsplit=1)
            name = parts[0].strip()
            pack = parts[1].strip() if len(parts) > 1 else ""
            current = {"serial": match.group(1), "code": match.group(2), "name": name, "pack": pack}
            continue
        if current and (line.startswith(" ") or line.startswith("\t")):
            cont_parts = re.split(r"\s{2,}", line.strip(), maxsplit=1)
            current["name"] += " " + cont_parts[0].strip()
            if len(cont_parts) > 1 and not current["pack"]:
                current["pack"] = cont_parts[1].strip()
            elif len(cont_parts) > 1 and current["pack"] and len(cont_parts[1]) > len(current["pack"]):
                current["pack"] = cont_parts[1].strip()
    flush()
    # Unique by drug code, keep first.
    seen = set()
    unique = []
    for item in items:
        if item["key"] in seen:
            continue
        seen.add(item["key"])
        unique.append(item)
    return unique


def samples() -> list[dict]:
    common = {
        "kind": "brand-sample",
        "retrieved": RETRIEVED,
        "care": None,
        "sectionCode": None,
    }
    return [
        {
            **common,
            "key": "sun-pantocid-40-tab-15",
            "name": "Pantocid 40 mg Tablet 15",
            "brand": "Pantocid",
            "company": "Sun Pharmaceutical Industries Ltd.",
            "form": "Tablet",
            "composition": "Pantoprazole 40 mg",
            "pack": "15",
            "sectionTitle": "GASTRO INTESTINAL",
            "sourceName": "Sun Pharma India products table, therapy group GASTRO INTESTINAL",
            "sourceUrl": "https://sunpharma.com/india-products/",
            "sourceDetail": "Cited brand sample from the company public products table, retrieved 2026-10-27. Not Sun's full catalog.",
        },
        {
            **common,
            "key": "mankind-moxikind-cv",
            "name": "Moxikind-CV",
            "brand": "Moxikind-CV",
            "company": "Mankind Pharma Limited",
            "form": None,
            "composition": "Amoxicillin trihydrate + clavulanic acid potassium salt (strength not in the source row)",
            "pack": None,
            "sectionTitle": None,
            "sourceName": "Mankind all-products page and division-wise brand list",
            "sourceUrl": "https://www.mankindpharma.com/products/all-products/",
            "sourceDetail": "Cited brand sample. Strength is absent from the public brand row. Also listed in the December 2024 product PDF. Not Mankind's full catalog.",
        },
        {
            **common,
            "key": "mankind-zady",
            "name": "Zady",
            "brand": "Zady",
            "company": "Mankind Pharma Limited",
            "form": None,
            "composition": "Azithromycin (strength not in the source row)",
            "pack": None,
            "sectionTitle": None,
            "sourceName": "Mankind brand list, division FUTURE",
            "sourceUrl": "https://www.mankindpharma.com/wp-content/uploads/2024/12/Product-List-1.pdf",
            "sourceDetail": "Cited brand sample from the Mankind division-wise brand PDF. Strength is absent. Not Mankind's full catalog.",
        },
    ]


# Disease tree: ICD-11 MMS chapters as categories, clinical search terms as subcategories.
# Citation: World Health Organization ICD-11 for Mortality and Morbidity Statistics.
TAXONOMY = [
    ("infectious", "Infectious or parasitic diseases", "ICD-11 chapter 01", [
        ("malaria", "Malaria", "1F40-1F45", "malaria, plasmodium"),
        ("dengue", "Dengue", "1D20-1D2Z", "dengue, breakbone"),
        ("typhoid", "Typhoid and enteric fever", "1A07", "typhoid, enteric fever"),
        ("tuberculosis", "Tuberculosis", "1B10-1B1Z", "tuberculosis, tb, koch"),
        ("leprosy", "Leprosy", "1B20", "leprosy, hansen"),
        ("hiv", "HIV", "1C60", "hiv, aids"),
        ("uti", "Urinary tract infection", "GC08", "uti, urine infection, urinary"),
        ("respiratory-infection", "Lower respiratory infection", "CA40", "respiratory infection, pneumonia, chest infection"),
        ("sore-throat", "Sore throat and pharyngitis", "CA02", "sore throat, pharyngitis, throat pain"),
        ("gi-infection", "Gastrointestinal infection", "1A40", "gastroenteritis, food poisoning"),
        ("amoebiasis", "Amoebiasis", "1A36", "amoebiasis, amebiasis"),
        ("skin-infection", "Skin and soft-tissue infection", "1B70", "cellulitis, abscess, skin infection"),
        ("helminth", "Helminth infection", "1F90", "worm, helminth, deworming"),
        ("filariasis", "Filariasis", "1F66", "filaria, filariasis"),
        ("fungal", "Fungal infection", "1F23", "fungal, ringworm, candida"),
        ("herpes", "Herpes simplex and zoster", "1F00", "herpes, zoster, shingles"),
        ("sti", "Sexually transmitted infection", "1A90", "sti, std"),
        ("covid", "COVID-19", "RA01", "covid, covid-19, coronavirus"),
        ("sepsis", "Sepsis", "1G40", "sepsis, septicaemia"),
    ]),
    ("neoplasm", "Neoplasms", "ICD-11 chapter 02", [
        ("cancer-support", "Cancer supportive care", "2D4Z", "cancer, tumour, chemotherapy support"),
        ("palliative", "Palliative symptoms", "QB00", "palliative, end of life"),
    ]),
    ("blood", "Blood and immune disorders", "ICD-11 chapters 03 and 04", [
        ("anaemia", "Iron deficiency anaemia", "3A00", "anaemia, anemia, iron deficiency"),
        ("thrombosis", "Coagulation and thrombosis", "3B10", "thrombosis, dvt, anticoagulation"),
        ("allergy", "Allergy and anaphylaxis", "4A80", "allergy, anaphylaxis, urticaria"),
        ("rheumatoid", "Rheumatoid arthritis", "FA20", "rheumatoid, ra"),
    ]),
    ("endocrine", "Endocrine, nutritional and metabolic", "ICD-11 chapter 05", [
        ("diabetes-2", "Type 2 diabetes", "5A11", "diabetes, type 2, sugar, madhumeh"),
        ("diabetes-1", "Type 1 diabetes and insulin", "5A10", "type 1, insulin"),
        ("hypothyroid", "Hypothyroidism", "5A00", "thyroid, hypothyroid"),
        ("adrenal", "Adrenal insufficiency", "5A70", "adrenal, addison"),
        ("gout", "Gout", "FA25", "gout, uric acid"),
        ("dyslipidaemia", "Dyslipidaemia", "5C80", "cholesterol, lipid, dyslipidemia"),
        ("vitamin", "Vitamin deficiency", "5B50", "vitamin deficiency"),
        ("electrolyte", "Mineral and electrolyte deficiency", "5C64", "electrolyte, hypokalemia, calcium"),
    ]),
    ("mental", "Mental and behavioural disorders", "ICD-11 chapter 06", [
        ("depression", "Depression", "6A70", "depression"),
        ("anxiety", "Anxiety", "6B00", "anxiety"),
        ("psychosis", "Psychosis", "6A20", "psychosis, schizophrenia"),
        ("bipolar", "Bipolar disorder", "6A60", "bipolar, mania"),
        ("insomnia", "Insomnia", "7A00", "insomnia, sleep"),
        ("alcohol", "Alcohol withdrawal", "6C40", "alcohol withdrawal"),
    ]),
    ("nervous", "Nervous system", "ICD-11 chapter 08", [
        ("epilepsy", "Epilepsy", "8A60", "epilepsy, seizure, convulsion"),
        ("migraine", "Migraine", "8A80", "migraine, headache"),
        ("parkinson", "Parkinson disease", "8A00", "parkinson"),
        ("dementia", "Dementia", "6D80", "dementia, alzheimer"),
        ("neuropathic-pain", "Neuropathic pain", "8E43", "neuropathic, neuralgia"),
    ]),
    ("eye", "Visual system", "ICD-11 chapter 09", [
        ("conjunctivitis", "Conjunctivitis", "9A60", "conjunctivitis, eye infection, red eye"),
        ("glaucoma", "Glaucoma", "9C61", "glaucoma"),
        ("eye-inflammation", "Ocular inflammation", "9A20", "uveitis, eye inflammation"),
    ]),
    ("ent", "Ear and mastoid", "ICD-11 chapter 10", [
        ("otitis", "Otitis externa and media", "AA00", "otitis, ear infection, earache"),
        ("vertigo", "Vertigo", "AB30", "vertigo, giddiness"),
    ]),
    ("circulatory", "Circulatory system", "ICD-11 chapter 11", [
        ("hypertension", "Hypertension", "BA00", "hypertension, high bp"),
        ("heart-failure", "Heart failure", "BD10", "heart failure, chf"),
        ("ihd", "Ischaemic heart disease", "BA40", "angina, heart attack, ihd, cad"),
        ("arrhythmia", "Arrhythmia", "BC60", "arrhythmia, afib"),
        ("shock", "Shock", "MG40", "shock"),
    ]),
    ("respiratory", "Respiratory system", "ICD-11 chapter 12", [
        ("asthma", "Asthma", "CA23", "asthma, wheeze"),
        ("copd", "COPD", "CA22", "copd, chronic bronchitis"),
        ("cough", "Cough", "MD12", "cough"),
        ("rhinitis", "Allergic rhinitis", "CA08", "rhinitis, cold allergy, sneezing"),
    ]),
    ("digestive", "Digestive system", "ICD-11 chapter 13", [
        ("reflux", "Acid reflux and peptic ulcer", "DA22", "acidity, reflux, gerd, ulcer, gas"),
        ("nausea", "Nausea and vomiting", "MD90", "nausea, vomiting, ulti"),
        ("constipation", "Constipation", "DD91", "constipation"),
        ("diarrhoea", "Diarrhoea", "DD90", "diarrhoea, diarrhea, loose motion"),
        ("ibd", "Inflammatory bowel disease", "DD70", "ibd, ulcerative colitis"),
    ]),
    ("skin", "Skin", "ICD-11 chapter 14", [
        ("dermatitis", "Dermatitis and eczema", "EA80", "dermatitis, eczema, itch"),
        ("fungal-skin", "Fungal skin infection", "1F28", "ringworm, daad, fungal skin"),
        ("scabies", "Scabies", "1G04", "scabies"),
        ("psoriasis", "Psoriasis", "EA90", "psoriasis"),
    ]),
    ("msk", "Musculoskeletal", "ICD-11 chapter 15", [
        ("pain", "Acute pain and inflammation", "MG30", "pain, body pain, inflammation"),
        ("osteoarthritis", "Osteoarthritis", "FA00", "osteoarthritis, knee pain"),
        ("spasm", "Muscle spasm", "FB30", "spasm, muscle spasm"),
    ]),
    ("genitourinary", "Genitourinary and sexual health", "ICD-11 chapters 16 and 17", [
        ("bph", "Benign prostatic hyperplasia", "GA90", "bph, prostate"),
        ("menstrual", "Menstrual pain", "GA34", "menstrual, dysmenorrhoea, period pain"),
    ]),
    ("pregnancy", "Pregnancy, childbirth and contraception", "ICD-11 chapter 18", [
        ("labour", "Labour and postpartum haemorrhage", "JA43", "labour, pph, postpartum"),
        ("preeclampsia", "Pre-eclampsia", "JA24", "preeclampsia, eclampsia"),
        ("contraception", "Contraception", "QA21", "contraception, family planning"),
        ("pregnancy-nausea", "Nausea in pregnancy", "JA60", "morning sickness"),
    ]),
    ("injury", "Injury, poisoning and envenomation", "ICD-11 chapter 22", [
        ("poisoning", "Poisoning", "NE60", "poisoning, overdose"),
        ("envenomation", "Snake and scorpion envenomation", "NE61", "snake bite, scorpion"),
    ]),
    ("symptoms", "Symptoms and signs", "ICD-11 chapter 21", [
        ("fever", "Fever", "MG26", "fever, pyrexia, temperature, bukhar, jwar"),
        ("dehydration", "Dehydration", "5C70", "dehydration, ors"),
    ]),
    ("perioperative", "Anaesthesia and perioperative care", "NLEM section 1 (procedure category, not an ICD-11 disease chapter)", [
        ("general-anaesthesia", "General anaesthesia", "", "anaesthesia, anesthesia"),
        ("local-anaesthesia", "Local anaesthesia", "", "local anaesthesia"),
        ("muscle-relaxant", "Muscle relaxation", "", "muscle relaxant"),
        ("preop-sedation", "Preoperative sedation", "", "preoperative, sedation"),
    ]),
]


def taxonomy_payload():
    categories = []
    for index, (slug, name, icd, subs) in enumerate(TAXONOMY, start=1):
        categories.append(
            {
                "slug": slug,
                "name": name,
                "icd11": icd,
                "sort": index,
                "citation": "World Health Organization. ICD-11 for Mortality and Morbidity Statistics. Chapter used as the category; subcategories are clinical search terms under that chapter.",
                "subcategories": [
                    {"slug": s, "name": n, "icd11": code, "aliases": aliases, "sort": i}
                    for i, (s, n, code, aliases) in enumerate(subs, start=1)
                ],
            }
        )
    return categories


def tag(label, origin, sub=None, note=None):
    return {"label": label, "origin": origin, "sub": sub, "note": note}


def section_class_tags(section_title: str | None) -> list[dict]:
    if not section_title:
        return []
    text = section_title.lower()
    tags = [tag(section_title, "section", note="NLEM section or subsection heading")]
    checks = [
        ("antipyretic", "antipyretic"),
        ("analgesic", "analgesic"),
        ("nsaid", "nsaid"),
        ("antibacterial", "antibacterial"),
        ("antimalarial", "antimalarial"),
        ("antifungal", "antifungal"),
        ("antiviral", "antiviral"),
        ("anthelminth", "anthelminthic"),
        ("antituberculosis", "antituberculosis"),
        ("antileprosy", "antileprosy"),
        ("anticonvulsant", "anticonvulsant"),
        ("antiepileptic", "anticonvulsant"),
        ("antimigraine", "antimigraine"),
        ("antiparkinson", "antiparkinsonism"),
        ("cardiovascular", "cardiovascular"),
        ("gastrointestinal", "gastrointestinal"),
        ("diuretic", "diuretic"),
        ("covid", "covid-19 section"),
    ]
    for needle, label in checks:
        if needle in text:
            tags.append(tag(label, "section", note="Token taken from the NLEM section heading"))
    return tags


INFER_NOTE = "Inferred from the ingredient or NLEM class. Not an Indian product-licence indication."

# (subslug, regex, extra note)
NAME_RULES: list[tuple[str, str, str]] = [
    ("malaria", r"\b(artemether|artesunate|lumefantrine|primaquine|quinine|mefloquine)\b", "Antimalarial ingredient"),
    ("malaria", r"(?<!hydroxy)\bchloroquine\b", "Chloroquine, not hydroxychloroquine"),
    ("tuberculosis", r"\b(isoniazid|rifampicin|ethambutol|pyrazinamide|bedaquiline|delamanid|ethionamide)\b", "Antitubercular ingredient"),
    ("leprosy", r"\b(dapsone|clofazimine)\b", "Antileprosy ingredient"),
    ("hiv", r"\b(tenofovir|lamivudine|zidovudine|efavirenz|dolutegravir|abacavir|nevirapine|atazanavir|ritonavir|emtricitabine)\b", "Antiretroviral ingredient"),
    ("uti", r"\b(nitrofurantoin|norfloxacin|fosfomycin)\b", "Common urinary antibacterial"),
    ("helminth", r"\b(albendazole|mebendazole|pyrantel|praziquantel)\b", "Anthelminthic ingredient"),
    ("filariasis", r"\b(diethylcarbamazine|ivermectin)\b", "Antifilarial ingredient"),
    ("fungal", r"\b(fluconazole|itraconazole|clotrimazole|terbinafine|griseofulvin|nystatin|amphotericin)\b", "Antifungal ingredient"),
    ("fungal-skin", r"\b(clotrimazole|terbinafine|miconazole)\b", "Topical or skin antifungal"),
    ("herpes", r"\b(acyclovir|aciclovir|valacyclovir|valganciclovir)\b", "Antiherpes ingredient"),
    ("amoebiasis", r"\b(metronidazole|tinidazole|ornidazole|secnidazole)\b", "Antiamoebic nitroimidazole"),
    ("gi-infection", r"\b(metronidazole|tinidazole|ornidazole|ofloxacin|norfloxacin)\b", "Used in gut infection; inferred"),
    ("typhoid", r"\b(cefixime|ceftriaxone|azithromycin|ofloxacin)\b", "Often used for enteric fever in India; inferred, not a licence sentence"),
    ("sore-throat", r"\b(amoxycillin|amoxicillin|azithromycin|phenoxymethylpenicillin|penicillin)\b", "Common sore-throat antibacterial; inferred"),
    ("respiratory-infection", r"\b(amoxycillin|amoxicillin|azithromycin|cefpodoxime|cefuroxime|levofloxacin|doxycycline)\b", "Common respiratory antibacterial; inferred"),
    ("skin-infection", r"\b(cloxacillin|cephalexin|mupirocin|amoxycillin clav|clavulan)", "Skin and soft-tissue antibacterial; inferred"),
    ("diabetes-2", r"\b(metformin|glimepiride|glibenclamide|gliclazide|glipizide|sitagliptin|vildagliptin|teneligliptin|dapagliflozin|empagliflozin|pioglitazone)\b", "Type 2 diabetes medicine"),
    ("diabetes-1", r"\binsulin\b", "Insulin"),
    ("diabetes-2", r"\binsulin\b", "Insulin is also used in type 2 diabetes"),
    ("hypothyroid", r"\b(levothyroxine|thyroxine|eltroxin|thyronorm)\b", "Thyroid hormone"),
    ("gout", r"\b(allopurinol|colchicine|febuxostat)\b", "Gout medicine"),
    ("dyslipidaemia", r"\b(atorvastatin|rosuvastatin|simvastatin|pravastatin|fenofibrate|ezetimibe)\b", "Lipid-lowering medicine"),
    ("hypertension", r"\b(amlodipine|telmisartan|losartan|olmesartan|enalapril|ramipril|lisinopril|atenolol|metoprolol|bisoprolol|nebivolol|nifedipine|clonidine|telmisartan|chlorthalidone|hydrochlorothiazide|perindopril)\b", "Antihypertensive ingredient"),
    ("heart-failure", r"\b(furosemide|torsemide|spironolactone|carvedilol|digoxin|sacubitril)\b", "Heart-failure medicine"),
    ("ihd", r"\b(isosorbide|clopidogrel|aspirin|atorvastatin|rosuvastatin|glyceryl trinitrate|nitroglycerin)\b", "Ischaemic heart disease medicine; aspirin here is not tagged as fever"),
    ("arrhythmia", r"\b(amiodarone|verapamil|diltiazem|digoxin)\b", "Antiarrhythmic"),
    ("asthma", r"\b(salbutamol|levosalbutamol|budesonide|formoterol|ipratropium|tiotropium|montelukast|theophylline|beclomethasone|fluticasone)\b", "Asthma medicine"),
    ("copd", r"\b(tiotropium|ipratropium|salbutamol|formoterol)\b", "COPD bronchodilator"),
    ("cough", r"\b(dextromethorphan|ambroxol|bromhexine|guaifenesin|levosalbutamol)\b", "Cough preparation"),
    ("rhinitis", r"\b(cetirizine|levocetirizine|fexofenadine|loratadine|montelukast|fluticasone|mometasone|azelastine)\b", "Rhinitis medicine"),
    ("allergy", r"\b(cetirizine|levocetirizine|fexofenadine|loratadine|pheniramine|chlorpheniramine|adrenaline|epinephrine)\b", "Antiallergic"),
    ("reflux", r"\b(pantoprazole|omeprazole|rabeprazole|esomeprazole|lansoprazole|famotidine|ranitidine|sucralfate|domperidone)\b", "Acid-related disorder"),
    ("nausea", r"\b(ondansetron|domperidone|metoclopramide)\b", "Antiemetic"),
    ("constipation", r"\b(lactulose|bisacodyl|ispaghula|polyethylene glycol|docusate)\b", "Laxative"),
    ("diarrhoea", r"\b(oral rehydration|ors\b|loperamide|racecadotril|saccharomyces)\b", "Diarrhoea medicine"),
    ("dehydration", r"\b(oral rehydration|ors\b|ringer|sodium chloride|dextrose)\b", "Fluid replacement"),
    ("pain", r"\b(paracetamol|ibuprofen|diclofenac|aceclofenac|nimesulide|mefenamic|tramadol|morphine|serratiopeptidase)\b", "Analgesic"),
    ("fever", r"\b(paracetamol|ibuprofen|mefenamic|nimesulide|aceclofenac)\b", "Antipyretic ingredient. General fever search uses this tag."),
    ("menstrual", r"\bmefenamic\b", "Often used for menstrual pain"),
    ("epilepsy", r"\b(sodium valproate|valproate|carbamazepine|phenytoin|levetiracetam|phenobarbitone|phenobarbital|clobazam|lorazepam|diazepam)\b", "Antiseizure medicine"),
    ("migraine", r"\b(sumatriptan|flunarizine|propranolol)\b", "Migraine medicine"),
    ("parkinson", r"\b(levodopa|carbidopa|trihexyphenidyl|pramipexole)\b", "Parkinson medicine"),
    ("dementia", r"\b(donepezil|memantine)\b", "Dementia medicine"),
    ("depression", r"\b(amitriptyline|sertraline|fluoxetine|escitalopram|sertraline)\b", "Antidepressant"),
    ("anxiety", r"\b(clonazepam|etizolam|propranolol|buspirone)\b", "Anxiety medicine; inferred"),
    ("psychosis", r"\b(risperidone|olanzapine|quetiapine|haloperidol|aripiprazole)\b", "Antipsychotic"),
    ("bipolar", r"\b(lithium|valproate|quetiapine)\b", "Mood stabiliser; valproate overlap with epilepsy is intentional"),
    ("insomnia", r"\b(zolpidem|clonazepam|nitrazepam)\b", "Hypnotic"),
    ("neuropathic-pain", r"\b(pregabalin|gabapentin|amitriptyline|duloxetine)\b", "Neuropathic pain medicine"),
    ("anaemia", r"\b(ferrous|iron\b|folic acid|cyanocobalamin|vitamin b12)\b", "Haematinic"),
    ("thrombosis", r"\b(enoxaparin|heparin|warfarin|acenocoumarol|rivaroxaban|apixaban|clopidogrel)\b", "Antithrombotic"),
    ("vitamin", r"\b(cholecalciferol|vitamin d|vitamin a|thiamine|pyridoxine|ascorbic acid|folic acid)\b", "Vitamin"),
    ("scabies", r"\b(permethrin|ivermectin|gamma benzene)\b", "Scabies medicine"),
    ("dermatitis", r"\b(betamethasone|clobetasol|mometasone|hydrocortisone)\b", "Topical or systemic steroid; not a specific diagnosis"),
    ("conjunctivitis", r"\b(moxifloxacin eye|ofloxacin eye|chloramphenicol eye|tobramycin)\b", "Ocular anti-infective"),
    ("glaucoma", r"\b(timolol|latanoprost|brimonidine|pilocarpine)\b", "Glaucoma medicine"),
    ("otitis", r"\b(ciprofloxacin ear|ofloxacin ear|neomycin)\b", "Ear preparation"),
    ("bph", r"\b(tamsulosin|alfuzosin|silodosin|dutasteride|finasteride)\b", "Prostate medicine"),
    ("labour", r"\b(oxytocin|misoprostol|methylergometrine|carboprost)\b", "Uterotonic"),
    ("preeclampsia", r"\bmagnesium sulphate\b", "Used in eclampsia; also an anticonvulsant in NLEM"),
    ("contraception", r"\b(levonorgestrel|ethinylestradiol|medroxyprogesterone|norethisterone|desogestrel)\b", "Contraceptive hormone"),
    ("poisoning", r"\b(activated charcoal|naloxone|n-acetylcysteine|acetylcysteine|atropine|pralidoxime)\b", "Antidote"),
    ("envenomation", r"\b(anti-snake|antisnake|scorpion antivenom)\b", "Antivenom"),
    ("covid", r"\b(dexamethasone|enoxaparin|oxygen)\b", "Only when the row is in the COVID section; applied separately"),
    ("rheumatoid", r"\b(hydroxychloroquine|methotrexate|sulfasalazine|leflunomide)\b", "DMARD"),
    ("osteoarthritis", r"\b(diclofenac|aceclofenac)\b", "NSAID used for osteoarthritis; inferred"),
    ("spasm", r"\b(thiocolchicoside|chlorzoxazone|tizanidine|baclofen)\b", "Muscle relaxant"),
    ("general-anaesthesia", r"\b(propofol|ketamine|thiopentone|sevoflurane|isoflurane|halothane|nitrous oxide)\b", "Anaesthetic"),
    ("local-anaesthesia", r"\b(lignocaine|lidocaine|bupivacaine)\b", "Local anaesthetic"),
    ("muscle-relaxant", r"\b(atracurium|vecuronium|succinylcholine|rocuronium)\b", "Neuromuscular blocker"),
    ("adrenal", r"\b(fludrocortisone|hydrocortisone)\b", "Adrenal steroid"),
    ("cancer-support", r"\b(filgrastim|ondansetron|mesna)\b", "Supportive cancer care"),
    ("palliative", r"\b(morphine|fentanyl|tramadol)\b", "Opioid used in palliative care and acute pain"),
]


ANTIBIOTIC_RE = re.compile(
    r"\b(amoxycillin|amoxicillin|ampicillin|azithromycin|cefixime|cefpodoxime|cefuroxime|"
    r"ceftriaxone|cefotaxime|ceftazidime|cefadroxil|cefazolin|cefalexin|cephalexin|"
    r"ciprofloxacin|levofloxacin|ofloxacin|moxifloxacin|norfloxacin|doxycycline|"
    r"gentamicin|amikacin|metronidazole|nitrofurantoin|meropenem|vancomycin|"
    r"clindamycin|clarithromycin|linezolid|co-trimoxazole|trimethoprim|sulphamethoxazole|"
    r"sulfamethoxazole|cloxacillin|penicillin|piperacillin|tazobactam|clavulan|"
    r"benzylpenicillin|bedaquiline)\b",
    re.I,
)

NSAID_RE = re.compile(r"\b(ibuprofen|diclofenac|aceclofenac|nimesulide|mefenamic|aspirin|acetylsalicylic|indomethacin|etoricoxib|serratiopeptidase)\b", re.I)
PARA_RE = re.compile(r"\bparacetamol\b", re.I)
ORS_RE = re.compile(r"\b(oral rehydration|ors)\b", re.I)


def apply_tags(product: dict) -> list[dict]:
    blob = f"{product['name']} {product.get('composition') or ''}"
    section = product.get("sectionTitle") or ""
    tags: list[dict] = []
    seen = set()

    def add(item):
        key = (item["label"], item["origin"], item.get("sub"))
        if key in seen:
            return
        seen.add(key)
        tags.append(item)

    if product["kind"] == "nlem":
        code = product.get("sectionCode") or ""
        for item in section_class_tags(section):
            add(item)
        low = section.lower()
        if "antimalarial" in low:
            add(tag("malaria", "inferred", "malaria", "Inferred because the NLEM row sits in the antimalarial section."))
        if "antituberculosis" in low:
            add(tag("tuberculosis", "inferred", "tuberculosis", INFER_NOTE))
        if "antileprosy" in low:
            add(tag("leprosy", "inferred", "leprosy", INFER_NOTE))
        if "antibacterial" in low:
            add(tag("antibiotic", "inferred", None, "Inferred from the NLEM antibacterial heading. Not every anti-infective is an antibiotic."))
        if code.startswith("2.1") or "antipyretic" in low:
            add(tag("fever", "inferred", "fever", "Inferred from the NLEM antipyretic section (2.1). General fever search uses this tag."))
            add(tag("antipyretic", "section", None, "NLEM section 2.1 heading"))
        if code.startswith("6.2"):
            add(tag("antibiotic", "inferred", None, "Inferred from the NLEM antibacterial section (6.2). Anti-infective sections other than 6.2 are not labeled antibiotic."))
            add(tag("antibacterial", "section", None, "NLEM section 6.2 heading"))
        if code.startswith("6.10"):
            add(tag("malaria", "inferred", "malaria", "Inferred because the row is in NLEM antimalarial section 6.10."))
            add(tag("antimalarial", "section", None, "NLEM section 6.10 heading"))
        if code.startswith("6.4"):
            add(tag("tuberculosis", "inferred", "tuberculosis", INFER_NOTE))
        if code.startswith("6.3"):
            add(tag("leprosy", "inferred", "leprosy", INFER_NOTE))
        if code.startswith("6.5"):
            add(tag("fungal", "inferred", "fungal", INFER_NOTE))
        if code.startswith("6.1"):
            add(tag("helminth", "inferred", "helminth", INFER_NOTE))
        if code.startswith("27"):
            add(tag("covid", "inferred", "covid", "Inferred from NLEM section 27."))
        if "antimigraine" in low:
            add(tag("migraine", "inferred", "migraine", INFER_NOTE))
        if "gout" in low:
            add(tag("gout", "inferred", "gout", INFER_NOTE))
        if "rheumatoid" in low:
            add(tag("rheumatoid", "inferred", "rheumatoid", INFER_NOTE))
        if "covid" in low:
            add(tag("covid", "inferred", "covid", "Inferred from NLEM section 27. Not every ingredient is COVID-specific."))
        if "anthelminth" in low:
            add(tag("helminth", "inferred", "helminth", INFER_NOTE))
        if "antifungal" in low:
            add(tag("fungal", "inferred", "fungal", INFER_NOTE))
        if section.startswith("Medicines used in anaesthesia") or "anaesthesia" in low:
            add(tag("general anaesthesia", "inferred", "general-anaesthesia", INFER_NOTE))

    if product.get("kind") == "company" and section:
        add(tag(
            section,
            "section",
            None,
            "Therapy or section heading printed by the company. Not an Indian product-licence indication.",
        ))
        if "antipyretic" in section.lower():
            add(tag("antipyretic", "section", None, "The company section heading says antipyretic."))
        if re.search(r"\bantibiotic", section, re.I):
            add(tag("antibiotic", "section", None, "The company section heading says antibiotic. Anti-infective headings are not given this tag."))

    for sub, pattern, note in NAME_RULES:
        if sub == "covid":
            continue
        if re.search(pattern, blob, re.I):
            add(tag(sub.replace("-", " "), "inferred", sub, note + " " + INFER_NOTE))

    if ANTIBIOTIC_RE.search(blob):
        add(tag("antibiotic", "inferred", None, "Inferred antibacterial class tag. Antimalarials, antifungals and pure antivirals are not given this tag unless the name also matches."))

    # Dengue supportive care: paracetamol without an NSAID, and ORS.
    if (PARA_RE.search(blob) and not NSAID_RE.search(blob)) or ORS_RE.search(blob):
        add(tag(
            "dengue",
            "inferred",
            "dengue",
            "Inferred supportive care only (antipyretic without an NSAID, or oral rehydration). Not a dengue antiviral. NSAIDs are intentionally not tagged.",
        ))

    # Aspirin is not a general fever medicine unless the NLEM row is in the antipyretic section.
    if re.search(r"\b(aspirin|acetylsalicylic)\b", blob, re.I):
        if "antipyretic" in section.lower():
            add(tag("fever", "inferred", "fever", "Acetylsalicylic acid row in the NLEM antipyretic section."))
        else:
            # remove fever if a name rule added it — aspirin rule is not in NAME_RULES for fever.
            pass

    # Diclofenac in antipyretic section can be fever; name rule already adds fever for aceclofenac/ibuprofen/mefenamic/nimesulide.
    if re.search(r"\bdiclofenac\b", blob, re.I) and "antipyretic" in section.lower():
        add(tag("fever", "inferred", "fever", "NSAID in the NLEM antipyretic section."))

    return tags


TESTS = [
    ("Blood", ["Complete blood count (CBC)", "Haemoglobin", "ESR", "Peripheral blood smear", "Platelet count", "Blood group and Rh", "Reticulocyte count", "Prothrombin time / INR", "aPTT", "Bleeding time", "Clotting time", "D-dimer", "Serum ferritin", "Serum iron and TIBC", "Vitamin B12", "Serum folate", "Direct Coombs test", "G6PD"]),
    ("Biochemistry", ["Fasting blood glucose", "Post-prandial blood glucose", "Random blood glucose", "HbA1c", "Blood urea", "Serum creatinine", "eGFR", "Serum uric acid", "Serum sodium", "Serum potassium", "Serum chloride", "Serum calcium", "Serum magnesium", "Serum phosphate", "Total bilirubin", "Direct bilirubin", "SGOT / AST", "SGPT / ALT", "Alkaline phosphatase", "GGT", "Total protein", "Serum albumin", "Lipid profile", "Total cholesterol", "Triglycerides", "HDL cholesterol", "LDL cholesterol", "Serum amylase", "Serum lipase", "CPK", "LDH", "CRP", "hs-CRP", "RA factor", "ASO titre", "ANA", "Vitamin D", "PSA"]),
    ("Hormones", ["TSH", "Free T4", "Free T3", "Total T4", "Total T3", "Fasting insulin", "Cortisol", "Prolactin", "FSH", "LH", "Serum beta-hCG", "Testosterone"]),
    ("Urine", ["Urine routine and microscopy", "Urine culture and sensitivity", "Urine pregnancy test", "24-hour urine protein", "Urine microalbumin", "Urine ketones"]),
    ("Stool", ["Stool routine and microscopy", "Stool occult blood", "Stool culture", "Stool for ova and cyst", "Stool hanging drop"]),
    ("Serology and infection", ["Blood culture", "Widal test", "Dengue NS1 antigen", "Dengue IgM", "Malaria antigen", "Malaria smear", "Typhoid IgM", "HIV 1 and 2", "HBsAg", "Anti-HCV", "VDRL", "COVID-19 rapid antigen", "COVID-19 RT-PCR", "Mantoux test", "Procalcitonin"]),
    ("Ultrasound", ["USG abdomen", "USG pelvis", "USG KUB", "Obstetric ultrasound", "Transvaginal ultrasound", "Doppler ultrasound", "USG thyroid", "USG breast", "USG scrotum", "FAST scan"]),
    ("X-ray and radiology", ["Chest X-ray", "X-ray abdomen", "X-ray KUB", "X-ray spine", "X-ray skull", "X-ray extremity", "CT head", "CT chest", "CT abdomen", "MRI brain", "MRI spine", "Mammography", "DEXA bone density", "Barium swallow"]),
    ("Endoscopy", ["Upper GI endoscopy", "Colonoscopy", "Sigmoidoscopy", "Bronchoscopy", "Cystoscopy", "Nasal endoscopy", "Laryngoscopy", "ERCP", "Hysteroscopy"]),
    ("Cardiac", ["ECG", "Echocardiography", "Treadmill test", "Holter monitor", "Troponin I", "Troponin T", "CK-MB", "BNP", "NT-proBNP"]),
    ("Pulmonary", ["Spirometry", "Peak expiratory flow", "Arterial blood gas", "Pulse oximetry", "Sputum AFB", "Sputum culture", "CBNAAT / GeneXpert", "Pleural fluid analysis"]),
    ("Pathology", ["FNAC", "Biopsy histopathology", "Pap smear", "Liquid-based cytology"]),
]


def tests_payload():
    rows = []
    order = 1
    for category, names in TESTS:
        for name in names:
            rows.append({"category": category, "name": name, "sort": order})
            order += 1
    return rows


def main():
    nlem = parse_nlem(NLEM_TEXT.read_text(errors="replace"))
    pmbjp = parse_pmbjp(PMBJP_TEXT.read_text(errors="replace"))
    companies, company_report = load_company_products()
    products = nlem + pmbjp + companies
    for product in products:
        product["tags"] = apply_tags(product)
    categories = taxonomy_payload()
    sub_count = sum(len(c["subcategories"]) for c in categories)
    payload = {
        "meta": {
            "retrieved": RETRIEVED,
            "disclaimer": (
                "NLEM 2022, the PMBJP generic annexure of 31 December 2025, and public company "
                "composition tables retrieved 27 October 2026. A price is stored only when the source "
                "printed an MRP. This is not the Indian 60,000-brand or 3,000-company market. "
                "Disease tags are inferred unless origin is section."
            ),
            "counts": {
                "nlem": len(nlem),
                "janaushadhi": len(pmbjp),
                "companyRows": company_report["companyRows"],
                "companyRowsWithPrice": company_report["companyRowsWithPrice"],
                "companyBrands": company_report["distinctBrands"],
                "companies": company_report["distinctCompanies"],
                "products": len(products),
                "categories": len(categories),
                "subcategories": sub_count,
                "tests": sum(len(n) for _, n in TESTS),
            },
            "companies": company_report,
            "taxonomyCitation": "World Health Organization. ICD-11 for Mortality and Morbidity Statistics (MMS). Categories follow ICD-11 chapters. Subcategories are clinical search terms a doctor types, with an ICD-11 code or range where one is a fair anchor. Anaesthesia is included from NLEM section 1 because those rows are not diseases.",
        },
        "categories": categories,
        "tests": tests_payload(),
        "products": products,
    }
    OUT.write_text(json.dumps(payload, ensure_ascii=False, indent=2))
    print(json.dumps(payload["meta"]["counts"], indent=2))
    empty = [p for p in nlem if len(p["name"]) < 3]
    print("short nlem names", len(empty))
    # spot checks
    def show(pred, limit=8):
        hits = [p["name"] for p in products if pred(p)][:limit]
        print(len([p for p in products if pred(p)]), hits)

    def has(sub):
        return lambda p: any(t.get("sub") == sub for t in p["tags"])

    print("fever", end=" ")
    show(has("fever"))
    print("malaria", end=" ")
    show(has("malaria"))
    print("dengue", end=" ")
    show(has("dengue"))
    print("hypertension", end=" ")
    show(has("hypertension"))
    chloro_fever = [p["name"] for p in products if has("fever")(p) and re.search(r"chloroquine", p["name"], re.I)]
    ibu_dengue = [p["name"] for p in products if has("dengue")(p) and re.search(r"ibuprofen", p["name"], re.I)]
    print("chloroquine tagged fever", chloro_fever[:5], "count", len(chloro_fever))
    print("ibuprofen tagged dengue", ibu_dengue[:5], "count", len(ibu_dengue))


if __name__ == "__main__":
    main()
