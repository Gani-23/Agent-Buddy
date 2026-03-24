#!/usr/bin/env python3
"""
Setup Script for DOP Agent Automated PDF Reports
Installs all required dependencies
"""

import sys
import subprocess
import platform
import os

def print_header(text):
    print("\n" + "="*80)
    print(f" {text}")
    print("="*80 + "\n")

def check_python_version():
    """Check if Python version is 3.8+"""
    print_header("Checking Python Version")
    
    version = sys.version_info
    print(f"Python version: {version.major}.{version.minor}.{version.micro}")
    
    if version.major < 3 or (version.major == 3 and version.minor < 8):
        print("❌ Python 3.8 or higher is required")
        print("   Please upgrade Python: https://www.python.org/downloads/")
        return False
    
    print("✓ Python version is compatible")
    return True

def install_python_packages():
    """Install required Python packages"""
    print_header("Installing Python Packages")
    
    packages = [
        'pandas',
        'openpyxl',
        'reportlab',
        'selenium',
    ]
    
    # Add Windows-specific packages
    if platform.system() == 'Windows':
        packages.append('pywin32')
    
    print(f"Installing: {', '.join(packages)}\n")
    
    try:
        subprocess.check_call([
            sys.executable, '-m', 'pip', 'install', '--upgrade'
        ] + packages)
        print("\n✓ All Python packages installed successfully")
        return True
    except subprocess.CalledProcessError as e:
        print(f"\n❌ Failed to install packages: {e}")
        return False

def check_libreoffice():
    """Check if LibreOffice is installed"""
    print_header("Checking LibreOffice")
    
    try:
        result = subprocess.run(
            ['libreoffice', '--version'],
            capture_output=True,
            text=True,
            timeout=5
        )
        
        if result.returncode == 0:
            print(f"✓ LibreOffice found: {result.stdout.strip()}")
            return True
        else:
            print("⚠️  LibreOffice not found")
            return False
    except (subprocess.TimeoutExpired, FileNotFoundError):
        print("⚠️  LibreOffice not found")
        return False

def print_libreoffice_instructions():
    """Print instructions for installing LibreOffice"""
    print("\nLibreOffice Installation Instructions:")
    print("-" * 80)
    
    system = platform.system()
    
    if system == 'Windows':
        print("Windows:")
        print("  1. Download from: https://www.libreoffice.org/download/")
        print("  2. Run the installer")
        print("  3. Install with default settings")
        print("  4. Restart this setup script")
    
    elif system == 'Darwin':  # macOS
        print("macOS:")
        print("  Using Homebrew:")
        print("    brew install libreoffice")
        print("\n  Manual download:")
        print("    https://www.libreoffice.org/download/")
    
    else:  # Linux
        print("Linux (Ubuntu/Debian):")
        print("  sudo apt-get update")
        print("  sudo apt-get install libreoffice")
        print("\n  Linux (Fedora):")
        print("  sudo dnf install libreoffice")

def check_chrome():
    """Check if Chrome is installed"""
    print_header("Checking Google Chrome")
    
    system = platform.system()
    chrome_found = False
    
    if system == 'Windows':
        chrome_paths = [
            r"C:\Program Files\Google\Chrome\Application\chrome.exe",
            r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
        ]
        for path in chrome_paths:
            if os.path.exists(path):
                chrome_found = True
                break
    
    elif system == 'Darwin':  # macOS
        if os.path.exists('/Applications/Google Chrome.app'):
            chrome_found = True
    
    else:  # Linux
        try:
            result = subprocess.run(
                ['which', 'google-chrome'],
                capture_output=True
            )
            if result.returncode == 0:
                chrome_found = True
        except:
            pass
    
    if chrome_found:
        print("✓ Google Chrome found")
        return True
    else:
        print("⚠️  Google Chrome not found")
        print("   Download from: https://www.google.com/chrome/")
        return False

def create_directory_structure():
    """Create required directory structure"""
    print_header("Creating Directory Structure")
    
    home = os.path.expanduser("~")
    base_dir = os.path.join(home, 'Documents', 'DOPAgent')
    reports_dir = os.path.join(base_dir, 'Reports')
    
    try:
        os.makedirs(base_dir, exist_ok=True)
        os.makedirs(reports_dir, exist_ok=True)
        print(f"✓ Created: {base_dir}")
        print(f"✓ Created: {reports_dir}")
        return True
    except Exception as e:
        print(f"❌ Failed to create directories: {e}")
        return False

def print_summary(results):
    """Print setup summary"""
    print_header("Setup Summary")
    
    all_good = all(results.values())
    
    for item, status in results.items():
        symbol = "✓" if status else "❌"
        print(f"{symbol} {item}")
    
    if all_good:
        print("\n" + "="*80)
        print("✅ SETUP COMPLETE!")
        print("="*80)
        print("\nYou're ready to use the automated PDF report system!")
        print("\nNext steps:")
        print("1. Make sure you have saved credentials (run main DOP script)")
        print("2. Set your printer to grayscale mode")
        print("3. Run: python dop_agent_reports_with_pdf.py")
        print("\nSee QUICK_START.md for usage examples.")
    else:
        print("\n" + "="*80)
        print("⚠️  SETUP INCOMPLETE")
        print("="*80)
        print("\nPlease address the items marked with ❌ above")
        print("Then run this setup script again.")

def main():
    """Main setup function"""
    print("\n" + "="*80)
    print(" DOP AGENT AUTOMATED PDF REPORTS - SETUP")
    print("="*80)
    print("\nThis script will install all required dependencies.\n")
    
    results = {}
    
    # Check Python version
    results['Python 3.8+'] = check_python_version()
    if not results['Python 3.8+']:
        print("\n❌ Setup cannot continue without Python 3.8+")
        return
    
    # Install Python packages
    results['Python Packages'] = install_python_packages()
    
    # Check LibreOffice
    results['LibreOffice'] = check_libreoffice()
    if not results['LibreOffice']:
        print_libreoffice_instructions()
    
    # Check Chrome
    results['Google Chrome'] = check_chrome()
    
    # Create directories
    results['Directory Structure'] = create_directory_structure()
    
    # Print summary
    print_summary(results)

if __name__ == "__main__":
    main()
