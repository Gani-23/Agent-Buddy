#!/usr/bin/env python3
"""
Generate SB-103 style payslips from report references.
- Reads selected references from --refs-json
- Uses payment_references.txt + dop_agent.db
- Produces a single PDF with max 3 payslips per A4 page
"""

import argparse
import base64
import json
import re
import sqlite3
import sys
import zlib
from datetime import datetime
from pathlib import Path

from reportlab.lib.pagesizes import A4
from reportlab.pdfgen import canvas
from reportlab.lib import colors
from dop_paths import resolve_base_dir


REPORT_BLOCK_RE = re.compile(
    r"Timestamp:\s*(?P<timestamp>[^\r\n]+)\s*[\r\n]+"
    r"List #:\s*(?P<list_idx>\d+)\s*[\r\n]+"
    r"Reference Number:\s*(?P<reference>[^\r\n]+)\s*[\r\n]+"
    r"Accounts:\s*(?P<accounts>[^\r\n]+)",
    re.IGNORECASE,
)

TOTAL_AMOUNT_RE = re.compile(
    r"Total Amount\s*:\s*([0-9][0-9,]*(?:\.[0-9]+)?)",
    re.IGNORECASE,
)


def configure_console_output():
    """Force UTF-8 output to avoid Windows cp1252 UnicodeEncodeError."""
    for stream_name in ("stdout", "stderr"):
        stream = getattr(sys, stream_name, None)
        if stream is None:
            continue
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass


configure_console_output()


def parse_args():
    parser = argparse.ArgumentParser(description="Generate payslip PDF from references")
    parser.add_argument("--refs-json", required=True, help='JSON array of references, e.g. ["C1","C2"]')
    parser.add_argument("--beneficiary", default="SREEDEVI (DOP.MI5158650200005)")
    parser.add_argument("--max-per-page", type=int, default=3)
    return parser.parse_args()


def normalize_reference_list(refs_json):
    try:
        raw = json.loads(refs_json)
    except Exception:
        return []
    if not isinstance(raw, list):
        return []
    seen = set()
    refs = []
    for item in raw:
        text = str(item).strip()
        if not text:
            continue
        key = text.upper()
        if key in seen:
            continue
        seen.add(key)
        refs.append(text)
    return refs


def read_reference_entries(reference_file):
    if not reference_file.exists():
        return []

    content = reference_file.read_text(encoding="utf-8", errors="ignore")
    entries = []
    for match in REPORT_BLOCK_RE.finditer(content):
        ts_raw = match.group("timestamp").strip()
        try:
            ts = datetime.strptime(ts_raw, "%Y-%m-%d %H:%M:%S")
        except Exception:
            continue
        entries.append(
            {
                "timestamp": ts,
                "list_idx": int(match.group("list_idx").strip() or 0),
                "reference": match.group("reference").strip(),
                "accounts": match.group("accounts").strip(),
            }
        )
    return entries


def latest_entries_for_references(entries, refs):
    by_ref = {}
    for entry in entries:
        ref = entry["reference"]
        existing = by_ref.get(ref)
        if existing is None or entry["timestamp"] > existing["timestamp"]:
            by_ref[ref] = entry

    selected = []
    for ref in refs:
        item = by_ref.get(ref)
        if item:
            selected.append(item)
    return selected


def parse_account_tokens(accounts_raw):
    clean = accounts_raw.replace("[", "").replace("]", "").strip()
    if not clean:
        return []

    tokens = []
    for part in clean.split(','):
        text = part.strip()
        if not text:
            continue
        if '_' in text:
            account_no, inst_raw = text.split('_', 1)
            account_no = account_no.strip()
            try:
                inst = int(inst_raw.strip())
            except Exception:
                inst = 1
            inst = max(1, inst)
        else:
            account_no = text
            inst = 1

        if account_no.isdigit():
            tokens.append((account_no, inst))
    return tokens


def load_amounts(db_file, account_numbers):
    if not account_numbers:
        return {}
    conn = sqlite3.connect(str(db_file))
    cursor = conn.cursor()
    placeholders = ",".join(["?"] * len(account_numbers))
    cursor.execute(
        f"""
        SELECT account_no, COALESCE(amount, 0)
        FROM rd_accounts
        WHERE account_no IN ({placeholders})
        """,
        account_numbers,
    )
    result = {str(row[0]).strip(): int(row[1] or 0) for row in cursor.fetchall()}
    conn.close()
    return result


def load_agent_id(db_file):
    conn = sqlite3.connect(str(db_file))
    cursor = conn.cursor()
    cursor.execute("SELECT agent_id FROM credentials WHERE id = 1")
    row = cursor.fetchone()
    conn.close()
    if row and row[0]:
        return str(row[0]).strip()
    return "DOPMI5158650200005"


def extract_total_amount_from_report_pdf(pdf_path):
    if not pdf_path.exists():
        return None

    try:
        pdf_bytes = pdf_path.read_bytes()
    except Exception:
        return None

    decoded_text_parts = []
    start = 0

    while True:
        stream_pos = pdf_bytes.find(b"stream", start)
        if stream_pos == -1:
            break

        end_pos = pdf_bytes.find(b"endstream", stream_pos)
        if end_pos == -1:
            break

        start = end_pos + len(b"endstream")
        stream_data_start = stream_pos + len(b"stream")
        if pdf_bytes[stream_data_start:stream_data_start + 2] == b"\r\n":
            stream_data_start += 2
        elif pdf_bytes[stream_data_start:stream_data_start + 1] in (b"\n", b"\r"):
            stream_data_start += 1

        raw_stream = pdf_bytes[stream_data_start:end_pos].strip()
        if not raw_stream:
            continue

        # ScheduleArguments.py uses ReportLab which writes ASCII85 + Flate streams.
        payload = raw_stream
        if not payload.startswith(b"<~"):
            payload = b"<~" + payload
        if not payload.endswith(b"~>"):
            payload = payload + b"~>"

        try:
            decoded = base64.a85decode(payload, adobe=True)
            inflated = zlib.decompress(decoded)
            decoded_text_parts.append(inflated.decode("latin-1", errors="ignore"))
        except Exception:
            continue

    if not decoded_text_parts:
        return None

    merged_text = "\n".join(decoded_text_parts)
    matches = TOTAL_AMOUNT_RE.findall(merged_text)
    if not matches:
        return None

    amount_text = matches[-1].replace(",", "").strip()
    try:
        return float(amount_text)
    except Exception:
        return None


def compute_rebate(amount, installments):
    if amount <= 0 or installments <= 0:
        return 0
    hundreds = amount // 100
    if installments >= 12:
        return hundreds * 40
    if installments >= 6:
        return hundreds * 10
    return 0


def amount_to_words(num):
    num = int(max(0, num))
    if num == 0:
        return "Zero"

    ones = ["", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen"]
    tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"]

    def two_digits(n):
        if n < 20:
            return ones[n]
        return (tens[n // 10] + (" " + ones[n % 10] if n % 10 else "")).strip()

    def three_digits(n):
        h = n // 100
        rem = n % 100
        parts = []
        if h:
            parts.append(ones[h] + " Hundred")
        if rem:
            parts.append(two_digits(rem))
        return " ".join(parts)

    parts = []
    crore = num // 10000000
    num %= 10000000
    lakh = num // 100000
    num %= 100000
    thousand = num // 1000
    num %= 1000

    if crore:
        parts.append(two_digits(crore) + " Crore")
    if lakh:
        parts.append(two_digits(lakh) + " Lakh")
    if thousand:
        parts.append(two_digits(thousand) + " Thousand")
    if num:
        parts.append(three_digits(num))

    return " ".join(parts).strip()


def draw_slip(c, x, y, w, h, data):
    # Outer border
    c.setStrokeColor(colors.black)
    c.setLineWidth(0.8)
    c.rect(x, y, w, h)

    notes_w = w * 0.29
    left_w = w - notes_w

    c.line(x + left_w, y, x + left_w, y + h)

    pad = 6
    lx = x + pad
    ly_top = y + h - pad

    c.setFont("Helvetica", 8.5)
    c.drawString(lx, ly_top - 8, "S.B.-103")

    c.setFont("Helvetica-Bold", 11)
    c.drawCentredString(x + left_w / 2, ly_top - 26, "POST OFFICE SAVING BANK")
    c.setFont("Helvetica-Bold", 9)
    c.drawCentredString(x + left_w / 2, ly_top - 42, "TYPE OF A/C: SB/RD/MIS/TD/PPF")
    c.drawCentredString(x + left_w / 2, ly_top - 55, "RAYADURG P.O")

    y_ref = ly_top - 74
    c.setFont("Helvetica-Bold", 8.5)
    c.drawString(lx + 2, y_ref, f"Ref No. {data['reference']}.")
    c.drawString(lx + 120, y_ref, f"No of Passbooks={data['passbooks']}")
    c.drawString(lx + 250, y_ref, f"Date: {data['date_text']}")

    c.setLineWidth(0.6)
    c.line(lx, y_ref - 4, x + left_w - pad, y_ref - 4)

    y_paid = y_ref - 18
    c.setFont("Helvetica", 7)
    c.drawString(lx, y_paid + 2, "Paid into the credit of")
    c.setFont("Helvetica-Bold", 8.5)
    c.drawString(lx + 115, y_paid + 2, data["beneficiary"])
    c.line(lx + 112, y_paid - 1, x + left_w - pad, y_paid - 1)

    y_rupees = y_paid - 22
    c.setFont("Helvetica", 8.5)
    c.drawString(lx, y_rupees + 3, "Rupees")
    c.setFont("Helvetica-Bold", 9.5)
    c.drawString(lx + 55, y_rupees + 2, data["amount_words"]) 

    amt_box_w = 75
    c.rect(x + left_w - pad - amt_box_w, y_rupees - 5, amt_box_w, 20)
    c.setFont("Helvetica-Bold", 8)
    c.drawCentredString(x + left_w - pad - amt_box_w / 2, y_rupees + 2, f"Rs {data['gross_amount']:.1f}")

    c.line(lx + 50, y_rupees - 2, x + left_w - pad - amt_box_w - 4, y_rupees - 2)

    y_cash = y_rupees - 20
    c.setFont("Helvetica", 8.5)
    c.drawString(lx, y_cash, "By Cash/ Cheque No.")
    c.drawString(lx + 172, y_cash, "Date")
    c.drawString(lx + 246, y_cash, "Drawn on")
    c.line(lx + 95, y_cash - 3, lx + 168, y_cash - 3)
    c.line(lx + 198, y_cash - 3, lx + 242, y_cash - 3)
    c.line(lx + 288, y_cash - 3, x + left_w - pad, y_cash - 3)

    y_bank = y_cash - 18
    c.drawString(lx, y_bank, "Bank Name")
    c.line(lx + 55, y_bank - 3, x + left_w - pad, y_bank - 3)

    y_fee = y_bank - 19
    c.drawString(lx, y_fee, "Default Fee/Rebate info")
    c.setFont("Helvetica-Bold", 8.5)
    c.drawString(lx + 145, y_fee, f"Default Fee {data['default_fee']:.1f} , Rebate {data['rebate']:.1f}")
    c.line(lx + 100, y_fee - 3, x + left_w - pad, y_fee - 3)

    y_bal = y_fee - 20
    c.setFont("Helvetica", 8.5)
    c.drawString(lx, y_bal, "Balance After Transaction")
    c.line(lx + 120, y_bal - 3, x + left_w - pad, y_bal - 3)

    y_bottom = y + 16
    c.setFont("Helvetica-Bold", 9)
    c.drawRightString(x + left_w - pad, y_bottom + 18, data["transaction_id"])
    c.setFont("Helvetica", 8)
    c.drawCentredString(x + left_w * 0.38, y_bottom - 2, "sign of Accepting Official")
    c.drawCentredString(x + left_w * 0.78, y_bottom - 2, "Deposited By")

    # Notes panel
    nx = x + left_w + pad
    ny_top = y + h - pad
    c.setFont("Helvetica-Bold", 8.5)
    c.drawString(nx, ny_top - 16, "Notes No.")
    c.drawString(nx + 78, ny_top - 16, "Amount")

    c.setFont("Helvetica-Bold", 10)
    denominations = [2000, 500, 200, 100, 50, 20, 10, 5, 1]
    ry = ny_top - 34
    for d in denominations:
        c.drawString(nx + 2, ry, f"{d} X")
        c.drawString(nx + 58, ry, "=")
        ry -= 15

    c.setFont("Helvetica-Bold", 9)
    c.drawString(nx + 2, ry, "Coins")
    c.drawString(nx + 58, ry, "=")
    ry -= 15
    c.drawString(nx + 2, ry, "Grand Total")
    c.drawString(nx + 58, ry, "=")
    c.drawRightString(x + w - pad - 4, ry, f"{int(round(data['gross_amount']))}")


def make_payslip_data(entries, db_file, reports_pdf_dir, beneficiary, agent_id):
    slips = []
    for entry in entries:
        tokens = parse_account_tokens(entry["accounts"])
        acc_numbers = [acc for acc, _ in tokens]
        amounts = load_amounts(db_file, acc_numbers)

        gross = 0
        rebate = 0
        for acc, inst in tokens:
            amount = int(amounts.get(acc, 0) or 0)
            gross += amount * inst
            rebate += compute_rebate(amount, inst)

        default_fee = 0.0
        ts = entry["timestamp"]

        computed_net_amount = float(max(0, gross - rebate + default_fee))
        report_pdf_path = reports_pdf_dir / f"{entry['reference']}.pdf"
        pdf_total_amount = extract_total_amount_from_report_pdf(report_pdf_path)
        payable_amount = computed_net_amount if pdf_total_amount is None else float(max(0, pdf_total_amount))

        slips.append(
            {
                "reference": entry["reference"],
                "date_text": ts.strftime("%d-%b-%Y"),
                "passbooks": len(tokens),
                "beneficiary": beneficiary,
                "gross_amount": payable_amount,
                "rebate": float(rebate),
                "default_fee": float(default_fee),
                "amount_words": f"{amount_to_words(int(round(payable_amount)))} Only",
                "transaction_id": agent_id,
            }
        )
    return slips


def generate_pdf(slips, output_path, max_per_page):
    output_path.parent.mkdir(parents=True, exist_ok=True)
    c = canvas.Canvas(str(output_path), pagesize=A4)
    page_w, page_h = A4

    margin_x = 10
    margin_y = 10
    usable_h = page_h - (2 * margin_y)
    slip_h = usable_h / max_per_page
    slip_w = page_w - (2 * margin_x)

    for idx, slip in enumerate(slips):
        if idx > 0 and idx % max_per_page == 0:
            c.showPage()

        pos = idx % max_per_page
        y = page_h - margin_y - (pos + 1) * slip_h
        draw_slip(c, margin_x, y + 2, slip_w, slip_h - 4, slip)

    c.save()


def main():
    args = parse_args()
    refs = normalize_reference_list(args.refs_json)
    if not refs:
        print("ERROR: No valid references provided")
        raise SystemExit(2)

    base_dir = resolve_base_dir()
    db_file = base_dir / "dop_agent.db"
    reference_file = base_dir / "Reports" / "references" / "payment_references.txt"
    reports_pdf_dir = base_dir / "Reports" / "pdf"

    entries = read_reference_entries(reference_file)
    selected_entries = latest_entries_for_references(entries, refs)
    if not selected_entries:
        print("ERROR: No matching references found in payment_references.txt")
        raise SystemExit(3)

    agent_id = load_agent_id(db_file)
    slips = make_payslip_data(selected_entries, db_file, reports_pdf_dir, args.beneficiary, agent_id)

    ts = datetime.now().strftime("%Y%m%d_%H%M%S")
    output_path = base_dir / "Reports" / "payslips" / f"Payslips_{ts}.pdf"
    generate_pdf(slips, output_path, max(1, min(3, args.max_per_page)))

    print(f"OUTPUT_PDF: {output_path}")


if __name__ == "__main__":
    main()
