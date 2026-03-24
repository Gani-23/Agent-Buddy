#!/usr/bin/env python3
"""
Sync legacy `account_detail` data into DOPAgent database.

Default paths:
- source: ~/Desktop/database.sqlite (fallback: ~/Documents/database.sqlite)
- target: ~/Documents/DOPAgent/dop_agent.db

This script can be safely re-run periodically.
"""

from __future__ import annotations

import argparse
import shutil
import sqlite3
import sys
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, Iterable, Tuple


@dataclass
class SyncStats:
    inserted: int = 0
    updated: int = 0
    unchanged: int = 0


def resolve_default_source() -> Path:
    desktop = Path.home() / "Desktop" / "database.sqlite"
    if desktop.exists():
        return desktop
    return Path.home() / "Documents" / "database.sqlite"


def resolve_default_target() -> Path:
    return Path.home() / "Documents" / "DOPAgent" / "dop_agent.db"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Sync legacy account_detail into dop_agent.db"
    )
    parser.add_argument(
        "--source",
        type=Path,
        default=resolve_default_source(),
        help="Path to old database.sqlite",
    )
    parser.add_argument(
        "--target",
        type=Path,
        default=resolve_default_target(),
        help="Path to DOPAgent dop_agent.db",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Preview changes without writing to target DB",
    )
    parser.add_argument(
        "--skip-rd-accounts",
        action="store_true",
        help="Sync only account_detail table; skip rd_accounts updates",
    )
    parser.add_argument(
        "--deactivate-missing",
        action="store_true",
        help="Mark rows not present in source as deactivate/inactive",
    )
    parser.add_argument(
        "--no-backup",
        action="store_true",
        help="Do not create backup before applying changes",
    )
    parser.add_argument(
        "--prefer-source-values",
        action="store_true",
        help="Overwrite existing target values with non-empty source values (aggressive mode)",
    )
    parser.add_argument(
        "--force-aslaas",
        action="store_true",
        help="Force overwrite ASLAAS values from source account_detail into target tables",
    )
    parser.add_argument(
        "--interactive-force-aslaas",
        action="store_true",
        help="Ask interactively whether to force ASLAAS overwrite before syncing",
    )
    return parser.parse_args()


def connect(path: Path) -> sqlite3.Connection:
    conn = sqlite3.connect(str(path))
    conn.row_factory = sqlite3.Row
    return conn


def table_exists(conn: sqlite3.Connection, table_name: str) -> bool:
    row = conn.execute(
        "SELECT 1 FROM sqlite_master WHERE type='table' AND name=?",
        (table_name,),
    ).fetchone()
    return row is not None


def get_columns(conn: sqlite3.Connection, table_name: str) -> set[str]:
    rows = conn.execute(f"PRAGMA table_info({table_name})").fetchall()
    return {row[1] for row in rows}


def ensure_column(conn: sqlite3.Connection, table_name: str, column_name: str, definition: str) -> bool:
    columns = get_columns(conn, table_name)
    if column_name in columns:
        return False
    conn.execute(f"ALTER TABLE {table_name} ADD COLUMN {column_name} {definition}")
    return True


def normalize_text(value: Any) -> str | None:
    if value is None:
        return None
    text = str(value).strip()
    return text if text else None


def normalize_int(value: Any) -> int | None:
    if value is None:
        return None
    if isinstance(value, int):
        return value
    text = str(value).strip()
    if not text:
        return None
    try:
        return int(float(text))
    except ValueError:
        return None


def choose_text(source_value: Any, target_value: Any, prefer_source: bool) -> str | None:
    source = normalize_text(source_value)
    target = normalize_text(target_value)
    if prefer_source:
        return source if source is not None else target
    return target if target is not None else source


def choose_int(source_value: Any, target_value: Any, prefer_source: bool) -> int | None:
    source = normalize_int(source_value)
    target = normalize_int(target_value)
    if prefer_source:
        return source if source is not None else target
    return target if target is not None else source


def status_to_active(status: Any) -> int:
    text = (normalize_text(status) or "").lower()
    return 1 if text in {"active", "activate", "1", "yes", "true"} else 0


def create_backup(target_db: Path) -> Path:
    backup_dir = target_db.parent / "backups"
    backup_dir.mkdir(parents=True, exist_ok=True)
    ts = datetime.now().strftime("%Y%m%d_%H%M%S")
    backup_path = backup_dir / f"{target_db.stem}_{ts}{target_db.suffix}"
    shutil.copy2(target_db, backup_path)
    return backup_path


def load_source_rows(src_conn: sqlite3.Connection, source_has_aslaas: bool) -> Dict[str, sqlite3.Row]:
    select_sql = (
        """
        SELECT
            account_number,
            account_holder_name,
            amount,
            month_paid_upto,
            next_due_date,
            total_deposit,
            status,
            aslaas_no
        FROM account_detail
        WHERE account_number IS NOT NULL AND TRIM(account_number) <> ''
        """
        if source_has_aslaas
        else
        """
        SELECT
            account_number,
            account_holder_name,
            amount,
            month_paid_upto,
            next_due_date,
            total_deposit,
            status,
            NULL AS aslaas_no
        FROM account_detail
        WHERE account_number IS NOT NULL AND TRIM(account_number) <> ''
        """
    )

    rows = src_conn.execute(select_sql).fetchall()

    source_map: Dict[str, sqlite3.Row] = {}
    for row in rows:
        account_no = normalize_text(row["account_number"])
        if not account_no:
            continue
        source_map[account_no] = row
    return source_map


def sync_account_detail(
    tgt_conn: sqlite3.Connection,
    source_map: Dict[str, sqlite3.Row],
    dry_run: bool,
    prefer_source: bool,
    account_detail_has_aslaas: bool,
    force_aslaas: bool,
) -> SyncStats:
    stats = SyncStats()

    target_rows = tgt_conn.execute(
        (
            """
            SELECT
                account_number,
                account_holder_name,
                amount,
                month_paid_upto,
                next_due_date,
                total_deposit,
                status,
                aslaas_no
            FROM account_detail
            WHERE account_number IS NOT NULL AND TRIM(account_number) <> ''
            """
            if account_detail_has_aslaas
            else
            """
            SELECT
                account_number,
                account_holder_name,
                amount,
                month_paid_upto,
                next_due_date,
                total_deposit,
                status,
                NULL AS aslaas_no
            FROM account_detail
            WHERE account_number IS NOT NULL AND TRIM(account_number) <> ''
            """
        )
    ).fetchall()
    target_map = {normalize_text(r["account_number"]): r for r in target_rows}

    for account_no, src in source_map.items():
        tgt = target_map.get(account_no)

        if tgt is None:
            stats.inserted += 1
            if not dry_run:
                if account_detail_has_aslaas:
                    tgt_conn.execute(
                        """
                        INSERT INTO account_detail (
                            account_number,
                            account_holder_name,
                            amount,
                            month_paid_upto,
                            next_due_date,
                            total_deposit,
                            status,
                            aslaas_no,
                            first_seen,
                            last_updated
                        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                        """,
                        (
                            account_no,
                            normalize_text(src["account_holder_name"]),
                            normalize_int(src["amount"]),
                            normalize_int(src["month_paid_upto"]),
                            normalize_text(src["next_due_date"]),
                            normalize_int(src["total_deposit"]),
                            normalize_text(src["status"]),
                            normalize_text(src["aslaas_no"]),
                        ),
                    )
                else:
                    tgt_conn.execute(
                        """
                        INSERT INTO account_detail (
                            account_number,
                            account_holder_name,
                            amount,
                            month_paid_upto,
                            next_due_date,
                            total_deposit,
                            status,
                            first_seen,
                            last_updated
                        ) VALUES (?, ?, ?, ?, ?, ?, ?, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                        """,
                        (
                            account_no,
                            normalize_text(src["account_holder_name"]),
                            normalize_int(src["amount"]),
                            normalize_int(src["month_paid_upto"]),
                            normalize_text(src["next_due_date"]),
                            normalize_int(src["total_deposit"]),
                            normalize_text(src["status"]),
                        ),
                    )
            continue

        merged_aslaas = choose_text(src["aslaas_no"], tgt["aslaas_no"], prefer_source)
        if force_aslaas:
            source_aslaas = normalize_text(src["aslaas_no"])
            merged_aslaas = source_aslaas if source_aslaas is not None else normalize_text(tgt["aslaas_no"])

        merged = {
            "account_holder_name": choose_text(
                src["account_holder_name"], tgt["account_holder_name"], prefer_source
            ),
            "amount": choose_int(src["amount"], tgt["amount"], prefer_source),
            "month_paid_upto": choose_int(src["month_paid_upto"], tgt["month_paid_upto"], prefer_source),
            "next_due_date": choose_text(src["next_due_date"], tgt["next_due_date"], prefer_source),
            "total_deposit": choose_int(src["total_deposit"], tgt["total_deposit"], prefer_source),
            "status": choose_text(src["status"], tgt["status"], prefer_source),
            "aslaas_no": merged_aslaas,
        }

        current = {
            "account_holder_name": normalize_text(tgt["account_holder_name"]),
            "amount": normalize_int(tgt["amount"]),
            "month_paid_upto": normalize_int(tgt["month_paid_upto"]),
            "next_due_date": normalize_text(tgt["next_due_date"]),
            "total_deposit": normalize_int(tgt["total_deposit"]),
            "status": normalize_text(tgt["status"]),
            "aslaas_no": normalize_text(tgt["aslaas_no"]),
        }

        if merged == current:
            stats.unchanged += 1
            continue

        stats.updated += 1
        if not dry_run:
            if account_detail_has_aslaas:
                tgt_conn.execute(
                    """
                    UPDATE account_detail
                    SET account_holder_name = ?,
                        amount = ?,
                        month_paid_upto = ?,
                        next_due_date = ?,
                        total_deposit = ?,
                        status = ?,
                        aslaas_no = ?,
                        last_updated = CURRENT_TIMESTAMP
                    WHERE account_number = ?
                    """,
                    (
                        merged["account_holder_name"],
                        merged["amount"],
                        merged["month_paid_upto"],
                        merged["next_due_date"],
                        merged["total_deposit"],
                        merged["status"],
                        merged["aslaas_no"],
                        account_no,
                    ),
                )
            else:
                tgt_conn.execute(
                    """
                    UPDATE account_detail
                    SET account_holder_name = ?,
                        amount = ?,
                        month_paid_upto = ?,
                        next_due_date = ?,
                        total_deposit = ?,
                        status = ?,
                        last_updated = CURRENT_TIMESTAMP
                    WHERE account_number = ?
                    """,
                    (
                        merged["account_holder_name"],
                        merged["amount"],
                        merged["month_paid_upto"],
                        merged["next_due_date"],
                        merged["total_deposit"],
                        merged["status"],
                        account_no,
                    ),
                )

    return stats


def sync_rd_accounts(
    tgt_conn: sqlite3.Connection,
    source_map: Dict[str, sqlite3.Row],
    dry_run: bool,
    prefer_source: bool,
    rd_accounts_has_aslaas: bool,
    force_aslaas: bool,
) -> SyncStats:
    stats = SyncStats()

    target_rows = tgt_conn.execute(
        (
            """
            SELECT
                account_no,
                account_name,
                month_paid_upto,
                next_installment_date,
                is_active,
                amount,
                month_paid_upto_num,
                next_due_date_iso,
                total_deposit,
                status,
                aslaas_no
            FROM rd_accounts
            WHERE account_no IS NOT NULL AND TRIM(account_no) <> ''
            """
            if rd_accounts_has_aslaas
            else
            """
            SELECT
                account_no,
                account_name,
                month_paid_upto,
                next_installment_date,
                is_active,
                amount,
                month_paid_upto_num,
                next_due_date_iso,
                total_deposit,
                status,
                NULL AS aslaas_no
            FROM rd_accounts
            WHERE account_no IS NOT NULL AND TRIM(account_no) <> ''
            """
        )
    ).fetchall()
    target_map = {normalize_text(r["account_no"]): r for r in target_rows}

    for account_no, src in source_map.items():
        tgt = target_map.get(account_no)

        src_month_paid_num = normalize_int(src["month_paid_upto"])
        src_next_due = normalize_text(src["next_due_date"])
        src_status = normalize_text(src["status"])

        if tgt is None:
            stats.inserted += 1
            if not dry_run:
                if rd_accounts_has_aslaas:
                    tgt_conn.execute(
                        """
                        INSERT INTO rd_accounts (
                            account_no,
                            account_name,
                            month_paid_upto,
                            next_installment_date,
                            is_active,
                            amount,
                            month_paid_upto_num,
                            next_due_date_iso,
                            total_deposit,
                            status,
                            aslaas_no,
                            first_seen,
                            last_updated
                        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                        """,
                        (
                            account_no,
                            normalize_text(src["account_holder_name"]),
                            str(src_month_paid_num) if src_month_paid_num is not None else None,
                            src_next_due,
                            status_to_active(src_status),
                            normalize_int(src["amount"]),
                            src_month_paid_num,
                            src_next_due,
                            normalize_int(src["total_deposit"]),
                            src_status,
                            normalize_text(src["aslaas_no"]),
                        ),
                    )
                else:
                    tgt_conn.execute(
                        """
                        INSERT INTO rd_accounts (
                            account_no,
                            account_name,
                            month_paid_upto,
                            next_installment_date,
                            is_active,
                            amount,
                            month_paid_upto_num,
                            next_due_date_iso,
                            total_deposit,
                            status,
                            first_seen,
                            last_updated
                        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                        """,
                        (
                            account_no,
                            normalize_text(src["account_holder_name"]),
                            str(src_month_paid_num) if src_month_paid_num is not None else None,
                            src_next_due,
                            status_to_active(src_status),
                            normalize_int(src["amount"]),
                            src_month_paid_num,
                            src_next_due,
                            normalize_int(src["total_deposit"]),
                            src_status,
                        ),
                    )
            continue

        merged_aslaas = choose_text(src["aslaas_no"], tgt["aslaas_no"], prefer_source)
        if force_aslaas:
            source_aslaas = normalize_text(src["aslaas_no"])
            merged_aslaas = source_aslaas if source_aslaas is not None else normalize_text(tgt["aslaas_no"])

        merged = {
            "account_name": choose_text(src["account_holder_name"], tgt["account_name"], prefer_source),
            "month_paid_upto": choose_text(
                str(src_month_paid_num) if src_month_paid_num is not None else None,
                tgt["month_paid_upto"],
                prefer_source,
            ),
            "next_installment_date": choose_text(src_next_due, tgt["next_installment_date"], prefer_source),
            "is_active": status_to_active(
                choose_text(src_status, tgt["status"], prefer_source)
            ),
            "amount": choose_int(src["amount"], tgt["amount"], prefer_source),
            "month_paid_upto_num": choose_int(src_month_paid_num, tgt["month_paid_upto_num"], prefer_source),
            "next_due_date_iso": choose_text(src_next_due, tgt["next_due_date_iso"], prefer_source),
            "total_deposit": choose_int(src["total_deposit"], tgt["total_deposit"], prefer_source),
            "status": choose_text(src_status, tgt["status"], prefer_source),
            "aslaas_no": merged_aslaas,
        }

        current = {
            "account_name": normalize_text(tgt["account_name"]),
            "month_paid_upto": normalize_text(tgt["month_paid_upto"]),
            "next_installment_date": normalize_text(tgt["next_installment_date"]),
            "is_active": int(tgt["is_active"]) if tgt["is_active"] is not None else 0,
            "amount": normalize_int(tgt["amount"]),
            "month_paid_upto_num": normalize_int(tgt["month_paid_upto_num"]),
            "next_due_date_iso": normalize_text(tgt["next_due_date_iso"]),
            "total_deposit": normalize_int(tgt["total_deposit"]),
            "status": normalize_text(tgt["status"]),
            "aslaas_no": normalize_text(tgt["aslaas_no"]),
        }

        if merged == current:
            stats.unchanged += 1
            continue

        stats.updated += 1
        if not dry_run:
            if rd_accounts_has_aslaas:
                tgt_conn.execute(
                    """
                    UPDATE rd_accounts
                    SET account_name = ?,
                        month_paid_upto = ?,
                        next_installment_date = ?,
                        is_active = ?,
                        amount = ?,
                        month_paid_upto_num = ?,
                        next_due_date_iso = ?,
                        total_deposit = ?,
                        status = ?,
                        aslaas_no = ?,
                        last_updated = CURRENT_TIMESTAMP
                    WHERE account_no = ?
                    """,
                    (
                        merged["account_name"],
                        merged["month_paid_upto"],
                        merged["next_installment_date"],
                        merged["is_active"],
                        merged["amount"],
                        merged["month_paid_upto_num"],
                        merged["next_due_date_iso"],
                        merged["total_deposit"],
                        merged["status"],
                        merged["aslaas_no"],
                        account_no,
                    ),
                )
            else:
                tgt_conn.execute(
                    """
                    UPDATE rd_accounts
                    SET account_name = ?,
                        month_paid_upto = ?,
                        next_installment_date = ?,
                        is_active = ?,
                        amount = ?,
                        month_paid_upto_num = ?,
                        next_due_date_iso = ?,
                        total_deposit = ?,
                        status = ?,
                        last_updated = CURRENT_TIMESTAMP
                    WHERE account_no = ?
                    """,
                    (
                        merged["account_name"],
                        merged["month_paid_upto"],
                        merged["next_installment_date"],
                        merged["is_active"],
                        merged["amount"],
                        merged["month_paid_upto_num"],
                        merged["next_due_date_iso"],
                        merged["total_deposit"],
                        merged["status"],
                        account_no,
                    ),
                )

    return stats


def deactivate_missing_rows(
    tgt_conn: sqlite3.Connection,
    source_accounts: Iterable[str],
    sync_rd: bool,
    dry_run: bool,
) -> Tuple[int, int]:
    source_set = {acc for acc in source_accounts if acc}
    if not source_set:
        return (0, 0)

    placeholders = ",".join("?" for _ in source_set)

    account_detail_deactivated = 0
    rd_accounts_deactivated = 0

    query_ad = (
        f"UPDATE account_detail SET status='deactivate', last_updated=CURRENT_TIMESTAMP "
        f"WHERE account_number NOT IN ({placeholders}) "
        "AND (status IS NULL OR LOWER(TRIM(status)) <> 'deactivate')"
    )
    if not dry_run:
        cur = tgt_conn.execute(query_ad, tuple(source_set))
        account_detail_deactivated = cur.rowcount
    else:
        cur = tgt_conn.execute(
            f"SELECT COUNT(*) FROM account_detail WHERE account_number NOT IN ({placeholders}) "
            "AND (status IS NULL OR LOWER(TRIM(status)) <> 'deactivate')",
            tuple(source_set),
        )
        account_detail_deactivated = int(cur.fetchone()[0])

    if sync_rd and table_exists(tgt_conn, "rd_accounts"):
        query_rd = (
            f"UPDATE rd_accounts SET is_active=0, status='deactivate', last_updated=CURRENT_TIMESTAMP "
            f"WHERE account_no NOT IN ({placeholders}) "
            "AND (is_active <> 0 OR status IS NULL OR LOWER(TRIM(status)) <> 'deactivate')"
        )
        if not dry_run:
            cur = tgt_conn.execute(query_rd, tuple(source_set))
            rd_accounts_deactivated = cur.rowcount
        else:
            cur = tgt_conn.execute(
                f"SELECT COUNT(*) FROM rd_accounts WHERE account_no NOT IN ({placeholders}) "
                "AND (is_active <> 0 OR status IS NULL OR LOWER(TRIM(status)) <> 'deactivate')",
                tuple(source_set),
            )
            rd_accounts_deactivated = int(cur.fetchone()[0])

    return (account_detail_deactivated, rd_accounts_deactivated)


def resolve_force_aslaas(args: argparse.Namespace, source_has_aslaas: bool) -> bool:
    if not source_has_aslaas:
        return False

    if args.force_aslaas:
        return True

    if not args.interactive_force_aslaas:
        return False

    if not sys.stdin.isatty():
        print("Interactive ASLAAS force requested, but stdin is not interactive. Using default: disabled.")
        return False

    while True:
        choice = input(
            "Force overwrite ASLAAS from source for matching accounts? This updates current DB values. (y/N): "
        ).strip().lower()
        if choice in {"y", "yes"}:
            return True
        if choice in {"", "n", "no"}:
            return False
        print("Please enter y or n.")


def main() -> int:
    args = parse_args()

    source_db = args.source.expanduser().resolve()
    target_db = args.target.expanduser().resolve()

    if not source_db.exists():
        print(f"ERROR: Source database not found: {source_db}")
        return 1

    if not target_db.exists():
        print(f"ERROR: Target database not found: {target_db}")
        return 1

    print("=" * 78)
    print("LEGACY ACCOUNT DETAIL SYNC")
    print("=" * 78)
    print(f"Source DB : {source_db}")
    print(f"Target DB : {target_db}")
    print(f"Dry run   : {args.dry_run}")
    print(f"Prefer source values: {args.prefer_source_values}")
    print(f"Sync rd_accounts: {not args.skip_rd_accounts}")
    print(f"Deactivate missing: {args.deactivate_missing}")

    src_conn = connect(source_db)
    tgt_conn = connect(target_db)

    try:
        if not table_exists(src_conn, "account_detail"):
            print("ERROR: Source table 'account_detail' not found.")
            return 1
        if not table_exists(tgt_conn, "account_detail"):
            print("ERROR: Target table 'account_detail' not found.")
            return 1

        source_cols = get_columns(src_conn, "account_detail")
        required_source = {
            "account_number",
            "account_holder_name",
            "amount",
            "month_paid_upto",
            "next_due_date",
            "total_deposit",
            "status",
        }
        missing_source_cols = required_source - source_cols
        if missing_source_cols:
            print(f"ERROR: Source table missing required columns: {sorted(missing_source_cols)}")
            return 1

        source_has_aslaas = "aslaas_no" in source_cols
        account_detail_cols = get_columns(tgt_conn, "account_detail")
        account_detail_has_aslaas = "aslaas_no" in account_detail_cols
        if source_has_aslaas and not account_detail_has_aslaas:
            if args.dry_run:
                print("Dry-run: target account_detail missing aslaas_no (would add TEXT column).")
            else:
                ensure_column(tgt_conn, "account_detail", "aslaas_no", "TEXT")
                print("Added target column: account_detail.aslaas_no")
                account_detail_has_aslaas = True

        rd_accounts_has_aslaas = False
        if table_exists(tgt_conn, "rd_accounts"):
            rd_cols = get_columns(tgt_conn, "rd_accounts")
            rd_accounts_has_aslaas = "aslaas_no" in rd_cols

        force_aslaas = resolve_force_aslaas(args, source_has_aslaas)
        print(f"Force ASLAAS overwrite: {force_aslaas}")

        if not args.dry_run and not args.no_backup:
            backup_path = create_backup(target_db)
            print(f"Backup created: {backup_path}")

        source_map = load_source_rows(src_conn, source_has_aslaas)
        print(f"Source rows with account_number: {len(source_map)}")
        print(f"Source has aslaas_no: {source_has_aslaas}")
        print(f"Target account_detail has aslaas_no: {account_detail_has_aslaas}")
        if table_exists(tgt_conn, "rd_accounts"):
            print(f"Target rd_accounts has aslaas_no: {rd_accounts_has_aslaas}")

        if not args.dry_run:
            tgt_conn.execute("BEGIN")

        ad_stats = sync_account_detail(
            tgt_conn,
            source_map,
            args.dry_run,
            prefer_source=args.prefer_source_values,
            account_detail_has_aslaas=account_detail_has_aslaas,
            force_aslaas=force_aslaas,
        )

        rd_stats = SyncStats()
        sync_rd = not args.skip_rd_accounts and table_exists(tgt_conn, "rd_accounts")
        if sync_rd:
            rd_stats = sync_rd_accounts(
                tgt_conn,
                source_map,
                args.dry_run,
                prefer_source=args.prefer_source_values,
                rd_accounts_has_aslaas=rd_accounts_has_aslaas,
                force_aslaas=force_aslaas,
            )

        ad_deactivated = 0
        rd_deactivated = 0
        if args.deactivate_missing:
            ad_deactivated, rd_deactivated = deactivate_missing_rows(
                tgt_conn,
                source_map.keys(),
                sync_rd,
                args.dry_run,
            )

        if args.dry_run:
            print("\nDRY RUN complete. No changes committed.")
        else:
            tgt_conn.commit()
            print("\nSync committed successfully.")

        print("\nSummary:")
        print(
            f"- account_detail: +{ad_stats.inserted} inserted, "
            f"~{ad_stats.updated} updated, ={ad_stats.unchanged} unchanged"
        )
        if args.deactivate_missing:
            print(f"  deactivated (missing from source): {ad_deactivated}")

        if sync_rd:
            print(
                f"- rd_accounts   : +{rd_stats.inserted} inserted, "
                f"~{rd_stats.updated} updated, ={rd_stats.unchanged} unchanged"
            )
            if args.deactivate_missing:
                print(f"  deactivated (missing from source): {rd_deactivated}")
        elif not args.skip_rd_accounts:
            print("- rd_accounts table not found in target; skipped")

        return 0

    except Exception as exc:
        if not args.dry_run:
            tgt_conn.rollback()
        print(f"ERROR: {exc}")
        return 1

    finally:
        src_conn.close()
        tgt_conn.close()


if __name__ == "__main__":
    sys.exit(main())
