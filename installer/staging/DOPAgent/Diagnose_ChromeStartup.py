#!/usr/bin/env python3
"""
Chrome startup diagnostics for DOPAgent (Windows-focused).

Purpose:
- Measure where startup delay happens (driver init vs first navigation)
- Capture environment details (Python, Selenium, Chrome, PATH)
- Check basic network reachability used by Selenium Manager
- Save full report to JSON and text files for sharing

Usage:
    python Diagnose_ChromeStartup.py
    python Diagnose_ChromeStartup.py --runs 3 --include-isolated
"""

from __future__ import annotations

import argparse
import json
import os
import platform
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import traceback
import urllib.request
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, List, Tuple


def resolve_base_dir() -> Path:
    """Resolve DOPAgent base folder without depending on app runtime."""
    try:
        from dop_paths import resolve_base_dir as _resolve_base_dir

        return _resolve_base_dir()
    except Exception:
        base = Path.home() / "Documents" / "DOPAgent"
        base.mkdir(parents=True, exist_ok=True)
        return base


def run_command(command: List[str], timeout: int = 20) -> Dict[str, Any]:
    """Run shell command and return normalized result."""
    started = time.perf_counter()
    try:
        proc = subprocess.run(
            command,
            capture_output=True,
            text=True,
            timeout=timeout,
            check=False,
        )
        return {
            "ok": proc.returncode == 0,
            "returncode": proc.returncode,
            "stdout": (proc.stdout or "").strip(),
            "stderr": (proc.stderr or "").strip(),
            "seconds": round(time.perf_counter() - started, 3),
            "command": command,
        }
    except Exception as ex:
        return {
            "ok": False,
            "returncode": None,
            "stdout": "",
            "stderr": str(ex),
            "seconds": round(time.perf_counter() - started, 3),
            "command": command,
        }


def detect_chrome_paths() -> List[str]:
    """Find possible Chrome executable paths on Windows/macOS/Linux."""
    candidates = []
    which_names = [
        "chrome",
        "google-chrome",
        "google-chrome-stable",
        "chromium",
        "chromium-browser",
        "chrome.exe",
    ]
    for name in which_names:
        found = shutil.which(name)
        if found:
            candidates.append(found)

    if platform.system() == "Windows":
        env = os.environ
        win_candidates = [
            Path(env.get("PROGRAMFILES", "")) / "Google" / "Chrome" / "Application" / "chrome.exe",
            Path(env.get("PROGRAMFILES(X86)", "")) / "Google" / "Chrome" / "Application" / "chrome.exe",
            Path(env.get("LOCALAPPDATA", "")) / "Google" / "Chrome" / "Application" / "chrome.exe",
        ]
        for item in win_candidates:
            if str(item) and item.exists():
                candidates.append(str(item))
    elif platform.system() == "Darwin":
        mac_path = Path("/Applications/Google Chrome.app/Contents/MacOS/Google Chrome")
        if mac_path.exists():
            candidates.append(str(mac_path))

    # Deduplicate while preserving order
    out: List[str] = []
    seen = set()
    for path in candidates:
        norm = str(path).strip()
        if norm and norm not in seen:
            seen.add(norm)
            out.append(norm)
    return out


def get_chrome_version(chrome_path: str) -> Dict[str, Any]:
    return run_command([chrome_path, "--version"], timeout=10)


def find_chromedriver_paths() -> Dict[str, Any]:
    info: Dict[str, Any] = {"where": None, "selenium_cache_matches": []}
    if platform.system() == "Windows":
        info["where"] = run_command(["where", "chromedriver"], timeout=10)
        local_app_data = os.environ.get("LOCALAPPDATA", "")
        if local_app_data:
            root = Path(local_app_data) / "selenium"
            if root.exists():
                matches = [str(p) for p in root.rglob("chromedriver.exe")]
                info["selenium_cache_matches"] = matches[:30]
    else:
        info["where"] = run_command(["which", "chromedriver"], timeout=10)
    return info


def check_network(hosts: List[str], timeout: int = 6) -> List[Dict[str, Any]]:
    """Simple DNS + HTTPS reachability checks."""
    results: List[Dict[str, Any]] = []
    for host in hosts:
        entry: Dict[str, Any] = {"host": host, "dns_ok": False, "https_ok": False}
        try:
            resolved = socket.gethostbyname_ex(host)
            entry["dns_ok"] = True
            entry["dns"] = resolved[2][:5]
        except Exception as ex:
            entry["dns_error"] = str(ex)

        try:
            req = urllib.request.Request(
                f"https://{host}",
                method="GET",
                headers={"User-Agent": "DOPAgent-Diagnostics/1.0"},
            )
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                status = getattr(resp, "status", None)
                entry["https_ok"] = status is not None and status < 500
                entry["https_status"] = status
        except Exception as ex:
            entry["https_error"] = str(ex)

        results.append(entry)
    return results


def safe_import_selenium() -> Tuple[bool, str]:
    try:
        import selenium  # type: ignore

        return True, getattr(selenium, "__version__", "unknown")
    except Exception as ex:
        return False, str(ex)


def make_chrome_options(headless: bool = False, isolated_profile: bool = False) -> Tuple[Any, str]:
    """Create options similar to production scripts."""
    from selenium.webdriver.chrome.options import Options  # type: ignore

    options = Options()
    options.add_argument("--start-maximized")
    options.add_argument("--no-first-run")
    options.add_argument("--no-default-browser-check")
    options.add_argument("--disable-default-apps")
    options.add_argument("--disable-popup-blocking")
    options.add_argument("--disable-blink-features=AutomationControlled")
    options.add_experimental_option("excludeSwitches", ["enable-automation"])
    options.add_experimental_option("useAutomationExtension", False)

    if headless:
        options.add_argument("--headless=new")

    profile_dir = ""
    if isolated_profile:
        profile_dir = tempfile.mkdtemp(prefix="dopagent_diag_profile_")
        options.add_argument(f"--user-data-dir={profile_dir}")

    return options, profile_dir


def benchmark_scenario(
    name: str,
    runs: int,
    url: str,
    headless: bool = False,
    isolated_profile: bool = False,
) -> Dict[str, Any]:
    """Run webdriver startup benchmark and return detailed metrics."""
    from selenium import webdriver  # type: ignore

    scenario: Dict[str, Any] = {
        "name": name,
        "runs": [],
        "headless": headless,
        "isolated_profile": isolated_profile,
    }

    for run_index in range(1, runs + 1):
        run_data: Dict[str, Any] = {"run": run_index}
        driver = None
        profile_dir = ""
        t0 = time.perf_counter()
        try:
            options, profile_dir = make_chrome_options(
                headless=headless, isolated_profile=isolated_profile
            )
            t1 = time.perf_counter()
            driver = webdriver.Chrome(options=options)
            t2 = time.perf_counter()
            driver.get(url)
            t3 = time.perf_counter()
            current_url = driver.current_url
            driver.quit()
            t4 = time.perf_counter()
            driver = None

            run_data.update(
                {
                    "ok": True,
                    "options_build_seconds": round(t1 - t0, 3),
                    "driver_init_seconds": round(t2 - t1, 3),
                    "first_get_seconds": round(t3 - t2, 3),
                    "quit_seconds": round(t4 - t3, 3),
                    "total_seconds": round(t4 - t0, 3),
                    "final_url": current_url,
                }
            )
        except Exception as ex:
            run_data.update(
                {
                    "ok": False,
                    "error": str(ex),
                    "traceback": traceback.format_exc(),
                    "elapsed_seconds": round(time.perf_counter() - t0, 3),
                }
            )
            try:
                if driver is not None:
                    driver.quit()
            except Exception:
                pass
        finally:
            if profile_dir:
                try:
                    shutil.rmtree(profile_dir, ignore_errors=True)
                except Exception:
                    pass

        scenario["runs"].append(run_data)

    init_times = [
        r["driver_init_seconds"] for r in scenario["runs"] if r.get("ok") and "driver_init_seconds" in r
    ]
    scenario["summary"] = {
        "successful_runs": len(init_times),
        "failed_runs": len(scenario["runs"]) - len(init_times),
        "driver_init_avg_seconds": round(sum(init_times) / len(init_times), 3) if init_times else None,
        "driver_init_min_seconds": min(init_times) if init_times else None,
        "driver_init_max_seconds": max(init_times) if init_times else None,
    }
    return scenario


def build_report(args: argparse.Namespace) -> Dict[str, Any]:
    now = datetime.now()
    report: Dict[str, Any] = {
        "generated_at": now.strftime("%Y-%m-%d %H:%M:%S"),
        "platform": {
            "system": platform.system(),
            "release": platform.release(),
            "version": platform.version(),
            "machine": platform.machine(),
            "processor": platform.processor(),
        },
        "python": {
            "version": sys.version,
            "executable": sys.executable,
            "cwd": os.getcwd(),
        },
        "env": {
            "PATH_preview": os.environ.get("PATH", "")[:1200],
            "LOCALAPPDATA": os.environ.get("LOCALAPPDATA", ""),
            "TEMP": os.environ.get("TEMP", ""),
            "TMP": os.environ.get("TMP", ""),
        },
    }

    selenium_ok, selenium_info = safe_import_selenium()
    report["selenium"] = {
        "available": selenium_ok,
        "version_or_error": selenium_info,
    }

    chrome_paths = detect_chrome_paths()
    report["chrome"] = {
        "candidates": chrome_paths,
        "versions": {path: get_chrome_version(path) for path in chrome_paths[:5]},
    }
    report["chromedriver"] = find_chromedriver_paths()

    report["network"] = check_network(
        [
            "googlechromelabs.github.io",
            "storage.googleapis.com",
            "edgedl.me.gvt1.com",
            "dopagent.indiapost.gov.in",
        ]
    )

    if not selenium_ok:
        report["benchmarks"] = []
        report["note"] = "Selenium is not available; benchmark skipped."
        return report

    benchmarks: List[Dict[str, Any]] = []
    benchmarks.append(
        benchmark_scenario(
            name="standard_gui",
            runs=max(1, args.runs),
            url=args.url,
            headless=False,
            isolated_profile=False,
        )
    )
    if not args.skip_headless:
        benchmarks.append(
            benchmark_scenario(
                name="standard_headless",
                runs=max(1, min(2, args.runs)),
                url=args.url,
                headless=True,
                isolated_profile=False,
            )
        )
    if args.include_isolated:
        benchmarks.append(
            benchmark_scenario(
                name="isolated_gui",
                runs=max(1, min(2, args.runs)),
                url=args.url,
                headless=False,
                isolated_profile=True,
            )
        )

    report["benchmarks"] = benchmarks
    return report


def write_reports(report: Dict[str, Any]) -> Tuple[Path, Path]:
    base_dir = resolve_base_dir()
    out_dir = base_dir / "logs" / "diagnostics"
    out_dir.mkdir(parents=True, exist_ok=True)

    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    json_path = out_dir / f"chrome_startup_diag_{stamp}.json"
    txt_path = out_dir / f"chrome_startup_diag_{stamp}.txt"

    json_path.write_text(json.dumps(report, indent=2), encoding="utf-8")

    lines = []
    lines.append("DOPAgent Chrome Startup Diagnostics")
    lines.append("=" * 60)
    lines.append(f"Generated at: {report.get('generated_at')}")
    plat = report.get("platform", {})
    lines.append(f"Platform: {plat.get('system')} {plat.get('release')} ({plat.get('machine')})")
    py = report.get("python", {})
    lines.append(f"Python: {py.get('executable')}")
    lines.append("")

    selenium = report.get("selenium", {})
    lines.append(f"Selenium available: {selenium.get('available')}")
    lines.append(f"Selenium version/error: {selenium.get('version_or_error')}")
    lines.append("")

    for scenario in report.get("benchmarks", []):
        summary = scenario.get("summary", {})
        lines.append(f"Scenario: {scenario.get('name')}")
        lines.append(f"  successful_runs: {summary.get('successful_runs')}")
        lines.append(f"  failed_runs: {summary.get('failed_runs')}")
        lines.append(f"  driver_init_avg_seconds: {summary.get('driver_init_avg_seconds')}")
        lines.append(f"  driver_init_min_seconds: {summary.get('driver_init_min_seconds')}")
        lines.append(f"  driver_init_max_seconds: {summary.get('driver_init_max_seconds')}")
        lines.append("")

    lines.append(f"Full JSON report: {json_path}")
    txt_path.write_text("\n".join(lines) + "\n", encoding="utf-8")

    return json_path, txt_path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Chrome startup diagnostics for DOPAgent.")
    parser.add_argument(
        "--runs",
        type=int,
        default=2,
        help="Number of standard GUI runs (default: 2)",
    )
    parser.add_argument(
        "--url",
        default="https://dopagent.indiapost.gov.in",
        help="URL used for first navigation timing",
    )
    parser.add_argument(
        "--include-isolated",
        action="store_true",
        help="Also benchmark isolated profile mode (--user-data-dir=temp)",
    )
    parser.add_argument(
        "--skip-headless",
        action="store_true",
        help="Skip headless benchmark scenario",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()

    print("Running Chrome startup diagnostics...")
    if platform.system() != "Windows":
        print("Note: This script is Windows-focused, but it can run on other systems.")

    report = build_report(args)
    json_path, txt_path = write_reports(report)

    print("")
    print("Done.")
    print(f"JSON report: {json_path}")
    print(f"Text report: {txt_path}")

    failed = 0
    for scenario in report.get("benchmarks", []):
        failed += scenario.get("summary", {}).get("failed_runs", 0) or 0
    if failed:
        print(f"Warning: {failed} benchmark run(s) failed. Check JSON traceback entries.")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
