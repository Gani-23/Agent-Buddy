"""
India Post DOP Agent Portal - Report Generation Automation (OPTIMIZED)
Features:
- Process single or bulk account lists
- Handle installment numbers (_2, _3, _4, etc.)
- **BATCH PROCESSING**: Process all lists first, then download all reports!
- **AUTOMATICALLY CONVERT TO PDF** 
- **AUTOMATIC GRAYSCALE PRINTING - 2 COPIES**
- Save reference numbers for tracking
- Navigate pagination to select all accounts

OPTIMIZATION:
- Process all lists on same page (no navigation between lists)
- Collect all reference numbers
- Then navigate to Reports ONCE and download all reports in batch
- Much faster and more efficient!
"""

import time
import builtins
import pandas as pd
from selenium import webdriver
from selenium.webdriver.common.by import By
from selenium.webdriver.support.ui import WebDriverWait
from selenium.webdriver.support import expected_conditions as EC
from selenium.webdriver.chrome.options import Options
from selenium.webdriver.edge.options import Options as EdgeOptions
from selenium.webdriver.support.ui import Select
from selenium.webdriver.common.keys import Keys
from selenium.webdriver.common.action_chains import ActionChains
from datetime import datetime
import os
import sys
import logging
from pathlib import Path
import platform
import sqlite3
import argparse
import json
import re
import traceback
import shutil
import glob
import subprocess
from xml.sax.saxutils import escape
from dop_paths import resolve_base_dir

# PDF Creation imports
from reportlab.lib.pagesizes import A4
from reportlab.lib import colors
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch
from reportlab.platypus import SimpleDocTemplate, Table, TableStyle, Paragraph, Spacer
from reportlab.lib.enums import TA_CENTER, TA_LEFT, TA_RIGHT

try:
    import win32print
    import win32api
    WINDOWS_PRINTING = True
except ImportError:
    WINDOWS_PRINTING = False


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


class DOPAgentReports:
    def __init__(self, browser="chrome"):
        """Initialize the report generation automation"""
        self.driver = None
        self.wait = None
        self.os_type = platform.system()
        self.browser_name = self.normalize_browser_choice(browser)
        self.setup_paths()
        self.setup_logging()
        self.reference_numbers = []
        self.failed_lists = []
        
    def setup_paths(self):
        """Setup cross-platform file paths"""
        base_dir = resolve_base_dir()
        base_dir.mkdir(parents=True, exist_ok=True)

        # Categorized runtime folders
        self.logs_dir = base_dir / 'logs' / 'schedule'
        self.logs_dir.mkdir(parents=True, exist_ok=True)
        self.reports_root = base_dir / 'Reports'
        self.reports_root.mkdir(parents=True, exist_ok=True)
        self.reports_dir = self.reports_root / 'pdf'
        self.reports_dir.mkdir(parents=True, exist_ok=True)
        self.references_dir = self.reports_root / 'references'
        self.references_dir.mkdir(parents=True, exist_ok=True)
        self.download_dir = self.reports_root / 'downloads'
        self.download_dir.mkdir(parents=True, exist_ok=True)

        self.db_file = str(base_dir / 'dop_agent.db')
        self.log_file = str(self.logs_dir / f"dop_reports_{datetime.now().strftime('%Y%m%d')}.log")
        self.reference_file = str(self.references_dir / 'payment_references.txt')
        self.base_dir = base_dir
        
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
        
    def log_message(self, message):
        """Log messages to file and console"""
        logging.info(message)
        
    def print_section(self, title):
        """Print formatted section headers"""
        print("\n" + "="*80)
        print(f" {title}")
        print("="*80)
    
    def get_credentials(self):
        """Get credentials from database"""
        try:
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
                    import base64
                    try:
                        password = base64.b64decode(encrypted_password.encode()).decode()
                        return agent_id, password
                    except:
                        return None, None
                else:
                    return agent_id, None
            
            return None, None
                
        except Exception as e:
            print(f"❌ Error reading credentials: {str(e)}")
            return None, None
            
    def setup_driver(self, browser_name=None):
        """Setup Selenium driver with download preferences."""
        self.print_section("STEP 1: SETTING UP BROWSER")

        if browser_name is not None:
            self.browser_name = self.normalize_browser_choice(browser_name)
        selected_browser = self.browser_name

        # Set download directory
        prefs = {
            "download.default_directory": str(self.download_dir),
            "download.prompt_for_download": False,
            "download.directory_upgrade": True,
            "safebrowsing.enabled": True
        }

        try:
            if selected_browser == "edge":
                edge_options = EdgeOptions()
                edge_options.add_argument("--start-maximized")
                edge_options.add_argument("--disable-blink-features=AutomationControlled")
                edge_options.add_experimental_option("excludeSwitches", ["enable-automation"])
                edge_options.add_experimental_option("useAutomationExtension", False)
                edge_options.add_experimental_option("prefs", prefs)
                self.driver = webdriver.Edge(options=edge_options)
            elif selected_browser == "safari":
                # Safari does not support Chrome-style download preferences.
                self.driver = webdriver.Safari()
                self.driver.maximize_window()
            elif selected_browser == "ie":
                self.driver = webdriver.Ie()
                self.driver.maximize_window()
            else:
                chrome_options = Options()
                chrome_options.add_argument('--start-maximized')
                chrome_options.add_argument('--disable-blink-features=AutomationControlled')
                chrome_options.add_experimental_option("excludeSwitches", ["enable-automation"])
                chrome_options.add_experimental_option('useAutomationExtension', False)
                chrome_options.add_experimental_option("prefs", prefs)
                self.driver = webdriver.Chrome(options=chrome_options)
        except Exception as ex:
            if selected_browser != "chrome":
                print(f"⚠️  Could not start {selected_browser}. Falling back to Chrome. ({str(ex)})")
                self.browser_name = "chrome"
                chrome_options = Options()
                chrome_options.add_argument('--start-maximized')
                chrome_options.add_argument('--disable-blink-features=AutomationControlled')
                chrome_options.add_experimental_option("excludeSwitches", ["enable-automation"])
                chrome_options.add_experimental_option('useAutomationExtension', False)
                chrome_options.add_experimental_option("prefs", prefs)
                self.driver = webdriver.Chrome(options=chrome_options)
            else:
                raise

        self.wait = WebDriverWait(self.driver, 20)

        print(f"✓ Browser started successfully ({self.browser_name})")
        self.log_message(f"Browser initialized ({self.browser_name})")

    def close_browser(self):
        """Close browser safely and release driver handle."""
        if not self.driver:
            return
        print("\n🔒 Closing browser...")
        time.sleep(1)
        try:
            self.driver.quit()
        except Exception as ex:
            print(f"⚠️  Browser close warning: {str(ex)}")
        finally:
            self.driver = None
            self.wait = None
        
    def login(self, agent_id=None, password=None):
        """Login to DOP Agent portal"""
        self.print_section("STEP 2: LOGGING IN")
        
        if agent_id is None or password is None:
            saved_id, saved_password = self.get_credentials()
            
            if saved_id and saved_password:
                print(f"✓ Auto-loading credentials for: {saved_id}")
                agent_id = saved_id
                password = saved_password
            else:
                print("❌ No saved credentials found")
                return False
        
        print("\n🌐 Opening DOP Agent Portal...")
        self.driver.get("https://dopagent.indiapost.gov.in")
        time.sleep(2)
        
        print("📝 Filling login form...")
        
        agent_field = self.wait.until(
            EC.presence_of_element_located((By.NAME, "AuthenticationFG.USER_PRINCIPAL"))
        )
        agent_field.clear()
        agent_field.send_keys(agent_id)
        
        password_field = self.driver.find_element(By.NAME, "AuthenticationFG.ACCESS_CODE")
        password_field.clear()
        password_field.send_keys(password)
        
        print("\n🔍 Waiting for CAPTCHA...")
        captcha_field = self.driver.find_element(By.NAME, "AuthenticationFG.VERIFICATION_CODE")
        
        print("👀 Please solve the CAPTCHA in the browser window")
        print("⏳ Waiting for you to enter CAPTCHA (checking every 0.5 seconds)...")
        
        while True:
            captcha_value = captcha_field.get_attribute('value')
            if captcha_value and len(captcha_value) >= 6:
                print(f"✓ CAPTCHA detected: {captcha_value}")
                break
            time.sleep(0.5)
        
        print("\n🔐 Logging in...")
        login_button = self.driver.find_element(By.NAME, "Action.VALIDATE_RM_PLUS_CREDENTIALS_CATCHA_DISABLED")
        login_button.click()
        
        time.sleep(3)
        
        if "Agent Enquire" in self.driver.page_source or "Accounts" in self.driver.page_source:
            print("✓ Login successful!")
            self.log_message(f"Login successful for Agent ID: {agent_id}")
            return True
        else:
            print("❌ Login failed!")
            return False
            
    def navigate_to_agent_screen(self):
        """Navigate to Agent Enquire & Update Screen"""
        self.print_section("STEP 3: NAVIGATING TO ACCOUNTS")
        
        print("🔍 Looking for Accounts menu...")
        time.sleep(2)
        
        try:
            accounts_link = self.wait.until(
                EC.element_to_be_clickable((By.LINK_TEXT, "Accounts"))
            )
            accounts_link.click()
            print("✓ Clicked 'Accounts' menu")
            time.sleep(1)
            
            enquire_link = self.wait.until(
                EC.element_to_be_clickable((By.LINK_TEXT, "Agent Enquire & Update Screen"))
            )
            enquire_link.click()
            print("✓ Opened 'Agent Enquire & Update Screen'")
            time.sleep(2)
            
            self.log_message("Navigated to Agent Enquire & Update Screen")
            return True
            
        except Exception as e:
            print(f"❌ Navigation failed: {str(e)}")
            return False

    def navigate_to_update_aslaas_screen(self):
        """Navigate to Accounts -> Update ASLAAS Number screen."""
        self.print_section("STEP 3A: NAVIGATING TO UPDATE ASLAAS NUMBER")

        try:
            try:
                accounts_link = self.wait.until(
                    EC.element_to_be_clickable((By.LINK_TEXT, "Accounts"))
                )
                accounts_link.click()
                time.sleep(1)
            except Exception:
                # In some flows we are already inside Accounts.
                pass

            selectors = [
                (By.NAME, "HREF_Update ASLAAS Number"),
                (By.ID, "HREF_Update ASLAAS Number"),
                (By.LINK_TEXT, "Update ASLAAS Number"),
                (By.PARTIAL_LINK_TEXT, "Update ASLAAS"),
            ]

            for by, value in selectors:
                elements = self.driver.find_elements(by, value)
                for element in elements:
                    if not element.is_displayed():
                        continue
                    try:
                        element.click()
                    except Exception:
                        self.driver.execute_script("arguments[0].click();", element)
                    time.sleep(2)
                    self.log_message("Navigated to Update ASLAAS Number screen")
                    return True

            print("❌ Could not find 'Update ASLAAS Number' link")
            return False

        except Exception as e:
            print(f"❌ Failed to open Update ASLAAS Number: {str(e)}")
            return False

    def navigate_to_aslaas_from_error_link(self):
        """Use in-page ASLAAS error link from red alert (no menu navigation)."""
        selectors = [
            (By.NAME, "HREF_ASLAAS Number Report"),
            (By.ID, "HREF_ASLAAS Number Report"),
            (By.ID, "errorlink1"),
            (
                By.XPATH,
                "//div[@role='alert' and contains(@class, 'redbg')]//a[contains(@id, 'errorlink')]",
            ),
            (
                By.XPATH,
                "//div[@role='alert' and contains(@class, 'redbg')]//a[contains(@name, 'HREF_ASLAAS')]",
            ),
            (By.PARTIAL_LINK_TEXT, "ASLAAS Number"),
            (By.PARTIAL_LINK_TEXT, "Update ASLAAS"),
        ]

        for by, value in selectors:
            try:
                elements = self.driver.find_elements(by, value)
            except Exception:
                continue

            for element in elements:
                try:
                    self.driver.execute_script("arguments[0].click();", element)
                    time.sleep(1.5)
                    self.wait.until(
                        EC.presence_of_element_located((By.NAME, "CustomAgentAslaasNoFG.RD_ACC_NO"))
                    )
                    self.wait.until(
                        EC.presence_of_element_located((By.NAME, "CustomAgentAslaasNoFG.ASLAAS_NO"))
                    )
                    print("✓ Opened Update ASLAAS form using in-page error link")
                    self.log_message("Opened Update ASLAAS form from red alert link")
                    return True
                except Exception:
                    continue

        return False

    def clear_accounts_for_next_list(self):
        """Clear account search on the same Agent Enquire screen for the next list."""
        try:
            clear_button = self.wait.until(
                EC.element_to_be_clickable((By.NAME, "Action.CLEAR_ACCOUNTS"))
            )
            clear_button.click()
            time.sleep(1)

            try:
                search_field = self.driver.find_element(
                    By.NAME, "CustomAgentRDAccountFG.ACCOUNT_NUMBER_FOR_SEARCH"
                )
                if search_field.get_attribute("value"):
                    search_field.clear()
            except Exception:
                pass

            self.log_message("Cleared account search for next list")
            return True
        except Exception as e:
            print(f"   ⚠️  Could not clear accounts in-place: {str(e)}")
            self.log_message(f"Clear accounts in-place failed: {str(e)}")
            return False

    def ensure_aslaas_form_open(self):
        """Ensure Update ASLAAS Number form is currently open."""
        try:
            self.driver.find_element(By.NAME, "CustomAgentAslaasNoFG.RD_ACC_NO")
            self.driver.find_element(By.NAME, "CustomAgentAslaasNoFG.ASLAAS_NO")
            return True
        except Exception:
            if self.navigate_to_aslaas_from_error_link():
                return True
            return self.navigate_to_update_aslaas_screen()

    def wait_for_aslaas_form_ready(self, timeout_seconds=10):
        """Wait until ASLAAS entry form fields are present."""
        try:
            WebDriverWait(self.driver, timeout_seconds).until(
                EC.presence_of_element_located((By.NAME, "CustomAgentAslaasNoFG.RD_ACC_NO"))
            )
            WebDriverWait(self.driver, timeout_seconds).until(
                EC.presence_of_element_located((By.NAME, "CustomAgentAslaasNoFG.ASLAAS_NO"))
            )
            return True
        except Exception:
            return False

    def click_first_clickable(self, selectors, timeout_seconds=10):
        """Click first clickable selector from a list of (By, value)."""
        for by, value in selectors:
            try:
                element = WebDriverWait(self.driver, timeout_seconds).until(
                    EC.element_to_be_clickable((by, value))
                )
                try:
                    element.click()
                except Exception:
                    self.driver.execute_script("arguments[0].click();", element)
                return True
            except Exception:
                continue
        return False

    def filter_aslaas_updates_for_open_accounts(self, updates):
        """Skip ASLAAS updates for accounts already closed/matured."""
        valid = updates or []
        if not valid:
            return [], []

        accounts = [str(item.get("account_no", "")).strip() for item in valid]
        accounts = [a for a in accounts if a]
        if not accounts:
            return [], []

        allowed_accounts = set()
        try:
            conn = sqlite3.connect(self.db_file)
            cursor = conn.cursor()

            # Build map for quick case-insensitive comparison.
            index_map = {a: a for a in accounts}
            placeholders = ",".join("?" for _ in index_map)

            cursor.execute(
                f"""
                SELECT trim(COALESCE(account_no, ''))
                FROM rd_accounts
                WHERE trim(COALESCE(account_no, '')) IN ({placeholders})
                  AND COALESCE(is_active, 0) = 1
                  AND lower(trim(COALESCE(status, ''))) NOT IN ('closed', 'matured', 'matured/closed', 'closed/matured')
                """,
                list(index_map.keys())
            )
            for row in cursor.fetchall():
                acct = str(row[0]).strip()
                if acct:
                    allowed_accounts.add(acct)

            # Remove anything explicitly in closed_accounts table.
            try:
                cursor.execute(
                    f"""
                    SELECT trim(COALESCE(account_no, ''))
                    FROM closed_accounts
                    WHERE trim(COALESCE(account_no, '')) IN ({placeholders})
                    """,
                    list(index_map.keys())
                )
                for row in cursor.fetchall():
                    acct = str(row[0]).strip()
                    if acct and acct in allowed_accounts:
                        allowed_accounts.remove(acct)
            except Exception:
                # closed_accounts might not exist in very old DBs; ignore safely.
                pass

            conn.close()
        except Exception as ex:
            print(f"⚠️  Could not verify closed accounts before ASLAAS update: {str(ex)}")
            # Fail-safe: if we cannot verify account state, do not update any account.
            return [], accounts

        filtered = []
        skipped = []
        for item in valid:
            acct = str(item.get("account_no", "")).strip()
            if acct in allowed_accounts:
                filtered.append(item)
            else:
                skipped.append(acct)

        return filtered, skipped

    def apply_aslaas_updates(self, updates):
        """Apply ASLAAS updates before list processing."""
        valid_updates = []
        for item in updates or []:
            account_no = str(item.get("account_no", "")).strip()
            aslaas_no = str(item.get("aslaas_no", "")).strip() or "APPLIED"
            if not account_no:
                continue
            valid_updates.append({
                "account_no": account_no,
                "aslaas_no": aslaas_no
            })

        if not valid_updates:
            return True

        valid_updates, skipped_accounts = self.filter_aslaas_updates_for_open_accounts(valid_updates)
        if skipped_accounts:
            preview = ", ".join(skipped_accounts[:10])
            suffix = " ..." if len(skipped_accounts) > 10 else ""
            print(
                "⚠️  Skipping closed/matured accounts for ASLAAS update: "
                f"{preview}{suffix}"
            )
            self.log_message(
                "Skipped closed/matured ASLAAS updates: "
                + ", ".join(skipped_accounts)
            )
        if not valid_updates:
            print("⚠️  No eligible open accounts to update ASLAAS.")
            return True

        self.print_section("STEP 3B: APPLYING ASLAAS UPDATES")
        print(f"→ Updating ASLAAS for {len(valid_updates)} account(s) before list payment")

        if not self.ensure_aslaas_form_open():
            print("❌ Could not open ASLAAS form")
            return False
        if not self.wait_for_aslaas_form_ready(timeout_seconds=12):
            print("❌ ASLAAS form did not become ready")
            return False

        failures = 0
        for idx, item in enumerate(valid_updates, 1):
            account_no = item["account_no"]
            aslaas_no = item["aslaas_no"]

            print(f"→ [{idx}/{len(valid_updates)}] {account_no} -> {aslaas_no}")

            if not self.wait_for_aslaas_form_ready(timeout_seconds=8):
                if not self.ensure_aslaas_form_open() or not self.wait_for_aslaas_form_ready(timeout_seconds=10):
                    print(f"   ❌ Unable to open ASLAAS form for {account_no}")
                    failures += 1
                    continue

            try:
                account_field = self.wait.until(
                    EC.presence_of_element_located((By.NAME, "CustomAgentAslaasNoFG.RD_ACC_NO"))
                )
                aslaas_field = self.driver.find_element(By.NAME, "CustomAgentAslaasNoFG.ASLAAS_NO")

                account_field.clear()
                account_field.send_keys(account_no)
                aslaas_field.clear()
                aslaas_field.send_keys(aslaas_no)

                continue_selectors = [
                    (By.ID, "LOAD_CONFIRM_PAGE"),
                    (By.NAME, "Action.LOAD_CONFIRM_PAGE"),
                    (By.ID, "Action.LOAD_CONFIRM_PAGE"),
                    (By.XPATH, "//input[contains(@name,'LOAD_CONFIRM_PAGE')]"),
                    (By.XPATH, "//button[contains(@id,'LOAD_CONFIRM_PAGE')]")
                ]
                if not self.click_first_clickable(continue_selectors, timeout_seconds=10):
                    raise RuntimeError("Continue button (LOAD_CONFIRM_PAGE) not found")

                submit_selectors = [
                    (By.ID, "ADD_FIELD_SUBMIT"),
                    (By.NAME, "Action.ADD_FIELD_SUBMIT"),
                    (By.ID, "Action.ADD_FIELD_SUBMIT"),
                    (By.XPATH, "//input[contains(@name,'ADD_FIELD_SUBMIT')]"),
                    (By.XPATH, "//button[contains(@id,'ADD_FIELD_SUBMIT')]")
                ]
                if not self.click_first_clickable(submit_selectors, timeout_seconds=12):
                    raise RuntimeError("Submit button (ADD_FIELD_SUBMIT) not found")

                if not self.wait_for_aslaas_form_ready(timeout_seconds=15):
                    raise RuntimeError("ASLAAS form did not reload after submit")
                time.sleep(0.8)

                try:
                    alert = self.driver.find_element(
                        By.XPATH,
                        "//div[@role='alert' and contains(@class, 'redbg')]"
                    )
                    alert_text = alert.text.strip()
                    if alert_text:
                        print(f"   ⚠️  Portal alert: {alert_text}")
                except Exception:
                    pass

                self.log_message(f"ASLAAS update submitted: {account_no} -> {aslaas_no}")
            except Exception as ex:
                failures += 1
                print(f"   ❌ Failed for {account_no}: {str(ex)}")
                self.log_message(f"ASLAAS update failed for {account_no}: {str(ex)}")

        if failures:
            print(f"⚠️  ASLAAS update phase completed with {failures} failure(s)")
        else:
            print("✓ ASLAAS update phase completed successfully")

        return failures == 0

    def get_latest_red_alert_text(self):
        """Read the most recent red portal alert text, if present."""
        try:
            alert_boxes = self.driver.find_elements(
                By.XPATH,
                "//div[@role='alert' and contains(@class, 'redbg')]"
            )
            for alert in alert_boxes:
                text = (alert.text or "").strip()
                if text:
                    return text
        except Exception:
            pass
        return None

    def extract_aslaas_accounts_from_alert(self, alert_text):
        """Extract RD account numbers from ASLAAS error alert text."""
        if not alert_text:
            return []

        accounts = []
        seen = set()

        # Typical format:
        # "ASLAAS Number is not updated for RD Account number 020200168719..."
        for account_no in re.findall(
            r"RD\s*Account\s*number\s*[:\-]?\s*([0-9]{8,20})",
            alert_text,
            flags=re.IGNORECASE
        ):
            if account_no not in seen:
                seen.add(account_no)
                accounts.append(account_no)

        # Collect all account-like numbers when ASLAAS is mentioned.
        # This also covers messages that list multiple accounts after a single prefix
        # (e.g. "RD Account number 123..., 456...").
        if "aslaas" in alert_text.lower():
            for account_no in re.findall(r"\b[0-9]{8,20}\b", alert_text):
                if account_no not in seen:
                    seen.add(account_no)
                    accounts.append(account_no)

        return accounts
            
    def parse_account_list(self, account_string):
        """Parse account list and extract installment numbers"""
        accounts = []
        clean_string = account_string.strip().replace('[', '').replace(']', '').strip()
        account_items = [item.strip() for item in clean_string.split(',') if item.strip()]
        
        for item in account_items:
            if '_' in item:
                parts = item.split('_')
                account_no = parts[0].strip()
                installments = int(parts[1].strip()) if len(parts) > 1 else 1
            else:
                account_no = item.strip()
                installments = 1
            
            accounts.append((account_no, installments))
        
        return accounts

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

    @staticmethod
    def normalize_pay_mode(raw_mode):
        """Normalize pay mode CLI value to one of: cash, dop_cheque, non_dop_cheque."""
        mode = (raw_mode or "cash").strip().lower().replace("-", " ").replace("_", " ")
        mode = " ".join(mode.split())
        if mode in {"dop cheque", "dop", "dop check"}:
            return "dop_cheque"
        if mode in {"non dop cheque", "non dop", "nondop cheque", "non dop check"}:
            return "non_dop_cheque"
        return "cash"

    def select_payment_mode(self, pay_mode):
        """Select Cash / DOP Cheque / Non DOP Cheque radio mode."""
        normalized_mode = self.normalize_pay_mode(pay_mode)

        radio_buttons = self.wait.until(
            lambda d: d.find_elements(By.NAME, "CustomAgentRDAccountFG.PAY_MODE_SELECTED_FOR_TRN")
        )
        if not radio_buttons:
            raise RuntimeError("Pay mode radio buttons not found")

        desired_by_value = {
            "cash": {"cash", "c", "0"},
            "dop_cheque": {"dop", "d", "1"},
            "non_dop_cheque": {"non", "n", "2"}
        }

        selected_button = None
        for button in radio_buttons:
            value = (button.get_attribute("value") or "").strip().lower()
            if value in desired_by_value.get(normalized_mode, set()):
                selected_button = button
                break

        if selected_button is None and len(radio_buttons) >= 3:
            index_map = {"cash": 0, "dop_cheque": 1, "non_dop_cheque": 2}
            selected_button = radio_buttons[index_map.get(normalized_mode, 0)]

        if selected_button is None:
            selected_button = radio_buttons[0]

        self.driver.execute_script("arguments[0].click();", selected_button)
        mode_label = {
            "cash": "Cash",
            "dop_cheque": "DOP Cheque",
            "non_dop_cheque": "Non DOP Cheque"
        }.get(normalized_mode, "Cash")
        print(f"✓ Pay mode selected: {mode_label}")
        
    def process_account_list(
        self,
        account_list_string,
        list_index=1,
        pay_mode="cash",
        dop_cheque_details=None,
        aslaas_auto_retry_count=0
    ):
        """Process a single account list - STAY ON SAME PAGE!
        
        Args:
            account_list_string: String containing account numbers
            list_index: Index of the list being processed
            aslaas_auto_retry_count: Internal guard to avoid infinite ASLAAS retries
            
        Returns:
            reference_number: Payment reference number or None if failed
        """
        self.print_section(f"PROCESSING LIST #{list_index}")
        
        try:
            if list_index > 1:
                print("→ Preparing same page for next list")
                if not self.clear_accounts_for_next_list():
                    print("   → Falling back to reopen Agent Enquire screen")
                    if not self.navigate_to_agent_screen():
                        print("❌ Could not prepare Agent Enquire screen")
                        return None

            parsed_accounts = self.parse_account_list(account_list_string)
            print(f"→ Parsed {len(parsed_accounts)} accounts")
            normalized_mode = self.normalize_pay_mode(pay_mode)
            print(f"→ Payment mode for this list: {normalized_mode}")
            
            accounts_by_installment = {}
            for account_no, installments in parsed_accounts:
                if installments not in accounts_by_installment:
                    accounts_by_installment[installments] = []
                accounts_by_installment[installments].append(account_no)
            
            for installments, accounts in accounts_by_installment.items():
                print(f"   • {len(accounts)} accounts with {installments} installment(s)")
            
            print("\n→ Selecting pay mode")
            self.select_payment_mode(pay_mode)
            time.sleep(0.5)
            
            all_account_numbers = [acc for acc, _ in parsed_accounts]
            account_string = ','.join(all_account_numbers)
            
            print(f"→ Entering {len(all_account_numbers)} account numbers")
            search_field = self.driver.find_element(By.NAME, "CustomAgentRDAccountFG.ACCOUNT_NUMBER_FOR_SEARCH")
            search_field.clear()
            search_field.send_keys(account_string)
            time.sleep(0.5)
            
            print("→ Fetching accounts")
            fetch_button = self.driver.find_element(By.NAME, "Action.FETCH_INPUT_ACCOUNT")
            fetch_button.click()
            time.sleep(3)
            
            print("→ Selecting all accounts (handling pagination)")
            total_selected = self.select_all_accounts_with_pagination(len(all_account_numbers))
            print(f"✓ Selected {total_selected} accounts")
            
            print("→ Saving selected accounts")
            save_button = self.driver.find_element(By.NAME, "Action.SAVE_ACCOUNTS")
            save_button.click()
            time.sleep(3)
            
            print("→ Now on saved installments page")
            
            needs_installments = any(inst > 1 for _, inst in parsed_accounts)

            needs_cheque_account_details = normalized_mode in {"dop_cheque", "non_dop_cheque"}
            if needs_cheque_account_details:
                detail_count = len(dop_cheque_details) if isinstance(dop_cheque_details, dict) else 0
                print(f"→ Received cheque/account details for {detail_count} account(s)")

            if needs_installments or needs_cheque_account_details:
                print("→ Some accounts need multiple installments")
                print("→ Setting installment numbers and adding to list")
                self.process_installments(
                    parsed_accounts,
                    pay_mode=normalized_mode,
                    dop_cheque_details=dop_cheque_details
                )
            else:
                print("→ All accounts have single installment (default)")
            
            print("→ Paying all installments")
            pay_button = self.wait.until(
                EC.element_to_be_clickable((By.NAME, "Action.PAY_ALL_SAVED_INSTALLMENTS"))
            )
            pay_button.click()
            time.sleep(3)

            red_alert_text = self.get_latest_red_alert_text()
            if red_alert_text:
                print(f"⚠️  Portal alert after pay-all: {red_alert_text}")
                self.log_message(f"Portal red alert after pay-all (list {list_index}): {red_alert_text}")

                aslaas_accounts = self.extract_aslaas_accounts_from_alert(red_alert_text)
                if aslaas_accounts:
                    print(
                        "→ Detected missing ASLAAS for account(s): "
                        + ", ".join(aslaas_accounts)
                    )
                    if aslaas_auto_retry_count >= 1:
                        print("❌ ASLAAS auto-recovery already attempted once; stopping retry loop")
                        return None

                    auto_updates = [
                        {"account_no": account_no, "aslaas_no": "APPLIED"}
                        for account_no in aslaas_accounts
                    ]
                    print("→ Auto-updating ASLAAS and retrying this same list once")
                    if not self.apply_aslaas_updates(auto_updates):
                        print("❌ Auto ASLAAS update failed; cannot continue this list")
                        return None

                    print("→ Returning to Agent Enquire screen after ASLAAS update")
                    if not self.navigate_to_agent_screen():
                        print("❌ Could not return to Agent Enquire screen after ASLAAS update")
                        return None

                    print("→ Retrying current list after ASLAAS auto-update")
                    return self.process_account_list(
                        account_list_string,
                        list_index=list_index,
                        pay_mode=normalized_mode,
                        dop_cheque_details=dop_cheque_details,
                        aslaas_auto_retry_count=aslaas_auto_retry_count + 1
                    )
            
            reference_number = self.extract_reference_number()
            
            if reference_number:
                print(f"✓ Payment successful! Reference: {reference_number}")
                self.save_reference_number(reference_number, list_index, account_list_string)
                
                print("→ Clearing account search for next list (stay on same page)")
                self.clear_accounts_for_next_list()
                
                return reference_number
            else:
                print("❌ Failed to extract reference number")
                return None
                
        except Exception as e:
            print(f"❌ Error processing list: {str(e)}")
            traceback.print_exc()
            self.log_message(f"Error processing list {list_index}: {str(e)}")
            return None
            
    def select_all_accounts_with_pagination(self, expected_count):
        """Select all accounts, handling pagination"""
        total_selected = 0
        page = 1
        
        while True:
            try:
                checkboxes = self.driver.find_elements(
                    By.XPATH, 
                    "//input[@type='checkbox' and contains(@name, 'CustomAgentRDAccountFG.SELECT_INDEX_ARRAY')]"
                )
                
                if not checkboxes:
                    break
                
                print(f"   Page {page}: Found {len(checkboxes)} checkboxes")
                
                for checkbox in checkboxes:
                    try:
                        if not checkbox.is_selected():
                            checkbox.click()
                            total_selected += 1
                            time.sleep(0.1)
                    except:
                        pass
                
                try:
                    next_button = self.driver.find_element(By.NAME, "Action.AgentRDActSummaryAllListing.GOTO_NEXT__")
                    if next_button.is_enabled():
                        print(f"   → Moving to next page")
                        next_button.click()
                        time.sleep(2)
                        page += 1
                    else:
                        break
                except:
                    break
                    
            except Exception as e:
                print(f"   ⚠️  Pagination warning: {str(e)}")
                break
        
        return total_selected
        
    def process_installments(self, parsed_accounts, pay_mode="cash", dop_cheque_details=None):
        """Process installments and DOP cheque details on saved installments page."""
        normalized_mode = self.normalize_pay_mode(pay_mode)
        is_dop_cheque_mode = normalized_mode == "dop_cheque"
        is_non_dop_cheque_mode = normalized_mode == "non_dop_cheque"
        account_cheque_map = {}

        if (is_dop_cheque_mode or is_non_dop_cheque_mode) and isinstance(dop_cheque_details, dict):
            for account_no_key, details in dop_cheque_details.items():
                if not isinstance(details, dict):
                    continue
                cheque_no = str(details.get("cheque_no", "")).strip()
                payment_account_no = str(details.get("payment_account_no", "")).strip()
                account_no = str(account_no_key or "").strip()
                if not account_no or not payment_account_no:
                    continue
                account_cheque_map[account_no] = {
                    "cheque_no": cheque_no,
                    "payment_account_no": payment_account_no
                }

        if (is_dop_cheque_mode or is_non_dop_cheque_mode) and not account_cheque_map:
            raise RuntimeError("Cheque/payment account details are missing for this list")

        for account_no, installments in parsed_accounts:
            if installments <= 1 and not is_dop_cheque_mode and not is_non_dop_cheque_mode:
                continue

            try:
                print(f"   → Setting {installments} installments for {account_no}")

                account_elements = self.driver.find_elements(
                    By.XPATH,
                    "//span[contains(@id, 'HREF_CustomAgentRDAccountFG.ACCOUNT_NUMBER_ARRAY')]"
                )

                account_index = None
                for idx, elem in enumerate(account_elements):
                    if elem.text.strip() == account_no:
                        account_index = idx
                        break

                if account_index is None:
                    print(f"   ⚠️  Could not find account {account_no} in the list")
                    continue

                radio_button = self.driver.find_element(
                    By.XPATH,
                    f"//input[@type='radio' and @name='CustomAgentRDAccountFG.SELECTED_INDEX' and @value='{account_index}']"
                )
                radio_button.click()
                time.sleep(0.5)

                installment_field = self.driver.find_element(
                    By.NAME,
                    "CustomAgentRDAccountFG.RD_INSTALLMENT_NO"
                )
                installment_field.clear()
                installment_field.send_keys(str(max(1, int(installments))))
                time.sleep(0.3)

                if is_dop_cheque_mode:
                    account_details = account_cheque_map.get(account_no, {})
                    cheque_no = str(account_details.get("cheque_no", "")).strip()
                    payment_account_no = str(account_details.get("payment_account_no", "")).strip()
                    if not cheque_no or not payment_account_no:
                        raise RuntimeError(f"DOP cheque details missing for account {account_no}")

                    cheque_field = self.driver.find_element(
                        By.NAME,
                        "CustomAgentRDAccountFG.RD_CHEQUE_NO"
                    )
                    cheque_field.clear()
                    cheque_field.send_keys(cheque_no)
                    time.sleep(0.2)

                    payment_account_field = self.driver.find_element(
                        By.NAME,
                        "CustomAgentRDAccountFG.RD_ACCOUNT_NUMBER_FOR_PAYMENT"
                    )
                    payment_account_field.clear()
                    payment_account_field.send_keys(payment_account_no)
                    time.sleep(0.2)
                elif is_non_dop_cheque_mode:
                    account_details = account_cheque_map.get(account_no, {})
                    payment_account_no = str(account_details.get("payment_account_no", "")).strip()
                    if not payment_account_no:
                        raise RuntimeError(f"Payment account number missing for account {account_no}")

                    payment_account_field = self.driver.find_element(
                        By.NAME,
                        "CustomAgentRDAccountFG.RD_ACCOUNT_NUMBER_FOR_PAYMENT"
                    )
                    payment_account_field.clear()
                    payment_account_field.send_keys(payment_account_no)
                    time.sleep(0.2)

                add_button = self.driver.find_element(By.NAME, "Action.ADD_TO_LIST")
                add_button.click()
                time.sleep(1)

                print(f"   ✓ Set {installments} installments for {account_no}")

            except Exception as e:
                print(f"   ⚠️  Warning: Could not set installments for {account_no}: {str(e)}")
                traceback.print_exc()
                    
    def extract_reference_number(self):
        """Extract payment reference number from success message"""
        try:
            success_div = self.driver.find_element(
                By.XPATH, 
                "//div[@role='alert' and contains(@class, 'greenbg')]"
            )
            
            message_text = success_div.text
            match = re.search(r'reference number is ([A-Z0-9]+)', message_text, re.IGNORECASE)
            
            if match:
                reference_number = match.group(1)
                return reference_number
            else:
                print(f"⚠️  Could not parse reference number from: {message_text}")
                return None
                
        except Exception as e:
            print(f"❌ Error extracting reference number: {str(e)}")
            return None
            
    def save_reference_number(self, reference_number, list_index, account_list):
        """Save reference number to file"""
        try:
            timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
            
            with open(self.reference_file, 'a', encoding='utf-8') as f:
                f.write(f"\n{'='*80}\n")
                f.write(f"Timestamp: {timestamp}\n")
                f.write(f"List #: {list_index}\n")
                f.write(f"Reference Number: {reference_number}\n")
                f.write(f"Accounts: {account_list}\n")
                f.write(f"{'='*80}\n")
            
            self.reference_numbers.append({
                'timestamp': timestamp,
                'list_index': list_index,
                'reference_number': reference_number,
                'accounts': account_list
            })
            
            print(f"✓ Reference number saved to: {self.reference_file}")
            
        except Exception as e:
            print(f"⚠️  Warning: Could not save reference number: {str(e)}")
    
    # ==================== BATCH REPORT GENERATION ====================
    
    def generate_all_reports(self, reference_numbers_list):
        """Generate reports for all reference numbers in batch
        
        Args:
            reference_numbers_list: List of reference numbers to generate reports for
            
        Returns:
            List of downloaded file paths
        """
        self.print_section(f"BATCH REPORT GENERATION - {len(reference_numbers_list)} REPORTS")
        
        downloaded_files = []
        
        # Navigate to Reports page ONCE
        print("→ Navigating to Reports page...")
        try:
            reports_link = self.wait.until(
                EC.element_to_be_clickable((By.NAME, "HREF_Reports"))
            )
        except:
            reports_link = self.wait.until(
                EC.element_to_be_clickable((By.LINK_TEXT, "Reports"))
            )
        reports_link.click()
        time.sleep(2)
        print("✓ On Reports page")
        
        # Generate each report
        for idx, ref_num in enumerate(reference_numbers_list, 1):
            print(f"\n[{idx}/{len(reference_numbers_list)}] Generating report for: {ref_num}")
            
            try:
                excel_file = self.generate_single_report(ref_num)
                
                if excel_file:
                    downloaded_files.append({
                        'reference': ref_num,
                        'file': excel_file
                    })
                    print(f"✓ Downloaded: {os.path.basename(excel_file)}")
                else:
                    print(f"❌ Failed to download report for {ref_num}")
                
                # Add delay between reports to avoid timing issues
                if idx < len(reference_numbers_list):
                    print("   → Waiting for page to reset...")
                    time.sleep(3)
                    
            except Exception as e:
                print(f"❌ Error generating report for {ref_num}: {str(e)}")
                traceback.print_exc()
        
        return downloaded_files
    
    def generate_single_report(self, reference_number):
        """Generate single report (called from batch process)
        
        Args:
            reference_number: The payment reference number
            
        Returns:
            Downloaded file path or None
        """
        try:
            # Wait for any blocking overlays to disappear
            try:
                WebDriverWait(self.driver, 10).until(
                    EC.invisibility_of_element_located((By.CLASS_NAME, "blockUI"))
                )
            except:
                pass  # No overlay present
            
            time.sleep(1)  # Extra safety buffer
            
            # CRITICAL: Clear ALL form fields before entering new data
            # This ensures we're starting fresh for each report
            
            # Clear the reference field using JavaScript to ensure it's completely empty
            ref_field = self.driver.find_element(
                By.NAME, 
                "CustomAgentRDAccountFG.EBANKING_REF_NUMBER"
            )
            self.driver.execute_script("arguments[0].value = '';", ref_field)
            time.sleep(0.3)
            
            # Now enter the new reference number
            ref_field.clear()
            ref_field.send_keys(reference_number)
            time.sleep(0.5)
            
            # Force a page refresh by clicking on the field and tabbing out
            # This ensures the form recognizes the new value
            ref_field.send_keys(Keys.TAB)
            time.sleep(0.5)
            
            # Select Status: Success
            status_select = Select(
                self.driver.find_element(By.NAME, "CustomAgentRDAccountFG.INSTALLMENT_STATUS")
            )
            status_select.select_by_value("SUC")
            time.sleep(0.5)
            
            # Select Output Format: XLS
            format_select = Select(
                self.driver.find_element(By.NAME, "CustomAgentRDAccountFG.OUTFORMAT")
            )
            format_select.select_by_value("4")
            time.sleep(0.5)
            
            # Get list of files before download
            before_files = set(os.listdir(self.download_dir))
            
            # Wait for button to be clickable
            generate_button = WebDriverWait(self.driver, 10).until(
                EC.element_to_be_clickable((By.NAME, "Action.GENERATE_REPORT"))
            )
            
            # Scroll button into view
            self.driver.execute_script("arguments[0].scrollIntoView(true);", generate_button)
            time.sleep(0.5)
            
            # Click Generate Report
            generate_button.click()
            
            # Wait for file to download
            downloaded_file = self.wait_for_download(before_files, timeout=20)
            
            return downloaded_file
                
        except Exception as e:
            print(f"   Error: {str(e)}")
            traceback.print_exc()
            return None
            
    def wait_for_download(self, before_files, timeout=30):
        """Wait for a file to be downloaded"""
        start_time = time.time()
        
        while time.time() - start_time < timeout:
            time.sleep(1)
            current_files = set(os.listdir(self.download_dir))
            new_files = current_files - before_files
            
            completed_files = [f for f in new_files 
                             if not f.endswith('.crdownload') 
                             and not f.endswith('.tmp')
                             and 'RDInstallmentReport' in f]
            
            if completed_files:
                return str(self.download_dir / completed_files[0])
        
        return None
    
    # ==================== PDF CONVERSION METHODS ====================
    
    def parse_excel_report(self, excel_file):
        """Parse the Excel file and extract relevant data"""
        print("   → Parsing Excel data...")
        
        excel_path = Path(excel_file)
        extension = excel_path.suffix.lower()
        read_errors = []

        # Prefer engine by extension. DOP download is usually .xls.
        if extension == '.xls':
            try:
                import xlrd  # noqa: F401
            except Exception:
                print("   → xlrd missing for .xls. Installing...")
                try:
                    subprocess.check_call([sys.executable, "-m", "pip", "install", "xlrd>=2.0.1"])
                    print("   ✓ Installed xlrd")
                except Exception as ex:
                    print(f"   ❌ Could not install xlrd automatically: {str(ex)}")
                    raise

            try:
                df = pd.read_excel(excel_file, header=None, engine='xlrd')
            except Exception as ex:
                read_errors.append(f"xlrd failed: {str(ex)}")
                try:
                    df = pd.read_excel(excel_file, header=None, engine='openpyxl')
                except Exception as ex2:
                    read_errors.append(f"openpyxl fallback failed: {str(ex2)}")
                    print(f"   ❌ Could not read Excel file: {' | '.join(read_errors)}")
                    raise
        else:
            # .xlsx/.xlsm path
            try:
                df = pd.read_excel(excel_file, header=None, engine='openpyxl')
            except Exception as ex:
                read_errors.append(f"openpyxl failed: {str(ex)}")
                try:
                    df = pd.read_excel(excel_file, header=None, engine='xlrd')
                except Exception as ex2:
                    read_errors.append(f"xlrd fallback failed: {str(ex2)}")
                    print(f"   ❌ Could not read Excel file: {' | '.join(read_errors)}")
                    raise
        
        metadata = {}
        
        agent_id = df.iloc[3, 8]
        if pd.notna(agent_id):
            metadata['agent_id'] = str(agent_id).strip()
        else:
            metadata['agent_id'] = 'N/A'
        
        date_str = df.iloc[4, 8]
        if pd.notna(date_str):
            match = re.search(r'(\d{2}-[A-Za-z]{3}-\d{4})\s+To Date:\s+(\d{2}-[A-Za-z]{3}-\d{4})', str(date_str))
            if match:
                metadata['from_date'] = match.group(1)
                metadata['to_date'] = match.group(2)
            else:
                metadata['from_date'] = 'N/A'
                metadata['to_date'] = 'N/A'
        
        ref_no = df.iloc[5, 8]
        if pd.notna(ref_no):
            metadata['reference_number'] = str(ref_no).strip()
        else:
            metadata['reference_number'] = 'N/A'
        
        status = df.iloc[6, 8]
        if pd.notna(status):
            metadata['status'] = str(status).strip()
        else:
            metadata['status'] = 'N/A'
        
        records = []
        
        for idx in range(13, len(df)):
            row = df.iloc[idx]
            
            if pd.notna(row[2]) and row[2] == 'E-Banking Ref No':
                break
            
            ref_no_cell = row[3]
            
            if pd.notna(ref_no_cell) and str(ref_no_cell).strip() == metadata['reference_number']:
                record = {
                    'ref_no': str(ref_no_cell).strip(),
                    'account_number': str(row[5]).strip() if pd.notna(row[5]) else '',
                    'account_name': str(row[6]).strip() if pd.notna(row[6]) else '',
                    'deposit_amount': str(row[10]).strip() if pd.notna(row[10]) else '',
                    'installments': str(row[12]).strip() if pd.notna(row[12]) else '1',
                    'rebate': str(row[13]).strip() if pd.notna(row[13]) else '0.0',
                    'default_fee': str(row[14]).strip() if pd.notna(row[14]) else '0.0',
                    'aslaas_no': str(row[20]).strip() if pd.notna(row[20]) else 'APPLIED',
                    'balance': 'N/A'
                }
                
                record['deposit_amount'] = record['deposit_amount'].replace('Cr.', '').replace(',', '').strip()
                
                records.append(record)
        
        total_amount = 0
        for record in records:
            try:
                amount = float(record['deposit_amount'])
                total_amount += amount
            except:
                pass
        
        metadata['total_amount'] = int(total_amount)
        
        print(f"   ✓ Parsed {len(records)} records, Total: {metadata['total_amount']}")
        
        return metadata, records
    
    def create_pdf(self, metadata, records, output_file):
        """Create PDF matching the template format"""
        print(f"   → Creating PDF: {os.path.basename(output_file)}")
        
        doc = SimpleDocTemplate(
            output_file,
            pagesize=A4,
            rightMargin=30,
            leftMargin=30,
            topMargin=30,
            bottomMargin=30
        )
        
        elements = []
        styles = getSampleStyleSheet()
        
        title_style = ParagraphStyle(
            'CustomTitle',
            parent=styles['Heading1'],
            fontSize=14,
            textColor=colors.black,
            spaceAfter=12,
            alignment=TA_CENTER,
            fontName='Helvetica-Bold'
        )
        
        normal_style = ParagraphStyle(
            'CustomNormal',
            parent=styles['Normal'],
            fontSize=10,
            textColor=colors.black,
            fontName='Helvetica'
        )

        table_header_style = ParagraphStyle(
            'TableHeader',
            parent=styles['Normal'],
            fontSize=7.5,
            leading=8.5,
            textColor=colors.black,
            alignment=TA_CENTER,
            fontName='Helvetica-Bold',
            splitLongWords=True
        )

        table_cell_center = ParagraphStyle(
            'TableCellCenter',
            parent=styles['Normal'],
            fontSize=7,
            leading=8,
            textColor=colors.black,
            alignment=TA_CENTER,
            fontName='Helvetica',
            splitLongWords=True
        )

        table_cell_left = ParagraphStyle(
            'TableCellLeft',
            parent=table_cell_center,
            alignment=TA_LEFT
        )

        def as_para(value, style, default='-'):
            text = '' if value is None else str(value).strip()
            if not text:
                text = default
            safe_text = escape(text).replace('\n', '<br/>')
            return Paragraph(safe_text, style)
        
        title = Paragraph("RECURRING DEPOSIT INSTALLMENT REPORT", title_style)
        elements.append(title)
        elements.append(Spacer(1, 12))
        
        agent_info = Paragraph(f"Agent Id:{metadata['agent_id']}", normal_style)
        elements.append(agent_info)
        elements.append(Spacer(1, 6))
        
        date_info = Paragraph(f"From Date:{metadata['from_date']} To Date:{metadata['to_date']}", normal_style)
        elements.append(date_info)
        elements.append(Spacer(1, 6))
        
        ref_info = Paragraph(f"List Refrence No:{metadata['reference_number']}", normal_style)
        elements.append(ref_info)
        elements.append(Spacer(1, 6))
        
        status_info = Paragraph(f"Status:{metadata['status']}", normal_style)
        elements.append(status_info)
        elements.append(Spacer(1, 6))
        
        amount_info = Paragraph(f"Total Amount:{metadata['total_amount']}", normal_style)
        elements.append(amount_info)
        elements.append(Spacer(1, 20))
        
        table_data = []
        
        headers = [
            as_para('Sr\nno.', table_header_style),
            as_para('E-Banking\nRef No', table_header_style),
            as_para('Rd Account\nNumber', table_header_style),
            as_para('Account Name', table_header_style),
            as_para('RD Total\nDeposit\nAmount', table_header_style),
            as_para('No of\nInstallment', table_header_style),
            as_para('Rebate', table_header_style),
            as_para('Default\nFee', table_header_style),
            as_para('Aslaas\nNo.', table_header_style),
            as_para('Balance', table_header_style)
        ]
        table_data.append(headers)
        
        for idx, record in enumerate(records, 1):
            row = [
                as_para(str(idx), table_cell_center),
                as_para(record['ref_no'], table_cell_center),
                as_para(record['account_number'], table_cell_center),
                as_para(record['account_name'], table_cell_left),
                as_para(record['deposit_amount'], table_cell_center),
                as_para(record['installments'], table_cell_center),
                as_para(record['rebate'], table_cell_center),
                as_para(record['default_fee'], table_cell_center),
                as_para(record['aslaas_no'], table_cell_center),
                as_para(record['balance'], table_cell_center)
            ]
            table_data.append(row)

        # Scale column widths to the printable area to avoid clipping on A4.
        width_ratios = [0.55, 1.05, 1.15, 1.85, 1.0, 0.85, 0.65, 0.65, 0.85, 0.75]
        ratio_total = sum(width_ratios)
        printable_width = doc.width
        col_widths = [(ratio / ratio_total) * printable_width for ratio in width_ratios]

        table = Table(table_data, colWidths=col_widths, repeatRows=1)
        
        table.setStyle(TableStyle([
            ('BACKGROUND', (0, 0), (-1, 0), colors.white),
            ('TEXTCOLOR', (0, 0), (-1, 0), colors.black),
            ('ALIGN', (0, 0), (-1, 0), 'CENTER'),
            ('VALIGN', (0, 0), (-1, -1), 'MIDDLE'),
            ('BOTTOMPADDING', (0, 0), (-1, 0), 4),
            ('TOPPADDING', (0, 0), (-1, 0), 4),
            ('BACKGROUND', (0, 1), (-1, -1), colors.white),
            ('TEXTCOLOR', (0, 1), (-1, -1), colors.black),
            ('TOPPADDING', (0, 1), (-1, -1), 2),
            ('BOTTOMPADDING', (0, 1), (-1, -1), 2),
            ('LEFTPADDING', (0, 0), (-1, -1), 3),
            ('RIGHTPADDING', (0, 0), (-1, -1), 3),
            ('GRID', (0, 0), (-1, -1), 0.5, colors.black),
            ('BOX', (0, 0), (-1, -1), 1, colors.black),
        ]))
        
        elements.append(table)
        elements.append(Spacer(1, 20))
        
        summary_data = [
            ['E-Banking Ref No', 'Total Deposit Amount'],
            [metadata['reference_number'], str(metadata['total_amount'])]
        ]
        
        summary_table = Table(summary_data, colWidths=[2*inch, 2*inch])
        summary_table.setStyle(TableStyle([
            ('BACKGROUND', (0, 0), (-1, 0), colors.white),
            ('TEXTCOLOR', (0, 0), (-1, -1), colors.black),
            ('ALIGN', (0, 0), (-1, -1), 'LEFT'),
            ('FONTNAME', (0, 0), (-1, 0), 'Helvetica-Bold'),
            ('FONTNAME', (0, 1), (-1, -1), 'Helvetica'),
            ('FONTSIZE', (0, 0), (-1, -1), 10),
            ('GRID', (0, 0), (-1, -1), 0.5, colors.black),
            ('TOPPADDING', (0, 0), (-1, -1), 6),
            ('BOTTOMPADDING', (0, 0), (-1, -1), 6),
        ]))
        
        elements.append(summary_table)
        
        doc.build(elements)
        print(f"   ✓ PDF created successfully")
    
    def convert_excel_to_pdf(self, excel_file, reference_number):
        """Convert Excel report to PDF format"""
        try:
            metadata, records = self.parse_excel_report(excel_file)
            
            pdf_filename = f"{reference_number}.pdf"
            pdf_path = str(self.reports_dir / pdf_filename)
            
            self.create_pdf(metadata, records, pdf_path)
            self.log_message(f"PDF created: {pdf_path}")
            
            try:
                os.remove(excel_file)
                print(f"   ✓ Cleaned up Excel file")
                self.log_message(f"Cleaned downloaded Excel: {excel_file}")
            except Exception as e:
                print(f"   ⚠️  Could not delete Excel: {str(e)}")
            
            return pdf_path
            
        except Exception as e:
            print(f"   ⚠️  PDF conversion failed: {str(e)}")
            traceback.print_exc()
            self.log_message(f"PDF conversion failed for {reference_number}: {str(e)}")
            return None
    
    # ==================== END PDF CONVERSION METHODS ====================
        
    def print_pdf_grayscale(self, pdf_file, copies=2):
        """Print PDF file in grayscale - EXACTLY 2 copies"""
        print(f"\n   → Printing {copies} grayscale copies")
        copies = max(1, int(copies))
        
        # macOS / Linux print path (CUPS / lp)
        if self.os_type in ('Darwin', 'Linux'):
            try:
                # Different CUPS drivers accept different grayscale options.
                commands = [
                    ["lp", "-n", str(copies), "-o", "print-color-mode=monochrome", "-o", "ColorModel=Gray", pdf_file],
                    ["lp", "-n", str(copies), "-o", "ColorModel=Gray", pdf_file],
                    ["lp", "-n", str(copies), "-o", "output-mode=monochrome", pdf_file],
                    ["lp", "-n", str(copies), pdf_file],
                ]

                for cmd in commands:
                    result = subprocess.run(cmd, capture_output=True, text=True)
                    if result.returncode == 0:
                        print("   ✓ Print job sent (lp)")
                        self.log_message(f"Print job sent via lp for: {pdf_file} (copies={copies})")
                        if result.stdout.strip():
                            print(f"      {result.stdout.strip()}")
                        return

                print("   ⚠️  lp print command failed")
                last_err = result.stderr.strip() if 'result' in locals() else "Unknown lp error"
                self.log_message(f"lp print failed for {pdf_file}: {last_err}")
                if last_err:
                    print(f"      {last_err}")
                if self.os_type == 'Darwin':
                    try:
                        subprocess.Popen(["open", pdf_file])
                        print("      Opened PDF in Preview for manual print")
                    except Exception:
                        print(f"      Please manually print: {pdf_file}")
                else:
                    print(f"      Please manually print: {pdf_file}")
                return
            except FileNotFoundError:
                print("   ⚠️  'lp' command not found on this system")
                self.log_message(f"lp command not found; manual print required for {pdf_file}")
                if self.os_type == 'Darwin':
                    try:
                        subprocess.Popen(["open", pdf_file])
                        print("      Opened PDF in Preview for manual print")
                    except Exception:
                        print(f"      Please manually print: {pdf_file}")
                else:
                    print(f"      Please manually print: {pdf_file}")
                return
            except Exception as e:
                print(f"   ⚠️  Printing failed: {str(e)}")
                self.log_message(f"Printing failed for {pdf_file}: {str(e)}")
                print(f"      Please manually print: {pdf_file}")
                return

        if not WINDOWS_PRINTING:
            print("   ⚠️  Windows printing libraries not available")
            print(f"      Please manually print: {pdf_file}")
            return
        
        try:
            normalized_pdf = os.path.abspath(str(pdf_file)).strip().strip('"')
            pdf_dir = os.path.dirname(normalized_pdf) or "."

            def open_for_manual_print():
                try:
                    os.startfile(normalized_pdf)  # type: ignore[attr-defined]
                    print("      Opened PDF for manual print")
                except Exception:
                    print(f"      Please manually print: {normalized_pdf}")

            def print_once_windows(target_printer_name):
                errors = []

                # Preferred: explicit printer via printto.
                if target_printer_name:
                    try:
                        win32api.ShellExecute(
                            0,
                            "printto",
                            normalized_pdf,
                            f'"{target_printer_name}"',
                            pdf_dir,
                            0
                        )
                        return True
                    except Exception as ex:
                        errors.append(f"printto: {str(ex)}")

                # Fallback 1: default associated app print verb.
                try:
                    win32api.ShellExecute(0, "print", normalized_pdf, None, pdf_dir, 0)
                    return True
                except Exception as ex:
                    errors.append(f"print: {str(ex)}")

                # Fallback 2: cmd start /print.
                try:
                    subprocess.Popen(
                        ["cmd", "/c", "start", "", "/min", "/print", normalized_pdf],
                        cwd=pdf_dir
                    )
                    return True
                except Exception as ex:
                    errors.append(f"cmd-start-print: {str(ex)}")

                # Fallback 3: PowerShell Start-Process -Verb Print.
                try:
                    escaped = normalized_pdf.replace("'", "''")
                    subprocess.Popen(
                        [
                            "powershell",
                            "-NoProfile",
                            "-ExecutionPolicy",
                            "Bypass",
                            "-Command",
                            f"Start-Process -FilePath '{escaped}' -Verb Print"
                        ],
                        cwd=pdf_dir
                    )
                    return True
                except Exception as ex:
                    errors.append(f"powershell-print: {str(ex)}")

                self.log_message(
                    f"Windows print failed for {normalized_pdf}: {' | '.join(errors)}"
                )
                return False

            printers = [printer[2] for printer in win32print.EnumPrinters(2)]
            
            if not printers:
                print("   ⚠️  No printers found")
                self.log_message(f"No printers found on Windows for {normalized_pdf}")
                open_for_manual_print()
                return
            
            target_printer = None
            for printer in printers:
                if 'L210' in printer.upper() or 'EPSON' in printer.upper():
                    target_printer = printer
                    break
            
            if not target_printer:
                target_printer = win32print.GetDefaultPrinter()
            
            print(f"      Using printer: {target_printer}")

            sent = 0
            for i in range(copies):
                if print_once_windows(target_printer):
                    sent += 1
                    time.sleep(1.2)
                else:
                    print("   ⚠️  Auto-print failed on Windows")
                    open_for_manual_print()
                    return

            print(f"   ✓ Print job sent ({sent} copy/copies)")
            self.log_message(f"Print job sent on Windows for {normalized_pdf} (copies={sent})")
            
        except Exception as e:
            print(f"   ⚠️  Printing failed: {str(e)}")
            self.log_message(f"Windows printing failed for {pdf_file}: {str(e)}")
            try:
                os.startfile(os.path.abspath(str(pdf_file)))  # type: ignore[attr-defined]
                print("      Opened PDF for manual print")
            except Exception:
                print(f"      Please manually print: {pdf_file}")
            
    def parse_bulk_lists_with_modes(self, lists_string, default_pay_mode="cash"):
        """Parse payload like 'cash:[...], dop_cheque:[...], [...]'."""
        normalized_default = self.normalize_pay_mode(default_pay_mode)
        pattern = re.compile(r'(?:(cash|dop_cheque|non_dop_cheque|dop cheque|non dop cheque)\s*:)?\s*\[(.*?)\]', re.IGNORECASE | re.DOTALL)
        parsed = []

        for match in pattern.finditer(lists_string or ""):
            mode_raw = match.group(1)
            list_content = (match.group(2) or "").strip()
            parsed.append((f"[{list_content}]", self.normalize_pay_mode(mode_raw or normalized_default)))

        return parsed

    def process_bulk_lists(self, lists_string, pay_mode="cash", dop_cheque_data=None):
        """Process multiple account lists - OPTIMIZED BATCH VERSION
        
        Args:
            lists_string: String containing multiple lists
        """
        self.print_section("BULK LIST PROCESSING (OPTIMIZED)")
        
        parsed_lists = self.parse_bulk_lists_with_modes(lists_string, default_pay_mode=pay_mode)
        
        if not parsed_lists:
            print("❌ No valid lists found in input")
            return
        
        total_lists = len(parsed_lists)
        print(f"→ Found {total_lists} lists to process")
        print("→ Strategy: Process all lists first, then download all reports in batch!\n")
        
        # PHASE 1: Process all lists and collect reference numbers
        collected_references = []
        
        for idx, (list_string, list_mode) in enumerate(parsed_lists, 1):
            
            try:
                parsed_count = len(self.parse_account_list(list_string))
                print(f"\n→ Dispatching list #{idx}: mode={list_mode}, accounts={parsed_count}")
                list_cheque_details = None
                if isinstance(dop_cheque_data, dict):
                    list_cheque_details = dop_cheque_data.get(idx) or dop_cheque_data.get(str(idx)) or None
                if isinstance(list_cheque_details, dict):
                    print(f"   → Cheque/account details provided for {len(list_cheque_details)} account(s)")
                reference_number = self.process_account_list(
                    list_string,
                    idx,
                    pay_mode=list_mode,
                    dop_cheque_details=list_cheque_details
                )
                
                if reference_number:
                    collected_references.append(reference_number)
                else:
                    self.failed_lists.append({
                        'list_index': idx, 
                        'reason': 'Payment processing failed'
                    })
                    
            except Exception as e:
                print(f"❌ Error processing list #{idx}: {str(e)}")
                self.failed_lists.append({
                    'list_index': idx,
                    'reason': str(e)
                })
                continue
        
        print(f"\n✓ Phase 1 Complete: Processed {len(collected_references)} lists successfully")
        
        # PHASE 2: Batch download all reports
        if collected_references:
            print("\n" + "="*80)
            print(" PHASE 2: BATCH DOWNLOADING ALL REPORTS")
            print("="*80)
            
            downloaded_files = self.generate_all_reports(collected_references)
            
            # PHASE 3: Convert all to PDF and print
            print("\n" + "="*80)
            print(" PHASE 3: CONVERTING TO PDF AND PRINTING")
            print("="*80)
            
            successful_reports = []
            
            for file_info in downloaded_files:
                ref_num = file_info['reference']
                excel_file = file_info['file']
                
                print(f"\nProcessing: {ref_num}")
                
                pdf_file = self.convert_excel_to_pdf(excel_file, ref_num)
                
                if pdf_file:
                    self.print_pdf_grayscale(pdf_file, copies=2)
                    successful_reports.append({
                        'reference': ref_num,
                        'pdf_file': pdf_file
                    })
                    print(f"✓ Complete: {ref_num}")
                else:
                    print(f"❌ PDF conversion failed for {ref_num}")
        
            # Print summary
            self.print_bulk_summary(total_lists, successful_reports)
        else:
            print("\n❌ No successful reference numbers to download")
        
    def print_bulk_summary(self, total_lists, successful_reports):
        """Print processing summary"""
        self.print_section("PROCESSING SUMMARY")
        
        print(f"Total Lists: {total_lists}")
        print(f"Successful: {len(successful_reports)}")
        print(f"Failed: {len(self.failed_lists)}")
        
        if successful_reports:
            print("\n✓ SUCCESSFUL REPORTS:")
            for report in successful_reports:
                print(f"   {report['reference']}")
                print(f"      PDF: {report['pdf_file']}")
        
        if self.failed_lists:
            print("\n❌ FAILED LISTS:")
            for failed in self.failed_lists:
                print(f"   List #{failed['list_index']}: {failed['reason']}")
        
        print(f"\n💾 Reference numbers saved to: {self.reference_file}")
        print(f"📁 PDF reports saved to: {self.reports_dir}")
        
    def run_single_list(self, account_list, pay_mode="cash"):
        """Run report generation for a single list"""
        try:
            self.setup_driver()
            
            if not self.login():
                print("❌ Login failed. Exiting...")
                return
            
            if not self.navigate_to_agent_screen():
                print("❌ Navigation failed. Exiting...")
                return
            
            reference_number = self.process_account_list(account_list, 1, pay_mode=pay_mode)
            
            if reference_number:
                # For single list, download immediately
                downloaded_files = self.generate_all_reports([reference_number])
                
                if downloaded_files:
                    excel_file = downloaded_files[0]['file']
                    pdf_file = self.convert_excel_to_pdf(excel_file, reference_number)
                    
                    if pdf_file:
                        self.print_pdf_grayscale(pdf_file, copies=2)
                        print("\n✅ Single list processing completed successfully!")
                        print(f"📄 PDF saved to: {pdf_file}")
                else:
                    print("❌ Report generation failed")
            else:
                print("❌ Payment processing failed")
                
        except Exception as e:
            print(f"\n❌ Error occurred: {str(e)}")
            traceback.print_exc()
            
        finally:
            self.close_browser()

    def run_aslaas_only(self, updates):
        """Run only ASLAAS update flow without list processing."""
        try:
            valid_updates = [
                item for item in (updates or [])
                if isinstance(item, dict) and str(item.get("account_no", "")).strip()
            ]

            if not valid_updates:
                print("⚠️  No ASLAAS updates provided.")
                return

            self.setup_driver()

            if not self.login():
                print("❌ Login failed. Exiting...")
                return

            if not self.apply_aslaas_updates(valid_updates):
                print("❌ ASLAAS update failed.")
                return

            print("\n✅ ASLAAS update completed successfully!")
        except Exception as e:
            print(f"\n❌ Error occurred: {str(e)}")
            traceback.print_exc()
        finally:
            self.close_browser()
                
    def run_bulk_lists(self, lists_string, aslaas_updates=None, pay_mode="cash", dop_cheque_data=None):
        """Run report generation for multiple lists"""
        try:
            self.setup_driver()
            
            if not self.login():
                print("❌ Login failed. Exiting...")
                return

            if aslaas_updates:
                if not self.apply_aslaas_updates(aslaas_updates):
                    print("❌ ASLAAS update phase failed. Stopping before list processing.")
                    return

            if not self.navigate_to_agent_screen():
                print("❌ Navigation failed. Exiting...")
                return
            
            self.process_bulk_lists(
                lists_string,
                pay_mode=pay_mode,
                dop_cheque_data=dop_cheque_data
            )
            
            print("\n✅ Bulk list processing completed!")
            
        except Exception as e:
            print(f"\n❌ Error occurred: {str(e)}")
            traceback.print_exc()
            
        finally:
            self.close_browser()


def main():
    """Main entry point"""
    print("\n" + "="*80)
    print(" DOP AGENT PORTAL - OPTIMIZED BATCH PROCESSING")
    print(" Process all lists → Download all reports → Convert all PDFs!")
    print("="*80)
    
    parser = argparse.ArgumentParser(
        description='DOP Agent Report Generation Tool - Optimized Batch Processing!',
        formatter_class=argparse.RawDescriptionHelpFormatter
    )
    
    parser.add_argument('--single', type=str, help='Process a single account list')
    parser.add_argument('--bulk', type=str, help='Process multiple account lists')
    parser.add_argument(
        '--browser',
        type=str,
        default='chrome',
        help='Browser: chrome, edge, ie, safari'
    )
    parser.add_argument(
        '--pay-mode',
        type=str,
        default='cash',
        help='Payment mode: cash, dop_cheque, non_dop_cheque'
    )
    parser.add_argument(
        '--dop-cheque-data',
        type=str,
        help='JSON array of {"list_index":1,"account_no":"...","cheque_no":"...","payment_account_no":"..."}'
    )
    parser.add_argument(
        '--aslaas-updates',
        type=str,
        help='JSON array of {"account_no":"...","aslaas_no":"..."} to update before list processing'
    )
    parser.add_argument(
        '--aslaas-only',
        type=str,
        help='JSON array of {"account_no":"...","aslaas_no":"..."} to update ASLAAS only'
    )
    parser.add_argument(
        '--aslaas-only-file',
        type=str,
        help='Path to JSON file containing ASLAAS-only updates'
    )
    
    args = parser.parse_args()
    agent = DOPAgentReports(browser=args.browser)
    pay_mode = DOPAgentReports.normalize_pay_mode(args.pay_mode)
    
    aslaas_updates = []
    dop_cheque_data = {}
    aslaas_payload = None
    if args.aslaas_only_file:
        try:
            with open(args.aslaas_only_file, 'r', encoding='utf-8-sig') as fp:
                aslaas_payload = fp.read()
        except Exception as ex:
            print(f"⚠️  Could not read --aslaas-only-file: {str(ex)}")

    if not aslaas_payload:
        aslaas_payload = args.aslaas_only if args.aslaas_only else args.aslaas_updates
    if aslaas_payload:
        try:
            parsed_updates = json.loads(aslaas_payload)
            if isinstance(parsed_updates, list):
                aslaas_updates = parsed_updates
            else:
                print("⚠️  Ignoring ASLAAS payload because it is not a JSON list")
        except Exception as ex:
            print(f"⚠️  Could not parse ASLAAS payload: {str(ex)}")

    if args.dop_cheque_data:
        try:
            parsed_dop_cheque = json.loads(args.dop_cheque_data)
            if isinstance(parsed_dop_cheque, list):
                for entry in parsed_dop_cheque:
                    if not isinstance(entry, dict):
                        continue
                    index = entry.get("list_index")
                    account_no = str(entry.get("account_no", "")).strip()
                    cheque_no = str(entry.get("cheque_no", "")).strip()
                    payment_account_no = str(entry.get("payment_account_no", "")).strip()
                    payment_mode = DOPAgentReports.normalize_pay_mode(entry.get("payment_mode", "dop_cheque"))
                    try:
                        index = int(index)
                    except Exception:
                        continue
                    requires_cheque_no = payment_mode == "dop_cheque"
                    if (
                        index <= 0 or
                        not account_no or
                        not payment_account_no or
                        (requires_cheque_no and not cheque_no)
                    ):
                        continue
                    if index not in dop_cheque_data:
                        dop_cheque_data[index] = {}
                    dop_cheque_data[index] = {
                        **dop_cheque_data[index],
                        account_no: {
                            "payment_mode": payment_mode,
                            "cheque_no": cheque_no,
                            "payment_account_no": payment_account_no
                        }
                    }
            else:
                print("⚠️  Ignoring --dop-cheque-data because payload is not a JSON list")
        except Exception as ex:
            print(f"⚠️  Could not parse --dop-cheque-data payload: {str(ex)}")

    if args.aslaas_only or args.aslaas_only_file:
        print("\n📋 MODE: ASLAAS Update Only")
        agent.run_aslaas_only(aslaas_updates)
    elif args.single:
        print("\n📋 MODE: Single List Processing")
        agent.run_single_list(args.single, pay_mode=pay_mode)
    elif args.bulk:
        print("\n📋 MODE: Bulk List Processing (Optimized)")
        agent.run_bulk_lists(
            args.bulk,
            aslaas_updates=aslaas_updates,
            pay_mode=pay_mode,
            dop_cheque_data=dop_cheque_data
        )
    else:
        print("\n📋 INTERACTIVE MODE")
        print("\n1. Single list")
        print("2. Bulk lists")
        print("3. Exit")
        
        choice = input("\nEnter choice (1-3): ").strip()
        
        if choice == '1':
            print("\nEnter account list:")
            account_list = input("> ").strip()
            if account_list:
                agent.run_single_list(account_list, pay_mode=pay_mode)
        elif choice == '2':
            print("\nEnter multiple lists:")
            lists_string = input("> ").strip()
            if lists_string:
                agent.run_bulk_lists(lists_string, pay_mode=pay_mode)
        elif choice == '3':
            print("\n👋 Goodbye!")


if __name__ == "__main__":
    main()
