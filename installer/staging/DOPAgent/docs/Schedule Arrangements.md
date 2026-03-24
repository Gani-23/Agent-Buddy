# Quick Start Guide - Automated PDF Report Generation

## What's New? ✨

This script now **AUTOMATICALLY**:
- ✅ Downloads Excel reports
- ✅ Converts them to PDF format
- ✅ Prints 2 copies in grayscale
- ✅ Deletes the Excel file (keeps only PDF)

**No more manual conversion needed!** Just run the script and everything happens automatically.

## Installation

### 1. Install Python Dependencies

```bash
pip install pandas openpyxl reportlab selenium
```

### 2. Install LibreOffice (for XLS to XLSX conversion)

**Windows:**
- Download from: https://www.libreoffice.org/download/
- Install with default settings

**Linux (Ubuntu/Debian):**
```bash
sudo apt-get install libreoffice
```

**macOS:**
```bash
brew install libreoffice
```

### 3. Install Chrome WebDriver

The script uses Selenium with Chrome. Make sure you have:
- Google Chrome browser installed
- ChromeDriver matching your Chrome version

## Usage

### Single List (Quick)

```bash
python dop_agent_reports_with_pdf.py --single "[020070332384,020065642673,020054317887]"
```

**What happens:**
1. Logs into portal
2. Processes the accounts
3. Downloads Excel report
4. **Converts to PDF automatically**
5. **Prints 2 copies automatically**
6. Saves reference number

### Bulk Lists (Multiple at Once)

```bash
python dop_agent_reports_with_pdf.py --bulk "[020070332384,020065642673],[020054317887_2,020159159607],[020024760612,020106449727]"
```

**What happens:**
1. Logs in once
2. Processes each list sequentially
3. For each list:
   - Downloads Excel
   - **Converts to PDF**
   - **Prints 2 copies**
   - Saves reference
4. Shows summary at the end

### Interactive Mode

```bash
python dop_agent_reports_with_pdf.py
```

Follow the on-screen prompts.

## Features

### Automatic PDF Conversion

- **No manual steps**: Excel is automatically converted to PDF
- **Template matching**: PDF format matches your official template exactly
- **Clean layout**: Professional table formatting
- **Auto-cleanup**: Excel files are deleted after conversion

### Automatic Printing

- **2 copies**: Always prints exactly 2 copies
- **Grayscale**: Set your printer to grayscale mode first
- **No dialog**: Prints directly without prompts
- **Works on Windows**: Automatic printing supported on Windows

### File Organization

All PDFs are saved to:
```
Documents/DOPAgent/Reports/
```

Filename format:
```
RDReport_C318942711_20260208_123456.pdf
```

## Important Notes

### Before Running

1. **Set Printer to Grayscale**
   - Open printer preferences
   - Set color mode to "Grayscale" or "Black & White"
   - Save as default

2. **Check Credentials**
   - Make sure you've saved credentials using the main script first
   - The script will auto-load your saved credentials

3. **Close Other Chrome Windows**
   - Script works best with no other Chrome windows open

### During Execution

1. **CAPTCHA Required**
   - You'll need to manually enter the CAPTCHA on login
   - Script waits for you to complete it

2. **Don't Close Browser**
   - Let the automation run completely
   - Browser will close automatically when done

### After Completion

- **Check Reports folder**: All PDFs will be saved here
- **Check Printer**: Verify 2 copies were printed
- **Check Reference File**: All reference numbers are logged

## Troubleshooting

### "No saved credentials found"

**Solution:** Run the main DOP agent script first to save your credentials:
```bash
python dop_agent_main.py
```

### "xlrd not found" or "openpyxl not found"

**Solution:** Install the required packages:
```bash
pip install pandas openpyxl reportlab
```

### "LibreOffice not found"

**Solution:** Install LibreOffice (see Installation section above)

### Printing doesn't work

**Reasons:**
1. Not on Windows (automatic printing only works on Windows)
2. No printer installed
3. win32print not available

**Solution:** 
- On Windows: Install `pywin32` package
  ```bash
  pip install pywin32
  ```
- On Linux/Mac: Manually print the PDF files from the Reports folder

### Wrong number of copies printed

**Solution:** 
- The script sends ONE print job
- Set your printer to print 2 copies by default
- Or adjust the `copies=2` parameter in the script

### PDF format looks wrong

**Solution:** The script is designed for the specific Excel format from DOP portal. If format changes, you may need to adjust the parsing logic.

## Advanced Usage

### Custom Number of Copies

Edit the script and change this line:
```python
self.print_pdf_grayscale(pdf_file, copies=2)  # Change to 3, 4, etc.
```

### Keep Excel Files

If you want to keep both Excel and PDF:

Comment out this line in the `convert_excel_to_pdf` method:
```python
# os.remove(excel_file)  # Comment this out
```

### Change PDF Filename Format

Edit the filename generation in `convert_excel_to_pdf`:
```python
pdf_filename = f"Report_{reference_number}.pdf"  # Simpler name
```

## Comparison: Old vs New

### OLD WAY ❌
1. Run automation script
2. Download Excel file
3. Run converter script manually
4. Open PDF
5. Print manually (2 times)
6. Set printer to grayscale

### NEW WAY ✅
1. Run automation script
2. **DONE!** Everything else is automatic

## File Locations

```
Documents/DOPAgent/
├── Reports/                          # PDF reports saved here
│   ├── RDReport_C318942711_20260208_123456.pdf
│   └── RDReport_C318942722_20260208_123501.pdf
├── dop_agent.db                      # Saved credentials
├── payment_references.txt            # Reference numbers log
└── dop_reports_log.txt              # Execution log
```

## Command Examples

### Process 3 lists at once
```bash
python dop_agent_reports_with_pdf.py --bulk "[020070332384,020065642673],[020054317887,020159159607],[020024760612,020106449727]"
```

### Process with installments
```bash
python dop_agent_reports_with_pdf.py --single "[020070332384_2,020065642673_3,020054317887]"
```

Note: `_2` means 2 installments, `_3` means 3 installments, etc.

## What Gets Automated

| Task | Old Script | New Script |
|------|-----------|------------|
| Login | Manual CAPTCHA | Manual CAPTCHA |
| Process accounts | ✅ Automatic | ✅ Automatic |
| Download Excel | ✅ Automatic | ✅ Automatic |
| Convert to PDF | ❌ Manual CLI | ✅ **Automatic** |
| Print document | ❌ Manual | ✅ **Automatic** |
| Print 2 copies | ❌ Manual | ✅ **Automatic** |
| Save reference | ✅ Automatic | ✅ Automatic |

## Benefits

1. **Save Time**: No more manual conversion and printing
2. **No Mistakes**: Can't forget to convert or print
3. **Consistent**: Same format every time
4. **Organized**: All PDFs in one folder
5. **Professional**: Clean PDF format for records

## Support

If you encounter issues:
1. Check the log file: `Documents/DOPAgent/dop_reports_log.txt`
2. Verify all dependencies are installed
3. Make sure LibreOffice is accessible from command line
4. Check printer settings (grayscale mode)

## Version

**Version 2.0** - Automated PDF Generation & Printing
- Released: February 2026
- Adds: Automatic PDF conversion
- Adds: Automatic printing (2 copies)
- Adds: Excel file cleanup
- Improves: User experience

---

**Happy Automating! 🚀**
