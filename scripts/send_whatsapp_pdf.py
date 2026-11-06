#!/usr/bin/env python3
import sys
import os
import time
import ctypes
import subprocess
import argparse

# Load macOS Frameworks
app_services = ctypes.cdll.LoadLibrary('/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices')
core_foundation = ctypes.cdll.LoadLibrary('/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation')

kCFStringEncodingUTF8 = 0x08000100

CFStringCreateWithCString = core_foundation.CFStringCreateWithCString
CFStringCreateWithCString.restype = ctypes.c_void_p
CFStringCreateWithCString.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_uint32]

CFStringGetCString = core_foundation.CFStringGetCString
CFStringGetCString.restype = ctypes.c_bool
CFStringGetCString.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_long, ctypes.c_uint32]

CFArrayGetCount = core_foundation.CFArrayGetCount
CFArrayGetCount.restype = ctypes.c_long
CFArrayGetCount.argtypes = [ctypes.c_void_p]

CFArrayGetValueAtIndex = core_foundation.CFArrayGetValueAtIndex
CFArrayGetValueAtIndex.restype = ctypes.c_void_p
CFArrayGetValueAtIndex.argtypes = [ctypes.c_void_p, ctypes.c_long]

def cf_to_str(cf):
    if not cf:
        return ""
    buf = ctypes.create_string_buffer(512)
    CFStringGetCString(cf, buf, 512, kCFStringEncodingUTF8)
    return buf.value.decode('utf-8', errors='ignore')

def str_to_cf(s):
    return CFStringCreateWithCString(None, s.encode('utf-8'), kCFStringEncodingUTF8)

AXUIElementCreateApplication = app_services.AXUIElementCreateApplication
AXUIElementCreateApplication.restype = ctypes.c_void_p
AXUIElementCreateApplication.argtypes = [ctypes.c_int]

AXUIElementCopyAttributeValue = app_services.AXUIElementCopyAttributeValue
AXUIElementCopyAttributeValue.restype = ctypes.c_int
AXUIElementCopyAttributeValue.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.POINTER(ctypes.c_void_p)]

AXUIElementPerformAction = app_services.AXUIElementPerformAction
AXUIElementPerformAction.restype = ctypes.c_int
AXUIElementPerformAction.argtypes = [ctypes.c_void_p, ctypes.c_void_p]

def get_attr(elem, attr_name):
    cf_attr = str_to_cf(attr_name)
    val = ctypes.c_void_p()
    res = AXUIElementCopyAttributeValue(elem, cf_attr, ctypes.byref(val))
    if res == 0 and val.value:
        return val.value
    return None

def find_by_desc(elem, keyword, max_depth=12):
    if max_depth <= 0 or not elem:
        return None
    desc = cf_to_str(get_attr(elem, "AXDescription"))
    if keyword.lower() in desc.lower():
        return elem
    children_cf = get_attr(elem, "AXChildren")
    if children_cf:
        count = CFArrayGetCount(children_cf)
        for i in range(count):
            child = CFArrayGetValueAtIndex(children_cf, i)
            found = find_by_desc(child, keyword, max_depth - 1)
            if found:
                return found
    return None

def send_pdf(pdf_path, phone=None):
    if not os.path.isfile(pdf_path):
        print(f"Error: File not found {pdf_path}", file=sys.stderr)
        return False

    pdf_path = os.path.abspath(pdf_path)

    # 1. Open WhatsApp directly to the chat
    phone_clean = ''.join(c for c in (phone or '') if c.isdigit())
    if phone_clean:
        uri = f"whatsapp://send?phone={phone_clean}"
    else:
        uri = "whatsapp://send"
    
    subprocess.call(['open', uri])
    time.sleep(1.0)

    # 2. Get WhatsApp Process
    out = subprocess.check_output(['pgrep', '-x', 'WhatsApp']).decode().strip().split()
    if not out:
        print("WhatsApp is not running", file=sys.stderr)
        return False
    pid = int(out[0])
    app = AXUIElementCreateApplication(pid)

    def get_main_window():
        wins = get_attr(app, "AXWindows")
        if wins and CFArrayGetCount(wins) > 0:
            return CFArrayGetValueAtIndex(wins, 0)
        return None

    # 3. Find 'Share media' button
    win = get_main_window()
    share_btn = find_by_desc(win, "share media")
    if not share_btn:
        print("Could not find 'Share media' button in WhatsApp window", file=sys.stderr)
        return False

    AXUIElementPerformAction(share_btn, str_to_cf("AXPress"))
    time.sleep(0.4)

    # 4. Find 'File' button in the popover
    win = get_main_window()
    file_btn = find_by_desc(win, "file")
    if not file_btn:
        print("Could not find 'File' button in popover", file=sys.stderr)
        return False

    AXUIElementPerformAction(file_btn, str_to_cf("AXPress"))
    time.sleep(1.0)

    # 5. Handle macOS Open File Dialog using AppleScript
    # Press Cmd+Shift+G, enter file path, hit Enter, hit Enter
    script = f'''
    tell application "System Events"
        tell process "WhatsApp"
            keystroke "g" using {{command down, shift down}}
            delay 0.6
            keystroke "{pdf_path}"
            delay 0.4
            key code 36
            delay 0.8
            key code 36
        end tell
    end tell
    '''
    subprocess.call(['osascript', '-e', script])
    time.sleep(1.2)

    # 6. Click 'Send' button in the WhatsApp document preview
    win = get_main_window()
    send_btn = find_by_desc(win, "send")
    if send_btn:
        AXUIElementPerformAction(send_btn, str_to_cf("AXPress"))
        print("Successfully sent PDF via WhatsApp!")
        return True
    else:
        # Fallback: simulate Return key
        subprocess.call(['osascript', '-e', 'tell application "System Events" to tell process "WhatsApp" to key code 36'])
        print("Sent PDF via Return key fallback!")
        return True

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--pdf', required=True, help="Path to PDF")
    parser.add_argument('--phone', default=None, help="Phone number")
    args = parser.parse_args()
    success = send_pdf(args.pdf, args.phone)
    sys.exit(0 if success else 1)
