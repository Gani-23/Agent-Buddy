"""
India Post DOP Agent Portal - Clipboard Extraction Version
Features:
- Automates Ctrl+A + Ctrl+C to extract popup data
- Parses clean tab-separated text (no HTML parsing!)
- Much faster and more reliable
- Guaranteed to get all 500+ records
"""

import time
import builtins
import argparse
import pandas as pd
from selenium import webdriver
from selenium.webdriver.common.by import By
from selenium.webdriver.support.ui import WebDriverWait
from selenium.webdriver.support import expected_conditions as EC
from selenium.webdriver.chrome.service import Service
from selenium.webdriver.chrome.options import Options
from selenium.webdriver.edge.options import Options as EdgeOptions
from selenium.webdriver.common.keys import Keys
from selenium.webdriver.common.action_chains import ActionChains
from datetime import datetime, timedelta
import os
import sys
import logging
import tempfile
import shutil
from pathlib import Path
import platform
import sqlite3
import hashlib
import getpass
import re
import pyperclip  # For clipboard access
from dop_paths import resolve_base_dir, resolve_documents_dir


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

_original_print = builtins.print


def safe_print(*args, **kwargs):
    """Print wrapper that never crashes on Windows console encoding."""
    try:
        _original_print(*args, **kwargs)
    except UnicodeEncodeError:
        sep = kwargs.get("sep", " ")
        end = kwargs.get("end", "\n")
        text = sep.join(str(a) for a in args)
        fallback = text.encode("ascii", errors="replace").decode("ascii", errors="ignore")
        _original_print(fallback, end=end)


builtins.print = safe_print


class DOPAgentClipboard:
    def __init__(self, browser="chrome"):
        """Initialize the clipboard-based automation"""
        self.driver = None
        self.wait = None
        self.os_type = platform.system()
        self.browser_name = self.normalize_browser_choice(browser)
        self.last_missing_accounts = []
        self._profile_dirs_to_cleanup = []
        self._use_isolated_profile = self.os_type != 'Windows'
        self.setup_paths()
        self.setup_database()
        self.setup_logging()

    @staticmethod
    def normalize_browser_choice(raw_browser):
        """Normalize browser token to one of: chrome, edge, ie, safari."""
        value = (raw_browser or "chrome").strip().lower().replace("-", " ").replace("_", " ")
        value = " ".join(value.split())
        if value in {"edge", "msedge", "microsoft edge"}:
            return "edge"
        if value in {"ie", "internet explorer"}:
            return "ie"
        if value == "safari":
            return "safari"
        return "chrome"
        
    def setup_paths(self):
        """Setup cross-platform file paths"""
        self.documents_dir = resolve_documents_dir()
        base_dir = resolve_base_dir()
        base_dir.mkdir(parents=True, exist_ok=True)

        # Categorized runtime folders
        self.logs_dir = base_dir / 'logs' / 'fetch'
        self.logs_dir.mkdir(parents=True, exist_ok=True)
        self.exports_dir = base_dir / 'exports' / 'accounts'
        self.exports_dir.mkdir(parents=True, exist_ok=True)

        self.db_file = str(base_dir / 'dop_agent.db')
        self.log_file = str(self.logs_dir / f"dop_update_{datetime.now().strftime('%Y%m%d')}.log")
        self.base_dir = base_dir
        
    def setup_database(self):
        """Setup SQLite database"""
        conn = sqlite3.connect(self.db_file)
        cursor = conn.cursor()
        
        # Create credentials table
        cursor.execute('''
            CREATE TABLE IF NOT EXISTS credentials (
                id INTEGER PRIMARY KEY,
                agent_id TEXT NOT NULL,
                password_hash TEXT NOT NULL,
                encrypted_password TEXT,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            )
        ''')
        
        # Migration: Add encrypted_password column if it doesn't exist
        try:
            cursor.execute("SELECT encrypted_password FROM credentials LIMIT 1")
        except sqlite3.OperationalError:
            # Column doesn't exist, add it
            cursor.execute("ALTER TABLE credentials ADD COLUMN encrypted_password TEXT")
            print("✓ Database migrated to new format")
        
        # Create accounts table
        cursor.execute('''
            CREATE TABLE IF NOT EXISTS rd_accounts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                account_no TEXT UNIQUE NOT NULL,
                account_name TEXT,
                denomination TEXT,
                month_paid_upto TEXT,
                next_installment_date TEXT,
                amount INTEGER DEFAULT 0,
                month_paid_upto_num INTEGER DEFAULT 0,
                next_due_date_iso TEXT,
                total_deposit INTEGER DEFAULT 0,
                status TEXT DEFAULT 'inactive',
                first_seen TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                last_updated TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                is_active INTEGER DEFAULT 1
            )
        ''')

        self.ensure_column(cursor, 'rd_accounts', 'amount', 'INTEGER DEFAULT 0')
        self.ensure_column(cursor, 'rd_accounts', 'month_paid_upto_num', 'INTEGER DEFAULT 0')
        self.ensure_column(cursor, 'rd_accounts', 'next_due_date_iso', 'TEXT')
        self.ensure_column(cursor, 'rd_accounts', 'total_deposit', 'INTEGER DEFAULT 0')
        self.ensure_column(cursor, 'rd_accounts', 'status', "TEXT DEFAULT 'inactive'")
        self.ensure_column(cursor, 'rd_accounts', 'aslaas_no', "TEXT DEFAULT ''")
        
        # Create update history table
        cursor.execute('''
            CREATE TABLE IF NOT EXISTS update_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                update_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                total_accounts INTEGER,
                new_accounts INTEGER,
                updated_accounts INTEGER,
                removed_accounts INTEGER,
                active_amount INTEGER DEFAULT 0,
                due_within_30_days INTEGER DEFAULT 0,
                status TEXT
            )
        ''')

        self.ensure_column(cursor, 'update_history', 'active_amount', 'INTEGER DEFAULT 0')
        self.ensure_column(cursor, 'update_history', 'due_within_30_days', 'INTEGER DEFAULT 0')

        # Account detail table (aligned with legacy sample schema for richer dashboard metrics)
        cursor.execute('''
            CREATE TABLE IF NOT EXISTS account_detail (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                account_number TEXT UNIQUE NOT NULL,
                account_holder_name TEXT,
                amount INTEGER,
                month_paid_upto INTEGER,
                next_due_date DATE,
                total_deposit INTEGER,
                status TEXT,
                first_seen TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                last_updated TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            )
        ''')
        cursor.execute('CREATE INDEX IF NOT EXISTS idx_account_detail_status ON account_detail(status)')
        cursor.execute('CREATE INDEX IF NOT EXISTS idx_account_detail_next_due_date ON account_detail(next_due_date)')

        # Closed/matured archive table for accounts removed from current popup fetch.
        cursor.execute('''
            CREATE TABLE IF NOT EXISTS closed_accounts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                account_no TEXT UNIQUE NOT NULL,
                account_name TEXT,
                aslaas_no TEXT DEFAULT '',
                denomination TEXT,
                month_paid_upto TEXT,
                next_installment_date TEXT,
                amount INTEGER DEFAULT 0,
                month_paid_upto_num INTEGER DEFAULT 0,
                next_due_date_iso TEXT,
                total_deposit INTEGER DEFAULT 0,
                status TEXT DEFAULT 'closed',
                first_seen TIMESTAMP,
                last_updated TIMESTAMP,
                closed_on TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                closed_reason TEXT DEFAULT 'missing_from_popup',
                source_update_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            )
        ''')
        cursor.execute('CREATE INDEX IF NOT EXISTS idx_closed_accounts_closed_on ON closed_accounts(closed_on)')
        cursor.execute('CREATE INDEX IF NOT EXISTS idx_closed_accounts_reason ON closed_accounts(closed_reason)')

        # Reference table for real commission/rebate trend from legacy database
        cursor.execute('''
            CREATE TABLE IF NOT EXISTS refrence_data (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                refrence_date DATE,
                refrence_id TEXT UNIQUE,
                monthly_amount INTEGER,
                advanced_amount INTEGER,
                no_of_accounts INTEGER,
                default_fee FLOAT,
                rebate FLOAT,
                total FLOAT,
                tds INTEGER,
                commision INTEGER,
                balance_to_pay INTEGER,
                lot_type TEXT,
                imported_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            )
        ''')

        self.backfill_normalized_account_fields(cursor)
        
        conn.commit()
        conn.close()

    def ensure_column(self, cursor, table_name, column_name, column_def):
        """Add a column if it does not exist"""
        cursor.execute(f"PRAGMA table_info({table_name})")
        existing_columns = {row[1] for row in cursor.fetchall()}
        if column_name not in existing_columns:
            cursor.execute(f"ALTER TABLE {table_name} ADD COLUMN {column_name} {column_def}")
            print(f"✓ Database migrated: added {table_name}.{column_name}")

    def backfill_normalized_account_fields(self, cursor):
        """Backfill normalized numeric/date/status fields for existing records"""
        cursor.execute('''
            SELECT account_no, denomination, month_paid_upto, next_installment_date, is_active
            FROM rd_accounts
        ''')
        rows = cursor.fetchall()

        for account_no, denomination, month_paid_upto, next_installment_date, is_active in rows:
            amount = self.parse_amount(denomination)
            month_paid_num = self.parse_int(month_paid_upto)
            next_due_date_iso = self.parse_due_date_iso(next_installment_date)
            total_deposit = amount * month_paid_num if amount > 0 and month_paid_num > 0 else 0
            status = 'activate' if int(is_active or 0) == 1 else 'deactivate'

            cursor.execute('''
                UPDATE rd_accounts
                SET amount = ?,
                    month_paid_upto_num = ?,
                    next_due_date_iso = ?,
                    total_deposit = ?,
                    status = ?
                WHERE account_no = ?
            ''', (amount, month_paid_num, next_due_date_iso, total_deposit, status, account_no))

            cursor.execute('''
                INSERT INTO account_detail (
                    account_number, account_holder_name, amount, month_paid_upto,
                    next_due_date, total_deposit, status, first_seen, last_updated
                )
                SELECT account_no, account_name, ?, ?, ?, ?, ?, first_seen, last_updated
                FROM rd_accounts
                WHERE account_no = ?
                ON CONFLICT(account_number) DO UPDATE SET
                    account_holder_name = excluded.account_holder_name,
                    amount = excluded.amount,
                    month_paid_upto = excluded.month_paid_upto,
                    next_due_date = excluded.next_due_date,
                    total_deposit = excluded.total_deposit,
                    status = excluded.status,
                    last_updated = excluded.last_updated
            ''', (amount, month_paid_num, next_due_date_iso, total_deposit, status, account_no))

    def parse_amount(self, denomination):
        """Parse denomination like '2,000.00 Cr.' into integer amount"""
        if not denomination:
            return 0

        cleaned = str(denomination).replace('Cr.', '').replace(',', '').strip()
        try:
            return int(round(float(cleaned)))
        except Exception:
            return 0

    def parse_int(self, value):
        """Safe integer parser"""
        if value is None:
            return 0
        text = str(value).strip()
        if not text:
            return 0
        try:
            return int(float(text))
        except Exception:
            return 0

    def parse_due_date_iso(self, value):
        """Convert date text to ISO format (YYYY-MM-DD)"""
        if not value:
            return None

        text = str(value).strip()
        if not text:
            return None

        date_formats = [
            '%d-%b-%Y',   # 13-Jan-2026
            '%d-%B-%Y',   # 13-January-2026
            '%Y-%m-%d',   # 2026-01-13
            '%d/%m/%Y'    # 13/01/2026
        ]

        for fmt in date_formats:
            try:
                parsed = datetime.strptime(text, fmt)
                return parsed.strftime('%Y-%m-%d')
            except ValueError:
                continue

        return None

    def import_reference_data_from_example_db(self):
        """Import real reference/commission history from legacy sample database if available"""
        sample_db_path = self.documents_dir / 'database.sqlite'

        if not sample_db_path.exists():
            self.log_update(f"Sample database not found at {sample_db_path}; skipping reference import")
            return

        try:
            src_conn = sqlite3.connect(str(sample_db_path))
            src_cursor = src_conn.cursor()

            src_cursor.execute(
                "SELECT name FROM sqlite_master WHERE type='table' AND name='refrence_data'"
            )
            if not src_cursor.fetchone():
                src_conn.close()
                self.log_update("Sample database has no refrence_data table; skipping import")
                return

            rows = src_cursor.execute('''
                SELECT refrence_date, refrence_id, monthly_amount, advanced_amount,
                       no_of_accounts, default_fee, rebate, total, tds, commision,
                       balance_to_pay, lot_type
                FROM refrence_data
                WHERE refrence_id IS NOT NULL AND trim(refrence_id) <> ''
            ''').fetchall()
            src_conn.close()

            if not rows:
                self.log_update("No rows found in sample refrence_data; skipping import")
                return

            conn = sqlite3.connect(self.db_file)
            cursor = conn.cursor()

            cursor.executemany('''
                INSERT INTO refrence_data (
                    refrence_date, refrence_id, monthly_amount, advanced_amount,
                    no_of_accounts, default_fee, rebate, total, tds, commision,
                    balance_to_pay, lot_type, imported_at
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, CURRENT_TIMESTAMP)
                ON CONFLICT(refrence_id) DO UPDATE SET
                    refrence_date = excluded.refrence_date,
                    monthly_amount = excluded.monthly_amount,
                    advanced_amount = excluded.advanced_amount,
                    no_of_accounts = excluded.no_of_accounts,
                    default_fee = excluded.default_fee,
                    rebate = excluded.rebate,
                    total = excluded.total,
                    tds = excluded.tds,
                    commision = excluded.commision,
                    balance_to_pay = excluded.balance_to_pay,
                    lot_type = excluded.lot_type,
                    imported_at = CURRENT_TIMESTAMP
            ''', rows)

            conn.commit()
            conn.close()

            self.log_update(f"Imported/updated {len(rows)} rows into refrence_data from sample database")
        except Exception as ex:
            self.log_update(f"Reference import failed: {str(ex)}")
        
    def setup_logging(self):
        """Setup logging"""
        logging.basicConfig(
            level=logging.INFO,
            format='%(asctime)s - %(message)s',
            handlers=[
                logging.FileHandler(self.log_file, encoding='utf-8'),
                logging.StreamHandler()
            ],
            force=True
        )
        
    def log_update(self, message):
        """Log updates to file and console"""
        logging.info(message)
        
    def print_section(self, title):
        """Print formatted section headers"""
        print("\n" + "="*80)
        print(f" {title}")
        print("="*80)
        
    def hash_password(self, password):
        """Hash password for secure storage"""
        return hashlib.sha256(password.encode()).hexdigest()
    
    def encrypt_password(self, password):
        """Simple encryption for password storage (base64)"""
        import base64
        # Simple encoding - for better security, use cryptography library
        return base64.b64encode(password.encode()).decode()
    
    def decrypt_password(self, encrypted):
        """Simple decryption for password retrieval"""
        import base64
        try:
            return base64.b64decode(encrypted.encode()).decode()
        except:
            return None
        
    def save_credentials(self, agent_id, password):
        """Save credentials to database"""
        conn = sqlite3.connect(self.db_file)
        cursor = conn.cursor()
        
        password_hash = self.hash_password(password)
        encrypted_password = self.encrypt_password(password)
        
        # Check if credentials exist
        cursor.execute('SELECT id FROM credentials WHERE id = 1')
        exists = cursor.fetchone()
        
        if exists:
            cursor.execute('''
                UPDATE credentials 
                SET agent_id = ?, password_hash = ?, encrypted_password = ?, updated_at = CURRENT_TIMESTAMP
                WHERE id = 1
            ''', (agent_id, password_hash, encrypted_password))
        else:
            cursor.execute('''
                INSERT INTO credentials (id, agent_id, password_hash, encrypted_password)
                VALUES (1, ?, ?, ?)
            ''', (agent_id, password_hash, encrypted_password))
        
        conn.commit()
        conn.close()
        
    def get_credentials(self):
        """Get credentials from database"""
        conn = sqlite3.connect(self.db_file)
        cursor = conn.cursor()
        
        cursor.execute('SELECT agent_id, password_hash, encrypted_password FROM credentials WHERE id = 1')
        result = cursor.fetchone()
        conn.close()
        
        if result:
            agent_id = result[0]
            password_hash = result[1]
            encrypted_password = result[2]
            
            # Decrypt the password
            if encrypted_password:
                password = self.decrypt_password(encrypted_password)
                return agent_id, password
            else:
                # Old database without encrypted password
                return agent_id, None
        
        return None, None
        
    def setup_driver(self, use_isolated_profile=None, browser_name=None):
        """Setup Selenium driver with options."""
        self.print_section("STEP 1: SETTING UP BROWSER")

        if use_isolated_profile is not None:
            self._use_isolated_profile = bool(use_isolated_profile)

        if browser_name is not None:
            self.browser_name = self.normalize_browser_choice(browser_name)
        selected_browser = self.browser_name

        try:
            if selected_browser == "edge":
                edge_options = EdgeOptions()
                edge_options.add_argument("--start-maximized")
                edge_options.add_argument("--no-first-run")
                edge_options.add_argument("--no-default-browser-check")
                edge_options.add_argument("--disable-default-apps")
                edge_options.add_argument("--disable-popup-blocking")
                edge_options.add_argument("--disable-blink-features=AutomationControlled")
                edge_options.add_experimental_option("excludeSwitches", ["enable-automation"])
                edge_options.add_experimental_option("useAutomationExtension", False)
                if self._use_isolated_profile:
                    profile_dir = tempfile.mkdtemp(prefix="dopagent_edge_")
                    self._profile_dirs_to_cleanup.append(profile_dir)
                    edge_options.add_argument(f"--user-data-dir={profile_dir}")
                    print("✓ Browser mode: isolated profile")
                else:
                    print("✓ Browser mode: standard profile fallback")
                self.driver = webdriver.Edge(options=edge_options)
            elif selected_browser == "safari":
                print("✓ Browser mode: Safari")
                self.driver = webdriver.Safari()
                self.driver.maximize_window()
            elif selected_browser == "ie":
                print("✓ Browser mode: Internet Explorer")
                self.driver = webdriver.Ie()
                self.driver.maximize_window()
            else:
                chrome_options = Options()
                chrome_options.add_argument('--start-maximized')
                chrome_options.add_argument('--no-first-run')
                chrome_options.add_argument('--no-default-browser-check')
                chrome_options.add_argument('--disable-default-apps')
                chrome_options.add_argument('--disable-popup-blocking')
                chrome_options.add_argument('--disable-blink-features=AutomationControlled')
                chrome_options.add_experimental_option("excludeSwitches", ["enable-automation"])
                chrome_options.add_experimental_option('useAutomationExtension', False)

                # Isolated profile works well on macOS/Linux.
                # On some Windows systems it can get stuck at data:, so we allow fallback.
                if self._use_isolated_profile:
                    profile_dir = tempfile.mkdtemp(prefix="dopagent_chrome_")
                    self._profile_dirs_to_cleanup.append(profile_dir)
                    chrome_options.add_argument(f'--user-data-dir={profile_dir}')
                    print("✓ Browser mode: isolated profile")
                else:
                    print("✓ Browser mode: standard profile fallback")

                self.driver = webdriver.Chrome(options=chrome_options)
        except Exception as ex:
            if selected_browser != "chrome":
                print(f"⚠️  Could not start {selected_browser}. Falling back to Chrome. ({str(ex)})")
                self.browser_name = "chrome"
                chrome_options = Options()
                chrome_options.add_argument('--start-maximized')
                chrome_options.add_argument('--no-first-run')
                chrome_options.add_argument('--no-default-browser-check')
                chrome_options.add_argument('--disable-default-apps')
                chrome_options.add_argument('--disable-popup-blocking')
                chrome_options.add_argument('--disable-blink-features=AutomationControlled')
                chrome_options.add_experimental_option("excludeSwitches", ["enable-automation"])
                chrome_options.add_experimental_option('useAutomationExtension', False)
                if self._use_isolated_profile:
                    profile_dir = tempfile.mkdtemp(prefix="dopagent_chrome_")
                    self._profile_dirs_to_cleanup.append(profile_dir)
                    chrome_options.add_argument(f'--user-data-dir={profile_dir}')
                    print("✓ Browser mode: isolated profile")
                else:
                    print("✓ Browser mode: standard profile fallback")
                self.driver = webdriver.Chrome(options=chrome_options)
            else:
                raise

        self.wait = WebDriverWait(self.driver, 20)

        print(f"✓ Browser started successfully ({self.browser_name})")
        self.log_update(f"Browser initialized ({self.browser_name})")

    def cleanup_temp_profiles(self):
        """Cleanup temporary Chrome profile directories"""
        while self._profile_dirs_to_cleanup:
            profile_dir = self._profile_dirs_to_cleanup.pop()
            try:
                shutil.rmtree(profile_dir, ignore_errors=True)
            except Exception:
                pass
        
    def login(self, agent_id=None, password=None, max_attempts=8):
        """Login to DOP Agent portal with CAPTCHA retry handling"""
        self.print_section("STEP 2: LOGGING IN")
        portal_url = "https://dopagent.indiapost.gov.in"

        # Get credentials from database if not provided
        if agent_id is None or password is None:
            saved_id, saved_password = self.get_credentials()

            if saved_id and saved_password:
                print(f"✓ Auto-loading credentials for: {saved_id}")
                agent_id = saved_id
                password = saved_password
            elif saved_id and not saved_password:
                # Old database format - need password
                print(f"✓ Found Agent ID: {saved_id}")
                print("⚠️  Password not stored (old format)")
                password = getpass.getpass("Enter password: ")
                # Re-save with encryption
                self.save_credentials(saved_id, password)
                print("✓ Password saved for next time!")
            else:
                # No saved credentials - prompt for new ones
                print("📝 No saved credentials found")
                agent_id = input("Enter Agent ID: ")
                password = getpass.getpass("Enter Password: ")

                # Ask if they want to save
                save = input("Save credentials for future? (y/n): ").lower()
                if save == 'y':
                    self.save_credentials(agent_id, password)
                    print("✓ Credentials saved!")

        def on_home_or_menu():
            try:
                if self.find_account_search_field(wait_seconds=0):
                    return True
            except Exception:
                pass

            selectors = [
                (By.LINK_TEXT, "Accounts"),
                (By.PARTIAL_LINK_TEXT, "Agent Enquire"),
                (By.PARTIAL_LINK_TEXT, "Logout"),
                (By.PARTIAL_LINK_TEXT, "Log Out")
            ]
            for by, value in selectors:
                try:
                    elements = self.driver.find_elements(by, value)
                    if elements:
                        return True
                except Exception:
                    continue
            return False

        def open_portal_login_page(max_tries=4):
            """Open login URL robustly (Windows sometimes gets stuck on data: tab)."""
            urls_to_try = [
                portal_url,
                "https://dopagent.indiapost.gov.in/",
                "http://dopagent.indiapost.gov.in",
            ]

            def restart_driver_for_navigation():
                try:
                    if self.driver:
                        self.driver.quit()
                except Exception:
                    pass
                self.driver = None
                self.wait = None
                time.sleep(1)
                self.setup_driver(use_isolated_profile=self._use_isolated_profile)

            def switch_to_primary_window():
                try:
                    handles = self.driver.window_handles
                    if not handles:
                        return False
                    # Close extra tabs to avoid getting stuck on wrong/empty tab.
                    for extra in handles[1:]:
                        try:
                            self.driver.switch_to.window(extra)
                            self.driver.close()
                        except Exception:
                            pass
                    self.driver.switch_to.window(handles[0])
                    return True
                except Exception:
                    return False

            def login_form_visible(timeout_sec=4):
                try:
                    WebDriverWait(self.driver, timeout_sec).until(
                        EC.presence_of_element_located((By.NAME, "AuthenticationFG.USER_PRINCIPAL"))
                    )
                    return True
                except Exception:
                    return False

            def on_portal_url():
                try:
                    current_url = (self.driver.current_url or "").strip().lower()
                    return "dopagent.indiapost.gov.in" in current_url
                except Exception:
                    return False

            def on_data_url():
                try:
                    current_url = (self.driver.current_url or "").strip().lower()
                    return current_url.startswith("data:")
                except Exception:
                    return False

            def navigate_via_address_bar(url):
                """Last-resort nav for environments where driver.get sticks on data:"""
                try:
                    body = self.driver.find_element(By.TAG_NAME, "body")
                    body.click()
                except Exception:
                    pass

                actions = ActionChains(self.driver)
                if self.os_type == 'Darwin':
                    actions.key_down(Keys.COMMAND).send_keys('l').key_up(Keys.COMMAND).perform()
                else:
                    actions.key_down(Keys.CONTROL).send_keys('l').key_up(Keys.CONTROL).perform()
                time.sleep(0.2)

                actions = ActionChains(self.driver)
                actions.send_keys(url).send_keys(Keys.ENTER).perform()
                time.sleep(2)
                return on_portal_url() or login_form_visible(timeout_sec=2)

            for nav_try in range(1, max_tries + 1):
                switch_to_primary_window()

                for url in urls_to_try:
                    # Method 1: regular navigate
                    try:
                        self.driver.get(url)
                    except Exception as ex:
                        print(f"⚠️  Navigation try {nav_try} (get {url}) failed: {str(ex)}")

                    time.sleep(2)

                    if on_portal_url() or login_form_visible(timeout_sec=2):
                        return True

                    # Method 2: JS hard redirect
                    try:
                        self.driver.execute_script("window.location.replace(arguments[0]);", url)
                        time.sleep(2)
                        if on_portal_url() or login_form_visible(timeout_sec=2):
                            return True
                    except Exception:
                        pass

                    # Method 3: CDP navigate
                    try:
                        self.driver.execute_cdp_cmd("Page.navigate", {"url": url})
                        time.sleep(2)
                        if on_portal_url() or login_form_visible(timeout_sec=2):
                            return True
                    except Exception:
                        pass

                    # Method 4: open a fresh tab and navigate there
                    try:
                        self.driver.switch_to.new_window('tab')
                        self.driver.get(url)
                        time.sleep(2)
                        if on_portal_url() or login_form_visible(timeout_sec=2):
                            return True
                    except Exception:
                        pass

                    # Method 5: force address-bar navigation (helps on some Windows hosts)
                    try:
                        if navigate_via_address_bar(url):
                            return True
                    except Exception:
                        pass

                    # Method 6: neutral reset + retry (data: -> about:blank -> target)
                    try:
                        self.driver.get("about:blank")
                        time.sleep(0.4)
                        self.driver.get(url)
                        time.sleep(2)
                        if on_portal_url() or login_form_visible(timeout_sec=2):
                            return True
                    except Exception:
                        pass

                try:
                    current_url = (self.driver.current_url or "").strip()
                except Exception:
                    current_url = "(unknown)"
                print(f"⚠️  Portal still not open after try {nav_try}; current URL: {current_url}")

                if nav_try < max_tries:
                    # Windows fallback: isolated profile can get stuck on data:
                    if self.os_type == 'Windows' and self._use_isolated_profile and on_data_url():
                        print("↻ Detected data: lock on Windows. Switching to standard profile mode...")
                        self._use_isolated_profile = False
                    print("↻ Restarting browser session and retrying portal open...")
                    restart_driver_for_navigation()

            return False

        def detect_login_error():
            # Prefer explicit on-page alert block when present.
            try:
                alerts = self.driver.find_elements(By.CSS_SELECTOR, "div.redbg[role='alert']")
                for alert in alerts:
                    message = " ".join((alert.text or "").split()).strip().lower()
                    if not message:
                        continue
                    if "enter the characters that you see in the picture" in message:
                        return "wrong captcha"
                    if any(token in message for token in ["invalid", "incorrect", "wrong", "failed", "password"]):
                        return message[:120]
            except Exception:
                pass

            lower_source = (self.driver.page_source or "").lower()
            patterns = [
                "invalid captcha",
                "incorrect captcha",
                "wrong captcha",
                "invalid verification",
                "incorrect verification",
                "invalid user",
                "authentication failed",
                "invalid credentials"
            ]
            for key in patterns:
                if key in lower_source:
                    return key

            # Generic safety: words like "verification code" exist on normal login form,
            # so only treat as error when paired with negative terms.
            if "verification" in lower_source and any(
                term in lower_source for term in ["invalid", "incorrect", "wrong", "failed"]
            ):
                return "verification error"

            return ""

        def set_field_value(field_name, desired_value):
            """Set input value robustly and verify it"""
            field = self.wait.until(
                EC.presence_of_element_located((By.NAME, field_name))
            )

            try:
                field.click()
            except Exception:
                pass

            try:
                field.clear()
            except Exception:
                pass

            try:
                field.send_keys(Keys.COMMAND, "a")
                field.send_keys(Keys.DELETE)
            except Exception:
                try:
                    field.send_keys(Keys.CONTROL, "a")
                    field.send_keys(Keys.DELETE)
                except Exception:
                    pass

            if desired_value:
                try:
                    field.send_keys(desired_value)
                except Exception:
                    pass

            current_value = (field.get_attribute("value") or "")
            if current_value != desired_value:
                try:
                    self.driver.execute_script(
                        "arguments[0].value = arguments[1];"
                        "arguments[0].dispatchEvent(new Event('input', {bubbles:true}));"
                        "arguments[0].dispatchEvent(new Event('change', {bubbles:true}));",
                        field,
                        desired_value
                    )
                except Exception:
                    pass

            return self.driver.find_element(By.NAME, field_name)

        def clear_captcha_field():
            try:
                field = self.driver.find_element(By.NAME, "AuthenticationFG.VERIFICATION_CODE")
            except Exception:
                return

            try:
                field.click()
            except Exception:
                pass

            try:
                field.clear()
            except Exception:
                pass

            try:
                field.send_keys(Keys.COMMAND, "a")
                field.send_keys(Keys.DELETE)
            except Exception:
                try:
                    field.send_keys(Keys.CONTROL, "a")
                    field.send_keys(Keys.DELETE)
                except Exception:
                    pass

            try:
                self.driver.execute_script(
                    "arguments[0].value='';"
                    "arguments[0].dispatchEvent(new Event('input', {bubbles:true}));"
                    "arguments[0].dispatchEvent(new Event('change', {bubbles:true}));",
                    field
                )
            except Exception:
                pass

        def find_login_button():
            selectors = [
                (By.NAME, "Action.VALIDATE_RM_PLUS_CREDENTIALS_CATCHA_DISABLED"),
                (By.ID, "Action.VALIDATE_RM_PLUS_CREDENTIALS_CATCHA_DISABLED"),
                (By.XPATH, "//input[@type='submit' and contains(@name,'VALIDATE')]"),
                (By.XPATH, "//input[@type='submit' and (contains(@value,'Login') or contains(@value,'Sign'))]")
            ]
            for by, value in selectors:
                try:
                    button = self.driver.find_element(by, value)
                    if button.is_enabled():
                        return button
                except Exception:
                    continue
            return None

        print("\n🌐 Opening DOP Agent Portal...")
        if not open_portal_login_page():
            print("❌ Could not open DOP Agent login page (browser stayed on blank/data tab).")
            return False

        captcha_min_length = 6
        last_submitted_captcha = ""
        for attempt in range(1, max_attempts + 1):
            print(f"\n🔁 Login attempt {attempt}/{max_attempts}")

            try:
                self.wait.until(
                    EC.presence_of_element_located((By.NAME, "AuthenticationFG.VERIFICATION_CODE"))
                )
            except Exception:
                if on_home_or_menu():
                    print("✓ Login successful!")
                    self.log_update(f"Login successful for Agent ID: {agent_id}")
                    return True

                print("⚠️  Login form not available; reloading login page...")
                if not open_portal_login_page():
                    print("⚠️  Could not reload login page. Retrying...")
                continue

            # Always refill ID/password because failed CAPTCHA can reset fields.
            try:
                set_field_value("AuthenticationFG.USER_PRINCIPAL", agent_id or "")
                set_field_value("AuthenticationFG.ACCESS_CODE", password or "")
            except Exception as ex:
                print(f"⚠️  Could not fill credentials reliably: {str(ex)}")
                if not open_portal_login_page():
                    print("⚠️  Could not reload login page. Retrying...")
                continue

            # Reset CAPTCHA field for fresh input.
            clear_captcha_field()

            print("👀 Enter CAPTCHA in browser, then wait for auto-submit...")
            wait_start = time.time()
            captcha_value = ""
            while time.time() - wait_start < 180:
                try:
                    captcha_field = self.driver.find_element(By.NAME, "AuthenticationFG.VERIFICATION_CODE")
                    current_captcha = (captcha_field.get_attribute('value') or "").strip()
                except Exception:
                    current_captcha = ""

                # Avoid immediate re-submit with stale captcha from a failed attempt.
                if len(current_captcha) >= captcha_min_length and current_captcha != last_submitted_captcha:
                    captcha_value = current_captcha
                    break
                time.sleep(0.5)

            if len(captcha_value) < captcha_min_length:
                print(f"⚠️  CAPTCHA was not detected (need {captcha_min_length} chars); retrying...")
                continue

            last_submitted_captcha = captcha_value
            print("🔐 Submitting login...")
            login_button = find_login_button()
            if not login_button:
                print("⚠️  Login button not found; reloading login page...")
                if not open_portal_login_page():
                    print("⚠️  Could not reload login page. Retrying...")
                continue

            try:
                login_button.click()
            except Exception:
                try:
                    self.driver.execute_script("arguments[0].click();", login_button)
                except Exception:
                    print("⚠️  Could not click login button; retrying...")
                    continue

            # Wait for login outcome.
            outcome_start = time.time()
            while time.time() - outcome_start < 25:
                if on_home_or_menu():
                    print("✓ Login successful!")
                    self.log_update(f"Login successful for Agent ID: {agent_id}")
                    return True

                error_hint = detect_login_error()
                if error_hint:
                    print(f"❌ Login not accepted ({error_hint}).")
                    break

                time.sleep(0.7)
            else:
                # Timeout waiting for an explicit state transition.
                if on_home_or_menu():
                    print("✓ Login successful!")
                    self.log_update(f"Login successful for Agent ID: {agent_id}")
                    return True
                print("⚠️  Login response timeout; retrying...")

        print("❌ Login failed after multiple attempts.")
        return False
            
    def navigate_to_accounts(self):
        """Navigate to Agent Enquire & Update Screen"""
        self.print_section("STEP 3: NAVIGATING TO ACCOUNTS")
        
        print("🔍 Looking for Accounts menu...")
        time.sleep(2)
        
        try:
            # Try to find and click "Accounts" link
            accounts_link = self.wait.until(
                EC.element_to_be_clickable((By.LINK_TEXT, "Accounts"))
            )
            accounts_link.click()
            print("✓ Clicked 'Accounts' menu")
            time.sleep(1)
            
            # Click "Agent Enquire & Update Screen"
            enquire_link = self.wait.until(
                EC.element_to_be_clickable((By.LINK_TEXT, "Agent Enquire & Update Screen"))
            )
            enquire_link.click()
            print("✓ Opened 'Agent Enquire & Update Screen'")
            time.sleep(2)
            
            self.log_update("Navigated to Agent Enquire & Update Screen")
            return True
            
        except Exception as e:
            print(f"❌ Navigation failed: {str(e)}")
            return False
            
    def click_list_button(self):
        """Click the list button to open popup - using exact HTML element"""
        self.print_section("STEP 4: OPENING ACCOUNTS LIST POPUP")
        
        print("🔍 Looking for list button (print preview)...")
        
        try:
            # Wait for page to load
            time.sleep(2)
            
            # Use exact HTML element: <img id="printpreview" src="L001/bankuser/images/btn-printscreen.gif">
            # Try multiple strategies to find the button
            list_button = None
            
            strategies = [
                # Strategy 1: By ID (most reliable)
                (By.ID, "printpreview"),
                # Strategy 2: By CSS with ID
                (By.CSS_SELECTOR, "img#printpreview"),
                # Strategy 3: By CSS with src
                (By.CSS_SELECTOR, "img[src*='btn-printscreen.gif']"),
                # Strategy 4: By alt text
                (By.CSS_SELECTOR, "img[alt='Print only main page content']"),
                # Strategy 5: By XPath with ID
                (By.XPATH, "//img[@id='printpreview']"),
            ]
            
            for strategy_type, strategy_value in strategies:
                try:
                    print(f"  Trying: {strategy_value}")
                    list_button = self.wait.until(
                        EC.element_to_be_clickable((strategy_type, strategy_value))
                    )
                    print(f"✓ Found button using: {strategy_value}")
                    break
                except:
                    continue
            
            if not list_button:
                print("❌ Could not find list button with any strategy")
                return False
            
            # Click the button
            list_button.click()
            print("✓ Clicked list button (printpreview)")
            time.sleep(3)
            
            # Check if popup opened
            if len(self.driver.window_handles) > 1:
                print("✓ Popup window detected!")
                self.driver.switch_to.window(self.driver.window_handles[-1])
                print("✓ Switched to popup")
                time.sleep(2)
                return True
            else:
                print("⚠️  Popup might be modal (same window)")
                return True
                
        except Exception as e:
            print(f"❌ Failed to click list button: {str(e)}")
            import traceback
            traceback.print_exc()
            return False

    def click_next_accounts_button(self):
        """Click 'fetch more accounts' button to load next account chunk"""
        selectors = [
            (By.NAME, "Action.NEXT_ACCOUNTS"),
            (By.ID, "Action.NEXT_ACCOUNTS"),
            (By.XPATH, "//input[@name='Action.NEXT_ACCOUNTS']"),
            (By.XPATH, "//input[contains(@name,'NEXT_ACCOUNTS')]"),
            (By.XPATH, "//input[@type='submit' and (contains(@value,'Next') or contains(@title,'Next'))]")
        ]

        for by, value in selectors:
            try:
                button = self.driver.find_element(by, value)
                if not button.is_enabled():
                    continue
                if button.get_attribute("disabled"):
                    continue
                button.click()
                time.sleep(1.5)
                return True
            except Exception:
                continue

        return False

    def switch_to_new_popup(self, previous_handles, wait_seconds=6):
        """Switch to a newly opened popup window, if any."""
        if not previous_handles:
            previous_handles = []

        end_time = time.time() + max(1, wait_seconds)
        while time.time() < end_time:
            try:
                handles = self.driver.window_handles
            except Exception:
                handles = []

            new_handles = [h for h in handles if h not in previous_handles]
            if new_handles:
                try:
                    self.driver.switch_to.window(new_handles[-1])
                    time.sleep(0.8)
                    return True
                except Exception:
                    pass

            time.sleep(0.2)

        return False
            
    def extract_data_via_clipboard(self):
        """Extract data using Ctrl+A + Ctrl+C automation"""
        self.print_section("STEP 5: EXTRACTING DATA VIA CLIPBOARD")
        
        print("📋 Using automated Ctrl+A + Ctrl+C method...")
        print("This is MUCH more reliable than HTML parsing!\n")
        
        # Check if we're in a popup
        if len(self.driver.window_handles) > 1:
            print("✓ Working with popup window")
        else:
            print("✓ Working with current window")
        
        try:
            # Wait for content to load
            time.sleep(2)
            
            # Scroll to ensure all content is loaded (triggers lazy loading)
            print("📜 Scrolling to load all content...")
            self.driver.execute_script("window.scrollTo(0, document.body.scrollHeight);")
            time.sleep(2)
            self.driver.execute_script("window.scrollTo(0, 0);")
            time.sleep(1)
            
            # Clear clipboard first
            pyperclip.copy('')
            
            # Use ActionChains to perform Ctrl+A (Select All)
            print("⌨️  Executing Ctrl+A (Select All)...")
            actions = ActionChains(self.driver)
            
            if self.os_type == 'Darwin':  # macOS
                # Cmd+A on Mac
                actions.key_down(Keys.COMMAND).send_keys('a').key_up(Keys.COMMAND).perform()
            else:
                # Ctrl+A on Windows/Linux
                actions.key_down(Keys.CONTROL).send_keys('a').key_up(Keys.CONTROL).perform()
            
            time.sleep(1)
            
            # Use ActionChains to perform Ctrl+C (Copy)
            print("⌨️  Executing Ctrl+C (Copy)...")
            actions = ActionChains(self.driver)
            
            if self.os_type == 'Darwin':  # macOS
                # Cmd+C on Mac
                actions.key_down(Keys.COMMAND).send_keys('c').key_up(Keys.COMMAND).perform()
            else:
                # Ctrl+C on Windows/Linux
                actions.key_down(Keys.CONTROL).send_keys('c').key_up(Keys.CONTROL).perform()
            
            time.sleep(1)
            
            # Get clipboard content
            print("📥 Reading clipboard content...")
            clipboard_text = pyperclip.paste()
            
            if not clipboard_text:
                print("❌ Clipboard is empty!")
                return []
            
            print(f"✓ Clipboard contains {len(clipboard_text)} characters")
            print(f"✓ First 200 characters:\n{clipboard_text[:200]}...\n")
            
            # Parse the clipboard text
            print("🔍 Parsing clipboard data...")
            all_data = self.parse_clipboard_text(clipboard_text)
            
            print(f"\n✅ Total records extracted: {len(all_data)}")
            self.log_update(f"Extracted {len(all_data)} records via clipboard")
            
            # Try to close popup if we're in one (may already be closed)
            try:
                current_handles = self.driver.window_handles
                if len(current_handles) > 1:
                    # We're still in popup, close it
                    self.driver.close()
                    time.sleep(0.5)
                    # Switch back to main window
                    self.driver.switch_to.window(current_handles[0])
                    print("✓ Closed popup, returned to main window")
                elif len(current_handles) == 1:
                    # Popup already closed, just make sure we're on the main window
                    self.driver.switch_to.window(current_handles[0])
                    print("✓ Popup already closed, switched to main window")
            except Exception as e:
                # Popup already closed or other issue - that's fine, we have the data!
                print(f"⚠️  Popup already closed (that's OK, we got the data!)")
                # Try to get back to main window if any window exists
                try:
                    handles = self.driver.window_handles
                    if handles:
                        self.driver.switch_to.window(handles[0])
                except:
                    pass
            
            return all_data
            
        except Exception as e:
            print(f"❌ Clipboard extraction failed: {str(e)}")
            import traceback
            traceback.print_exc()
            return []
    
    def parse_clipboard_text(self, text):
        """Parse the tab-separated clipboard text into structured data"""
        all_data = []
        lines = text.split('\n')
        
        print(f"  Found {len(lines)} lines in clipboard")
        
        # Find the header line
        header_found = False
        data_started = False
        
        for line_idx, line in enumerate(lines):
            line = line.strip()
            
            if not line:
                continue
            
            # Check if this is the header line
            if 'Account No' in line and 'Account Name' in line:
                print(f"  ✓ Found header at line {line_idx + 1}")
                header_found = True
                data_started = True
                continue
            
            # Skip until we find the header
            if not header_found:
                continue
            
            # Split by tab (clipboard uses tabs as separators)
            parts = line.split('\t')
            
            # Filter out empty parts
            parts = [p.strip() for p in parts if p.strip()]
            
            # We need at least 5 parts (serial no, account no, name, denom, month, date)
            # Or 6 if there's a Select column
            if len(parts) < 5:
                continue
            
            # Determine structure
            # Check if first part is a number (serial number)
            offset = 0
            if parts[0].isdigit() and len(parts[0]) <= 3:
                # First column is serial number
                offset = 1
            
            # Extract account number
            account_no_idx = 0 + offset
            if account_no_idx >= len(parts):
                continue
                
            account_no = parts[account_no_idx]
            
            # Validate account number (should be 12 digits)
            if not account_no.isdigit() or len(account_no) < 10:
                continue
            
            # Extract other fields
            account_name = parts[1 + offset] if (1 + offset) < len(parts) else ""
            denomination = parts[2 + offset] if (2 + offset) < len(parts) else ""
            month_paid = parts[3 + offset] if (3 + offset) < len(parts) else ""
            next_date = parts[4 + offset] if (4 + offset) < len(parts) else ""
            
            # Create record
            row_data = {
                "Account No": account_no,
                "Account Name": account_name,
                "Denomination": denomination,
                "Month Paid Upto": month_paid,
                "Next RD Installment Due Date": next_date
            }
            
            all_data.append(row_data)
            
            # Debug output
            if len(all_data) <= 3 or len(all_data) % 100 == 0:
                print(f"  Row {len(all_data)}: {account_no} | {account_name} | {denomination}")
        
        return all_data

    def fetch_accounts_via_next_accounts_popup(self, max_batches=220, stop_when_found_accounts=None):
        """Fetch accounts by looping NEXT_ACCOUNTS -> printpreview popup copy/parse"""
        self.print_section("STEP 6: FETCHING ACCOUNTS VIA NEXT_ACCOUNTS + POPUP")

        if not self.ensure_accounts_screen():
            print("❌ Could not ensure Agent Enquire & Update Screen")
            return []

        all_records = {}
        seen_batch_signatures = set()
        batch = 0
        stop_set = set()
        if stop_when_found_accounts:
            for account_no in stop_when_found_accounts:
                text = str(account_no).strip()
                if text.isdigit() and len(text) >= 10:
                    stop_set.add(text)
            if stop_set:
                print(f"→ Early-stop target loaded: {len(stop_set)} active account(s)")

        while batch < max_batches:
            batch += 1
            print(f"\n→ Batch {batch}: clicking NEXT_ACCOUNTS")
            if not self.click_next_accounts_button():
                print("✓ NEXT_ACCOUNTS unavailable/disabled. Reached last batch.")
                break

            print(f"→ Batch {batch}: opening popup (printpreview)")
            if not self.click_list_button():
                print("⚠️  Could not open popup; stopping batch loop")
                break

            batch_records = self.extract_data_via_clipboard()
            if not batch_records:
                print("⚠️  Empty popup batch detected; stopping")
                break

            normalized_batch = self.merge_account_records(batch_records)
            account_numbers = [
                str(item.get("Account No", "")).strip()
                for item in normalized_batch
                if str(item.get("Account No", "")).strip().isdigit()
            ]

            if not account_numbers:
                print("⚠️  Popup batch had no valid account numbers; stopping")
                break

            signature = (account_numbers[0], account_numbers[-1], len(account_numbers))
            if signature in seen_batch_signatures:
                print("⚠️  Repeated popup batch detected; stopping")
                break
            seen_batch_signatures.add(signature)

            before_count = len(all_records)
            for item in normalized_batch:
                account_no = str(item.get("Account No", "")).strip()
                if account_no.isdigit() and len(account_no) >= 10:
                    all_records[account_no] = item
            added = len(all_records) - before_count
            print(f"✓ Batch {batch}: {len(normalized_batch)} parsed, {added} new, total {len(all_records)}")

            if stop_set:
                found_active = sum(1 for account_no in stop_set if account_no in all_records)
                if found_active >= len(stop_set):
                    print("✓ All active target accounts found. Stopping early.")
                    break

        if batch >= max_batches:
            print(f"⚠️  Batch safety limit reached at {max_batches}; stopping")

        merged = [all_records[key] for key in sorted(all_records.keys())]
        print(f"\n✅ NEXT_ACCOUNTS popup fetch collected {len(merged)} unique accounts")
        self.log_update(f"NEXT_ACCOUNTS popup fetch collected {len(merged)} accounts")
        return merged

    def load_seed_account_numbers(self):
        """Load seed account numbers from legacy and current databases"""
        seeds = set()
        skipped_accounts = set()

        # Legacy sample database (old dataset with more accounts)
        legacy_db = self.documents_dir / 'database.sqlite'
        if legacy_db.exists():
            try:
                conn = sqlite3.connect(str(legacy_db))
                cursor = conn.cursor()
                cursor.execute('''
                    SELECT DISTINCT account_number
                    FROM account_detail
                    WHERE account_number IS NOT NULL AND trim(account_number) <> ''
                ''')
                for (account_no,) in cursor.fetchall():
                    account_no = str(account_no).strip()
                    if account_no.isdigit() and len(account_no) >= 10:
                        seeds.add(account_no)
                conn.close()
                print(f"✓ Loaded {len(seeds)} seed accounts from legacy database")
            except Exception as ex:
                print(f"⚠️  Could not read legacy seed accounts: {str(ex)}")

        # Current application database
        try:
            conn = sqlite3.connect(self.db_file)
            cursor = conn.cursor()

            # Load permanently skipped accounts first so we can exclude them.
            cursor.execute('''
                SELECT DISTINCT account_no
                FROM rd_accounts
                WHERE account_no IS NOT NULL
                  AND trim(account_no) <> ''
                  AND status = 'inactive_skip'
            ''')
            for (account_no,) in cursor.fetchall():
                account_no = str(account_no).strip()
                if account_no.isdigit() and len(account_no) >= 10:
                    skipped_accounts.add(account_no)

            cursor.execute('''
                SELECT DISTINCT account_no
                FROM rd_accounts
                WHERE account_no IS NOT NULL
                  AND trim(account_no) <> ''
                  AND (status IS NULL OR status <> 'inactive_skip')
            ''')
            current_count_before = len(seeds)
            for (account_no,) in cursor.fetchall():
                account_no = str(account_no).strip()
                if account_no.isdigit() and len(account_no) >= 10:
                    seeds.add(account_no)
            conn.close()
            print(f"✓ Loaded {len(seeds) - current_count_before} additional seeds from current database")
        except Exception as ex:
            print(f"⚠️  Could not read current seed accounts: {str(ex)}")

        if skipped_accounts:
            before_exclusion = len(seeds)
            seeds.difference_update(skipped_accounts)
            excluded = before_exclusion - len(seeds)
            if excluded > 0:
                print(f"✓ Excluded {excluded} permanently skipped accounts from seed list")

        return sorted(seeds)

    def load_active_account_numbers(self):
        """Load currently active account numbers from application database"""
        active_accounts = []
        try:
            conn = sqlite3.connect(self.db_file)
            cursor = conn.cursor()
            cursor.execute('''
                SELECT DISTINCT account_no
                FROM rd_accounts
                WHERE account_no IS NOT NULL
                  AND trim(account_no) <> ''
                  AND is_active = 1
                  AND (status IS NULL OR status <> 'inactive_skip')
                ORDER BY account_no
            ''')
            for (account_no,) in cursor.fetchall():
                text = str(account_no).strip()
                if text.isdigit() and len(text) >= 10:
                    active_accounts.append(text)
            conn.close()
        except Exception as ex:
            print(f"⚠️  Could not read active accounts: {str(ex)}")

        print(f"✓ Loaded {len(active_accounts)} active accounts from current database")
        return active_accounts

    def sync_seed_accounts_to_database(self, account_numbers):
        """Seed account numbers into rd_accounts so missing numbers are tracked"""
        if not account_numbers:
            return 0

        conn = sqlite3.connect(self.db_file)
        cursor = conn.cursor()
        current_time = datetime.now().strftime('%Y-%m-%d %H:%M:%S')

        cursor.execute("SELECT COUNT(*) FROM rd_accounts")
        before_count = cursor.fetchone()[0]

        seed_rows = [(acc, current_time, current_time) for acc in account_numbers]
        cursor.executemany('''
            INSERT OR IGNORE INTO rd_accounts
            (account_no, account_name, denomination, month_paid_upto,
             next_installment_date, amount, month_paid_upto_num, next_due_date_iso,
             total_deposit, status, first_seen, last_updated, is_active)
            VALUES (?, '', '', '', '', 0, 0, NULL, 0, 'deactivate', ?, ?, 0)
        ''', seed_rows)

        cursor.execute("SELECT COUNT(*) FROM rd_accounts")
        after_count = cursor.fetchone()[0]
        conn.commit()
        conn.close()

        added = max(0, after_count - before_count)
        print(f"✓ Synced seed accounts to rd_accounts (added {added})")
        return added

    def ensure_accounts_screen(self):
        """Ensure we are on the Agent Enquire & Update Screen with search controls"""
        if self.find_account_search_field(wait_seconds=1):
            return True
        return self.navigate_to_accounts()

    def find_account_search_field(self, wait_seconds=10):
        """Find account search field using resilient selectors"""
        selectors = [
            (By.NAME, "CustomAgentRDAccountFG.ACCOUNT_NUMBER_FOR_SEARCH"),
            (By.ID, "CustomAgentRDAccountFG.ACCOUNT_NUMBER_FOR_SEARCH")
        ]

        for by, value in selectors:
            try:
                if wait_seconds > 0:
                    return WebDriverWait(self.driver, wait_seconds).until(
                        EC.presence_of_element_located((by, value))
                    )
                return self.driver.find_element(by, value)
            except Exception:
                continue

        return None

    def find_fetch_button(self, wait_seconds=10):
        """Find Fetch button"""
        selectors = [
            (By.NAME, "Action.FETCH_INPUT_ACCOUNT"),
            (By.ID, "Action.FETCH_INPUT_ACCOUNT"),
            (By.XPATH, "//input[@type='submit' and @value='Fetch']"),
        ]

        for by, value in selectors:
            try:
                if wait_seconds > 0:
                    return WebDriverWait(self.driver, wait_seconds).until(
                        EC.element_to_be_clickable((by, value))
                    )
                element = self.driver.find_element(by, value)
                if element.is_enabled():
                    return element
            except Exception:
                continue

        return None

    def clear_account_search(self):
        """Clear current account search criteria if possible"""
        selectors = [
            (By.NAME, "Action.CLEAR_ACCOUNTS"),
            (By.ID, "Action.CLEAR_ACCOUNTS"),
            (By.XPATH, "//input[@type='submit' and @value='Clear Account']")
        ]

        for by, value in selectors:
            try:
                button = self.driver.find_element(by, value)
                if button.is_enabled():
                    button.click()
                    time.sleep(0.8)
                    return True
            except Exception:
                continue

        # Fallback: clear textarea directly if the clear button is unavailable.
        field = self.find_account_search_field(wait_seconds=2)
        if field:
            try:
                field.clear()
                field.send_keys(Keys.COMMAND, "a")
                field.send_keys(Keys.DELETE)
                time.sleep(0.3)
                return True
            except Exception:
                try:
                    field.clear()
                    field.send_keys(Keys.CONTROL, "a")
                    field.send_keys(Keys.DELETE)
                    time.sleep(0.3)
                    return True
                except Exception:
                    pass

        return False

    def get_result_rows_from_current_page(self):
        """Parse result rows from the current listing page"""
        records = []

        try:
            rows = self.driver.find_elements(By.XPATH, "//table[@id='SummaryList']//tr[@id]")
            for row in rows:
                row_id = (row.get_attribute("id") or "").strip()
                if not row_id.isdigit():
                    continue

                cells = row.find_elements(By.TAG_NAME, "td")
                if len(cells) < 6:
                    continue

                account_no = cells[1].text.strip()
                if not account_no.isdigit() or len(account_no) < 10:
                    continue

                records.append({
                    "Account No": account_no,
                    "Account Name": cells[2].text.strip(),
                    "Denomination": cells[3].text.strip(),
                    "Month Paid Upto": cells[4].text.strip(),
                    "Next RD Installment Due Date": cells[5].text.strip()
                })
        except Exception as ex:
            print(f"⚠️  Could not parse listing rows: {str(ex)}")

        return records

    def go_to_next_result_page(self):
        """Go to next result page if available"""
        selectors = [
            (By.NAME, "Action.AgentRDActSummaryAllListing.GOTO_NEXT__"),
            (By.ID, "Action.AgentRDActSummaryAllListing.GOTO_NEXT__"),
            (By.XPATH, "//input[@type='submit' and (@title='Next' or @value='>')]")
        ]

        for by, value in selectors:
            try:
                next_button = self.driver.find_element(by, value)
                if not next_button.is_enabled():
                    continue
                if next_button.get_attribute("disabled"):
                    continue

                next_button.click()
                time.sleep(1.5)
                return True
            except Exception:
                continue

        return False

    def go_to_previous_result_page(self):
        """Go to previous result page if available"""
        selectors = [
            (By.NAME, "Action.AgentRDActSummaryAllListing.GOTO_PREV__"),
            (By.ID, "Action.AgentRDActSummaryAllListing.GOTO_PREV__"),
            (By.XPATH, "//input[@type='submit' and (@title='Previous' or @value='<')]")
        ]

        for by, value in selectors:
            try:
                prev_button = self.driver.find_element(by, value)
                if not prev_button.is_enabled():
                    continue
                if prev_button.get_attribute("disabled"):
                    continue

                prev_button.click()
                time.sleep(1.0)
                return True
            except Exception:
                continue

        return False

    def move_to_first_result_page(self, max_steps=100):
        """Best-effort navigation back to page 1"""
        steps = 0
        while steps < max_steps and self.go_to_previous_result_page():
            steps += 1
        return steps

    def execute_search_pass(self, target_accounts, pass_label="Pass", max_pages=220):
        """Execute one search pass for given account numbers and parse all result pages"""
        if not target_accounts:
            return {}

        normalized_targets = []
        for account_no in target_accounts:
            text = str(account_no).strip()
            if '_' in text:
                text = text.split('_', 1)[0].strip()
            if text.isdigit() and len(text) >= 10:
                normalized_targets.append(text)

        if not normalized_targets:
            return {}

        target_set = set(normalized_targets)
        query_string = ",".join(normalized_targets)
        print(f"\n→ {pass_label}: searching {len(normalized_targets)} account numbers")

        # Stay on same page: clear previous query then fetch next batch.
        self.clear_account_search()

        search_field = self.find_account_search_field(wait_seconds=10)
        if not search_field:
            print("❌ Search field not found")
            return {}

        search_field.clear()
        search_field.send_keys(query_string)

        fetch_button = self.find_fetch_button(wait_seconds=10)
        if not fetch_button:
            print("❌ Fetch button not found")
            return {}

        fetch_button.click()
        time.sleep(2.0)

        parsed_records = {}
        saw_match = False
        page_counter = 0
        seen_page_signatures = set()
        consecutive_empty_pages = 0

        while True:
            page_counter += 1
            current_page_records = self.get_result_rows_from_current_page()
            if current_page_records:
                consecutive_empty_pages = 0
                page_signature = (
                    current_page_records[0]["Account No"],
                    current_page_records[-1]["Account No"],
                    len(current_page_records)
                )
            else:
                consecutive_empty_pages += 1
                try:
                    summary_text = " ".join(
                        self.driver.find_element(By.ID, "SummaryList").text.split()
                    )
                except Exception:
                    summary_text = ""
                page_signature = ("EMPTY", summary_text[:240], len(summary_text))

            if page_signature in seen_page_signatures:
                print(f"⚠️  {pass_label}: repeated page detected, stopping pagination loop")
                break
            seen_page_signatures.add(page_signature)

            for record in current_page_records:
                account_no = record["Account No"]
                if account_no in target_set:
                    saw_match = True
                parsed_records[account_no] = record

            if consecutive_empty_pages >= 2:
                print(f"⚠️  {pass_label}: no parsable rows on consecutive pages, stopping pagination loop")
                break

            if not self.go_to_next_result_page():
                break

            if page_counter >= max_pages:
                print(f"⚠️  {pass_label}: pagination safety limit reached at {max_pages} pages")
                break

        if parsed_records and not saw_match:
            print(f"⚠️  {pass_label}: results did not match requested accounts (stale page or rejected input)")
            return {}

        print(f"✓ {pass_label}: parsed {len(parsed_records)} unique accounts")
        return parsed_records

    def get_effective_search_char_limit(self, default_limit=1800):
        """Get effective char limit for account-number search payload"""
        field = self.find_account_search_field(wait_seconds=3)
        if field is None:
            return default_limit

        max_length = (field.get_attribute("maxlength") or "").strip()
        if max_length.isdigit():
            parsed = int(max_length)
            if parsed > 0:
                # Keep some headroom for unexpected client-side processing.
                return max(50, parsed - 10)

        # No explicit maxlength in DOM (as in your HTML), use safe request-sized chunks.
        return default_limit

    def split_accounts_by_char_limit(self, account_numbers, max_chars):
        """Split account numbers into comma-separated chunks by char limit"""
        if max_chars <= 0:
            return [account_numbers] if account_numbers else []

        chunks = []
        current = []
        current_len = 0

        for account_no in account_numbers:
            account_no = str(account_no).strip()
            if not account_no:
                continue

            add_len = len(account_no) if not current else len(account_no) + 1  # include comma

            if current and current_len + add_len > max_chars:
                chunks.append(current)
                current = [account_no]
                current_len = len(account_no)
            else:
                current.append(account_no)
                current_len += add_len

        if current:
            chunks.append(current)

        return chunks

    def fetch_accounts_by_search(self, account_numbers):
        """Fetch account details using char-limited search chunks, then retry unresolved accounts"""
        self.print_section("STEP 6: FETCHING ACCOUNTS VIA SEARCH + PAGINATION")
        self.last_missing_accounts = []

        if not account_numbers:
            print("⚠️  No seed account numbers available for search-based fetch")
            return []

        # Do not navigate per chunk/pass; stay on the same page and reuse search controls.
        if not self.find_account_search_field(wait_seconds=10):
            print("❌ Account search field is not available on current page")
            return []

        unique_accounts = []
        seen = set()
        for account_no in account_numbers:
            text = str(account_no).strip()
            if '_' in text:
                text = text.split('_', 1)[0].strip()
            if not text.isdigit() or len(text) < 10:
                continue
            if text in seen:
                continue
            seen.add(text)
            unique_accounts.append(text)

        unique_accounts.sort()
        all_records = {}

        max_chars = self.get_effective_search_char_limit(default_limit=1800)
        chunks = self.split_accounts_by_char_limit(unique_accounts, max_chars)
        print(f"→ First pass: {len(chunks)} chunks (approx char limit {max_chars})")

        for index, chunk in enumerate(chunks, 1):
            pass_label = f"First pass chunk {index}/{len(chunks)}"
            chunk_result = self.execute_search_pass(chunk, pass_label=pass_label)
            all_records.update(chunk_result)

        missing = [acc for acc in unique_accounts if acc not in all_records]
        print(f"→ Missing after first pass: {len(missing)}")

        # Retry unresolved accounts once at the end with smaller chunks, then skip still missing.
        if missing:
            retry_chars = max(600, max_chars // 2)
            retry_chunks = self.split_accounts_by_char_limit(missing, retry_chars)
            print(f"→ Retry pass: {len(retry_chunks)} chunks (approx char limit {retry_chars})")

            for index, chunk in enumerate(retry_chunks, 1):
                pass_label = f"Retry chunk {index}/{len(retry_chunks)}"
                retry_result = self.execute_search_pass(chunk, pass_label=pass_label)
                all_records.update(retry_result)

            missing = [acc for acc in unique_accounts if acc not in all_records]

        if missing:
            print(f"⚠️  Still not found after retry: {len(missing)} accounts (skipping)")
            preview = ", ".join(missing[:20])
            if preview:
                print(f"   Skipped sample: {preview}")
        self.last_missing_accounts = sorted(missing)

        print(f"\n✅ Search-based fetch collected {len(all_records)} unique accounts")
        self.log_update(
            f"Search-based fetch collected {len(all_records)} accounts; skipped {len(missing)} not found"
        )

        return [all_records[key] for key in sorted(all_records.keys())]

    def click_if_present(self, selectors, wait_seconds=6):
        """Click first clickable element from selectors list"""
        for by, value in selectors:
            try:
                if wait_seconds > 0:
                    element = WebDriverWait(self.driver, wait_seconds).until(
                        EC.element_to_be_clickable((by, value))
                    )
                else:
                    element = self.driver.find_element(by, value)
                element.click()
                return True
            except Exception:
                continue
        return False

    def open_update_aslaas_screen(self):
        """Navigate to Accounts -> Update ASLAAS Number screen"""
        try:
            self.driver.find_element(By.NAME, "CustomAgentAslaasNoFG.RD_ACC_NO")
            return True
        except Exception:
            pass

        accounts_selectors = [
            (By.NAME, "HREF_Accounts"),
            (By.ID, "HREF_Accounts"),
            (By.LINK_TEXT, "Accounts"),
            (By.PARTIAL_LINK_TEXT, "Accounts")
        ]
        update_aslaas_selectors = [
            (By.NAME, "HREF_Update ASLAAS Number"),
            (By.ID, "HREF_Update ASLAAS Number"),
            (By.LINK_TEXT, "Update ASLAAS Number"),
            (By.PARTIAL_LINK_TEXT, "Update ASLAAS")
        ]

        if not self.click_if_present(accounts_selectors, wait_seconds=8):
            return False
        time.sleep(0.8)
        if not self.click_if_present(update_aslaas_selectors, wait_seconds=8):
            return False
        time.sleep(1.2)

        try:
            WebDriverWait(self.driver, 10).until(
                EC.presence_of_element_located((By.NAME, "CustomAgentAslaasNoFG.RD_ACC_NO"))
            )
            return True
        except Exception:
            return False

    def update_aslaas_for_account(self, account_no, aslaas_value="APPLIED"):
        """Submit one account in Update ASLAAS Number form"""
        try:
            account_field = WebDriverWait(self.driver, 8).until(
                EC.presence_of_element_located((By.NAME, "CustomAgentAslaasNoFG.RD_ACC_NO"))
            )
            aslaas_field = WebDriverWait(self.driver, 8).until(
                EC.presence_of_element_located((By.NAME, "CustomAgentAslaasNoFG.ASLAAS_NO"))
            )

            account_field.clear()
            account_field.send_keys(str(account_no).strip())

            aslaas_field.clear()
            aslaas_field.send_keys(aslaas_value)

            continue_selectors = [
                (By.NAME, "Action.LOAD_CONFIRM_PAGE"),
                (By.ID, "Action.LOAD_CONFIRM_PAGE"),
                (By.XPATH, "//input[@type='submit' and (contains(@value,'Continue') or contains(@name,'LOAD_CONFIRM_PAGE'))]")
            ]
            if not self.click_if_present(continue_selectors, wait_seconds=5):
                return False

            time.sleep(0.8)
            return True
        except Exception:
            return False

    def recover_missing_accounts_via_aslaas(self, missing_accounts):
        """Try to activate/search-missing accounts by posting APPLIED in Update ASLAAS Number screen"""
        self.print_section("STEP 6A: RECOVERY VIA UPDATE ASLAAS NUMBER")

        normalized = []
        seen = set()
        for account_no in missing_accounts:
            text = str(account_no).strip()
            if text.isdigit() and len(text) >= 10 and text not in seen:
                seen.add(text)
                normalized.append(text)

        if not normalized:
            print("⚠️  No valid missing accounts for ASLAAS recovery")
            return []

        if not self.open_update_aslaas_screen():
            print("⚠️  Could not open Update ASLAAS Number screen; skipping recovery step")
            return normalized

        failed_updates = []
        total = len(normalized)
        print(f"→ Updating ASLAAS='APPLIED' for {total} account(s)")
        for index, account_no in enumerate(normalized, 1):
            try:
                self.driver.find_element(By.NAME, "CustomAgentAslaasNoFG.RD_ACC_NO")
            except Exception:
                if not self.open_update_aslaas_screen():
                    failed_updates.append(account_no)
                    print(f"   [{index}/{total}] failed {account_no} (cannot open ASLAAS form)")
                    continue

            ok = self.update_aslaas_for_account(account_no, aslaas_value="APPLIED")
            if ok:
                if index <= 5 or index % 50 == 0 or index == total:
                    print(f"   [{index}/{total}] updated {account_no}")
            else:
                failed_updates.append(account_no)
                print(f"   [{index}/{total}] failed {account_no}")

        self.log_update(
            f"ASLAAS recovery attempted for {total}; failed submissions {len(failed_updates)}"
        )

        if not self.navigate_to_accounts():
            print("⚠️  Could not return to Agent Enquire screen after ASLAAS update")
        return failed_updates

    def mark_accounts_inactive_skip(self, account_numbers):
        """Mark unresolved accounts inactive and excluded from future seed searches"""
        if not account_numbers:
            return 0

        normalized = []
        seen = set()
        for account_no in account_numbers:
            text = str(account_no).strip()
            if text.isdigit() and len(text) >= 10 and text not in seen:
                seen.add(text)
                normalized.append(text)

        if not normalized:
            return 0

        conn = sqlite3.connect(self.db_file)
        cursor = conn.cursor()
        current_time = datetime.now().strftime('%Y-%m-%d %H:%M:%S')
        placeholders = ",".join(["?"] * len(normalized))

        cursor.execute(f'''
            UPDATE rd_accounts
            SET is_active = 0, status = 'inactive_skip', last_updated = ?
            WHERE account_no IN ({placeholders})
        ''', [current_time] + normalized)
        affected_rd = cursor.rowcount

        cursor.execute(f'''
            UPDATE account_detail
            SET status = 'inactive_skip', last_updated = ?
            WHERE account_number IN ({placeholders})
        ''', [current_time] + normalized)

        conn.commit()
        conn.close()

        print(f"✓ Marked {affected_rd} account(s) as inactive_skip (excluded from future searches)")
        self.log_update(f"Marked {affected_rd} unresolved accounts as inactive_skip")
        return affected_rd

    def merge_account_records(self, *record_lists):
        """Merge record lists by account number, last record wins"""
        merged = {}

        for records in record_lists:
            if not records:
                continue

            for record in records:
                account_no = str(record.get('Account No', '')).strip()
                if account_no.isdigit() and len(account_no) >= 10:
                    merged[account_no] = record

        return [merged[key] for key in sorted(merged.keys())]
    
    def save_to_database(self, data_list, delete_missing=True):
        """Save extracted data to database and detect changes"""
        self.print_section("STEP 7: SAVING TO DATABASE")
        
        conn = sqlite3.connect(self.db_file)
        cursor = conn.cursor()
        
        current_time = datetime.now().strftime('%Y-%m-%d %H:%M:%S')
        new_accounts = []
        updated_accounts = 0
        active_amount = 0
        due_within_30_days = 0
        today = datetime.now().date()
        due_limit = today + timedelta(days=30)
        
        for record in data_list:
            account_no = record.get('Account No', '')
            account_name = record.get('Account Name', '')
            denomination = record.get('Denomination', '')
            month_paid = record.get('Month Paid Upto', '')
            next_date = record.get('Next RD Installment Due Date', '')
            amount = self.parse_amount(denomination)
            month_paid_num = self.parse_int(month_paid)
            next_due_date_iso = self.parse_due_date_iso(next_date)
            total_deposit = amount * month_paid_num if amount > 0 and month_paid_num > 0 else 0

            active_amount += amount
            if next_due_date_iso:
                try:
                    due_date = datetime.strptime(next_due_date_iso, '%Y-%m-%d').date()
                    if today <= due_date <= due_limit:
                        due_within_30_days += 1
                except Exception:
                    pass
            
            # Check if account exists
            cursor.execute('SELECT * FROM rd_accounts WHERE account_no = ?', (account_no,))
            existing = cursor.fetchone()
            
            if existing:
                # Update existing account
                cursor.execute('''
                    UPDATE rd_accounts 
                    SET account_name = ?, denomination = ?, month_paid_upto = ?, 
                        next_installment_date = ?, amount = ?, month_paid_upto_num = ?,
                        next_due_date_iso = ?, total_deposit = ?, status = 'activate',
                        last_updated = ?, is_active = 1
                    WHERE account_no = ?
                ''', (
                    account_name, denomination, month_paid, next_date,
                    amount, month_paid_num, next_due_date_iso, total_deposit,
                    current_time, account_no
                ))
                updated_accounts += 1
            else:
                # Insert new account
                cursor.execute('''
                    INSERT INTO rd_accounts 
                    (account_no, account_name, denomination, month_paid_upto, 
                     next_installment_date, amount, month_paid_upto_num, next_due_date_iso,
                     total_deposit, status, first_seen, last_updated, is_active)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 'activate', ?, ?, 1)
                ''', (
                    account_no, account_name, denomination, month_paid, next_date,
                    amount, month_paid_num, next_due_date_iso, total_deposit,
                    current_time, current_time
                ))
                new_accounts.append(account_no)

            # Maintain account_detail compatibility table for richer dashboard queries
            cursor.execute('''
                INSERT INTO account_detail (
                    account_number, account_holder_name, amount, month_paid_upto,
                    next_due_date, total_deposit, status, first_seen, last_updated
                )
                VALUES (?, ?, ?, ?, ?, ?, 'activate', ?, ?)
                ON CONFLICT(account_number) DO UPDATE SET
                    account_holder_name = excluded.account_holder_name,
                    amount = excluded.amount,
                    month_paid_upto = excluded.month_paid_upto,
                    next_due_date = excluded.next_due_date,
                    total_deposit = excluded.total_deposit,
                    status = 'activate',
                    last_updated = excluded.last_updated
            ''', (
                account_no, account_name, amount, month_paid_num,
                next_due_date_iso, total_deposit, current_time, current_time
            ))
        
        # For full refresh: delete DB rows that are not present in fetched popup data.
        current_accounts = [r.get('Account No') for r in data_list]
        removed_accounts = 0
        if delete_missing:
            if current_accounts:
                placeholders = ','.join('?' * len(current_accounts))
                # Archive accounts that disappeared from latest portal popup before removing.
                cursor.execute(f'''
                    INSERT INTO closed_accounts (
                        account_no, account_name, aslaas_no, denomination, month_paid_upto,
                        next_installment_date, amount, month_paid_upto_num, next_due_date_iso,
                        total_deposit, status, first_seen, last_updated, closed_on, closed_reason, source_update_time
                    )
                    SELECT
                        account_no,
                        account_name,
                        COALESCE(aslaas_no, ''),
                        denomination,
                        month_paid_upto,
                        next_installment_date,
                        COALESCE(amount, 0),
                        COALESCE(month_paid_upto_num, 0),
                        COALESCE(next_due_date_iso, ''),
                        COALESCE(total_deposit, 0),
                        'closed',
                        first_seen,
                        last_updated,
                        ?,
                        'missing_from_popup',
                        ?
                    FROM rd_accounts
                    WHERE account_no NOT IN ({placeholders})
                    ON CONFLICT(account_no) DO UPDATE SET
                        account_name = excluded.account_name,
                        aslaas_no = excluded.aslaas_no,
                        denomination = excluded.denomination,
                        month_paid_upto = excluded.month_paid_upto,
                        next_installment_date = excluded.next_installment_date,
                        amount = excluded.amount,
                        month_paid_upto_num = excluded.month_paid_upto_num,
                        next_due_date_iso = excluded.next_due_date_iso,
                        total_deposit = excluded.total_deposit,
                        status = excluded.status,
                        first_seen = excluded.first_seen,
                        last_updated = excluded.last_updated,
                        closed_on = excluded.closed_on,
                        closed_reason = excluded.closed_reason,
                        source_update_time = excluded.source_update_time
                ''', [current_time, current_time] + current_accounts)

                cursor.execute(f'''
                    DELETE FROM rd_accounts
                    WHERE account_no NOT IN ({placeholders})
                ''', current_accounts)
                removed_accounts = cursor.rowcount

                cursor.execute(f'''
                    DELETE FROM account_detail
                    WHERE account_number NOT IN ({placeholders})
                ''', current_accounts)

                # If previously closed accounts appear again, clear them from archive.
                cursor.execute(f'''
                    DELETE FROM closed_accounts
                    WHERE account_no IN ({placeholders})
                ''', current_accounts)
            else:
                cursor.execute('''
                    INSERT INTO closed_accounts (
                        account_no, account_name, aslaas_no, denomination, month_paid_upto,
                        next_installment_date, amount, month_paid_upto_num, next_due_date_iso,
                        total_deposit, status, first_seen, last_updated, closed_on, closed_reason, source_update_time
                    )
                    SELECT
                        account_no,
                        account_name,
                        COALESCE(aslaas_no, ''),
                        denomination,
                        month_paid_upto,
                        next_installment_date,
                        COALESCE(amount, 0),
                        COALESCE(month_paid_upto_num, 0),
                        COALESCE(next_due_date_iso, ''),
                        COALESCE(total_deposit, 0),
                        'closed',
                        first_seen,
                        last_updated,
                        ?,
                        'missing_from_popup',
                        ?
                    FROM rd_accounts
                    ON CONFLICT(account_no) DO UPDATE SET
                        account_name = excluded.account_name,
                        aslaas_no = excluded.aslaas_no,
                        denomination = excluded.denomination,
                        month_paid_upto = excluded.month_paid_upto,
                        next_installment_date = excluded.next_installment_date,
                        amount = excluded.amount,
                        month_paid_upto_num = excluded.month_paid_upto_num,
                        next_due_date_iso = excluded.next_due_date_iso,
                        total_deposit = excluded.total_deposit,
                        status = excluded.status,
                        first_seen = excluded.first_seen,
                        last_updated = excluded.last_updated,
                        closed_on = excluded.closed_on,
                        closed_reason = excluded.closed_reason,
                        source_update_time = excluded.source_update_time
                ''', (current_time, current_time))

                cursor.execute('''
                    DELETE FROM rd_accounts
                ''')
                removed_accounts = cursor.rowcount

                cursor.execute('''
                    DELETE FROM account_detail
                ''')
        
        # Log update history
        cursor.execute('''
            INSERT INTO update_history 
            (total_accounts, new_accounts, updated_accounts, removed_accounts, active_amount, due_within_30_days, status)
            VALUES (?, ?, ?, ?, ?, ?, ?)
        ''', (
            len(data_list),
            len(new_accounts),
            updated_accounts,
            removed_accounts,
            active_amount,
            due_within_30_days,
            'Success' if delete_missing else 'Success (preserve-missing)'
        ))
        
        conn.commit()
        conn.close()
        
        # Print summary
        print(f"\n📊 UPDATE SUMMARY:")
        print(f"   Total accounts processed: {len(data_list)}")
        print(f"   New accounts added: {len(new_accounts)}")
        print(f"   Existing accounts updated: {updated_accounts}")
        print(f"   Accounts deleted (missing from fetched data): {removed_accounts}")
        print(f"   Active amount total: ₹{active_amount:,.0f}")
        print(f"   Due within 30 days: {due_within_30_days}")
        
        if new_accounts:
            print(f"\n🆕 NEW ACCOUNTS:")
            for acc in new_accounts[:10]:
                print(f"   • {acc}")
            if len(new_accounts) > 10:
                print(f"   ... and {len(new_accounts) - 10} more")
        
        self.log_update(
            f"Database updated: {len(new_accounts)} new, {updated_accounts} updated, "
            f"{removed_accounts} removed, active amount {active_amount}, due soon {due_within_30_days}, "
            f"mode={'full-delete-missing' if delete_missing else 'preserve-missing'}"
        )
        
    def export_to_excel(self):
        """Export database to Excel"""
        self.print_section("EXPORTING TO EXCEL")
        
        conn = sqlite3.connect(self.db_file)
        
        # Read active accounts
        df = pd.read_sql_query('''
            SELECT account_no, account_name, denomination, month_paid_upto, 
                   next_installment_date, first_seen, last_updated
            FROM rd_accounts 
            WHERE is_active = 1
            ORDER BY account_no
        ''', conn)
        
        conn.close()
        
        # Save to Excel
        excel_file = str(self.exports_dir / f'RDAccounts_{datetime.now().strftime("%Y%m%d_%H%M%S")}.xlsx')
        df.to_excel(excel_file, index=False)
        
        print(f"✓ Exported {len(df)} records to: {excel_file}")
        self.log_update(f"Exported {len(df)} records to Excel")
        
    def run_update(self, mode='all'):
        """Main workflow"""
        try:
            self.setup_driver()
            
            if not self.login():
                print("❌ Login failed. Exiting...")
                return
            
            if not self.navigate_to_accounts():
                print("❌ Navigation failed. Exiting...")
                return

            full_data = self.fetch_accounts_via_next_accounts_popup(
                stop_when_found_accounts=None
            )
            if not full_data:
                print("❌ No data extracted via NEXT_ACCOUNTS + popup flow. Exiting...")
                return

            # Save full data to database
            self.save_to_database(full_data, delete_missing=True)
            self.import_reference_data_from_example_db()
            
            print("\n✅ Update completed successfully!")
            
        except Exception as e:
            print(f"\n❌ Error occurred: {str(e)}")
            import traceback
            traceback.print_exc()
            self.log_update(f"Error: {str(e)}")
            
        finally:
            if self.driver:
                print("\n🔒 Auto-closing browser...")
                time.sleep(1)  # Brief pause so user sees the success message
                try:
                    self.driver.quit()
                except Exception as ex:
                    print(f"⚠️  Browser close warning: {str(ex)}")
                finally:
                    self.driver = None
                    self.wait = None
                print("✓ Browser closed")
                print("\n" + "="*80)
            self.cleanup_temp_profiles()


def main():
    """Main entry point"""
    parser = argparse.ArgumentParser(description="DOP Agent account update tool")
    parser.add_argument(
        "--browser",
        type=str,
        default="chrome",
        help="Browser: chrome, edge, ie, safari"
    )
    args = parser.parse_args()

    print("\n" + "="*80)
    print(" DOP AGENT PORTAL AUTOMATION - CLIPBOARD EXTRACTION")
    print(" Automatically extracts ALL deposit accounts using Ctrl+A + Ctrl+C")
    print("="*80)
    
    # Check if pyperclip is installed
    try:
        import pyperclip
    except ImportError:
        print("\n❌ Missing required package: pyperclip")
        print("\nInstalling pyperclip...")
        import subprocess
        subprocess.check_call([sys.executable, "-m", "pip", "install", "pyperclip"])
        print("✓ pyperclip installed successfully!")
        print("\nPlease run the script again.")
        return
    
    agent = DOPAgentClipboard(browser=args.browser)
    
    # Main menu loop
    while True:
        # Check if credentials exist
        saved_id, saved_password = agent.get_credentials()
        
        print("\n" + "="*80)
        if saved_id and saved_password:
            print(f"✓ Auto-login ready for: {saved_id}")
            print("  (Password stored securely)")
        elif saved_id:
            print(f"✓ Agent ID saved: {saved_id}")
            print("  ⚠️  Password not stored (will ask once to save)")
        else:
            print("⚠️  No saved credentials found")
        print("="*80)
        
        print("\n📋 MENU:")
        print("1. Run full update (sync all fetched popup accounts + delete missing from DB)")
        print("2. Export current database to Excel")
        print("3. View database statistics")
        print("4. Change saved credentials")
        print("5. Exit")
        
        choice = input("\nEnter choice: ").strip()
        
        if choice == '1':
            agent.run_update(mode='all')
            break  # EXIT after extraction!
            
        elif choice == '2':
            agent.export_to_excel()
            input("\n⏸️  Press ENTER to return to menu...")
            
        elif choice == '3':
            conn = sqlite3.connect(agent.db_file)
            cursor = conn.cursor()
            cursor.execute('SELECT COUNT(*) FROM rd_accounts WHERE is_active = 1')
            total = cursor.fetchone()[0]
            cursor.execute('SELECT COUNT(*) FROM rd_accounts WHERE is_active = 0')
            inactive = cursor.fetchone()[0]
            cursor.execute('SELECT MAX(last_updated) FROM rd_accounts')
            last_update = cursor.fetchone()[0]
            cursor.execute('SELECT COALESCE(SUM(amount), 0) FROM rd_accounts WHERE is_active = 1')
            active_amount = cursor.fetchone()[0]
            cursor.execute('''
                SELECT due_within_30_days
                FROM update_history
                ORDER BY id DESC
                LIMIT 1
            ''')
            due_row = cursor.fetchone()
            due_soon = due_row[0] if due_row else 0
            conn.close()
            print(f"\n📊 DATABASE STATS:")
            print(f"   Active accounts: {total}")
            print(f"   Inactive accounts: {inactive}")
            print(f"   Active amount: ₹{active_amount:,.0f}")
            print(f"   Due within 30 days: {due_soon}")
            if last_update:
                print(f"   Last updated: {last_update}")
            input("\n⏸️  Press ENTER to return to menu...")
            
        elif choice == '4':
            # Change credentials
            print("\n🔄 Update Credentials")
            new_agent_id = input("Enter new Agent ID: ")
            new_password = getpass.getpass("Enter new Password: ")
            agent.save_credentials(new_agent_id, new_password)
            print("✓ Credentials updated!")
            input("\n⏸️  Press ENTER to return to menu...")
            
        elif choice == '5':
            print("\n" + "="*80)
            print("👋 Goodbye!")
            print(f"💾 Your data is saved in: {agent.db_file}")
            print("="*80 + "\n")
            break
            
        else:
            print("\n⚠️  Invalid choice. Please enter 1-5.")
            time.sleep(1)


if __name__ == "__main__":
    main()
