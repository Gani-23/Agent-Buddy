import os
import sys
from xml.sax.saxutils import escape


SKIP_DIRS = {"__pycache__", ".venv"}
SKIP_FILES = {".DS_Store"}


def collect_tree(root_dir: str):
    files_by_dir = {}
    tree = {}
    for dirpath, dirnames, filenames in os.walk(root_dir):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        rel_dir = os.path.relpath(dirpath, root_dir)
        if rel_dir == ".":
            rel_dir = ""

        node = tree
        if rel_dir:
            for part in rel_dir.split(os.sep):
                node = node.setdefault(part, {})
        for d in dirnames:
            node.setdefault(d, {})

        file_list = []
        for name in filenames:
            if name in SKIP_FILES:
                continue
            file_list.append(name)
        if file_list:
            files_by_dir[rel_dir] = sorted(file_list)

    return tree, files_by_dir


def generate_wxs(root_dir: str, out_path: str):
    tree, files_by_dir = collect_tree(root_dir)

    dir_id_map = {"": "INSTALLFOLDER"}
    dir_counter = 1
    comp_counter = 1

    lines = []
    lines.append('<?xml version="1.0" encoding="UTF-8"?>')
    lines.append('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"')
    lines.append('     xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui">')
    lines.append('  <Package Name="AgentBuddy"')
    lines.append('           Manufacturer="AgentBuddy"')
    lines.append('           Version="1.0.0"')
    lines.append('           InstallScope="perMachine"')
    lines.append('           UpgradeCode="6B5C94C5-9E3A-46A7-B3A4-0F6F5D8C7E9B">')
    lines.append('    <MajorUpgrade DowngradeErrorMessage="A newer version of [ProductName] is already installed." />')
    lines.append('    <MediaTemplate />')
    lines.append('    <Property Id="ARPPRODUCTICON" Value="AgentBuddyIcon" />')
    lines.append('    <Feature Id="MainFeature" Title="AgentBuddy" Level="1">')
    lines.append('      <ComponentGroupRef Id="AppComponents" />')
    lines.append('    </Feature>')
    lines.append('    <ui:WixUI Id="WixUI_Minimal" />')
    lines.append('  </Package>')
    lines.append('')
    lines.append('  <Fragment>')
    lines.append('    <StandardDirectory Id="ProgramFilesFolder">')
    lines.append('      <Directory Id="INSTALLFOLDER" Name="AgentBuddy" />')
    lines.append('    </StandardDirectory>')
    lines.append('    <StandardDirectory Id="CommonDesktopFolder" />')
    lines.append('  </Fragment>')
    lines.append('')
    lines.append('  <Fragment>')
    lines.append('    <Icon Id="AgentBuddyIcon" SourceFile="$(var.SourceDir)/AgentBuddy.ico" />')
    lines.append('  </Fragment>')
    lines.append('')

    lines.append('  <Fragment>')
    lines.append('    <DirectoryRef Id="INSTALLFOLDER">')

    def emit_directory(node, rel_dir, indent):
        nonlocal dir_counter, comp_counter
        files = files_by_dir.get(rel_dir, [])
        if rel_dir == "":
            current_indent = indent
            for name in files:
                comp_id = f"cmp_{comp_counter}"
                file_id = f"fil_{comp_counter}"
                comp_counter += 1
                rel_path = name.replace(os.sep, "/")
                src = f'$(var.SourceDir)/{rel_path}'
                lines.append(f'{current_indent}<Component Id="{comp_id}" Guid="*">')
                lines.append(f'{current_indent}  <File Id="{file_id}" Source="{escape(src)}" />')
                lines.append(f'{current_indent}</Component>')
        for subdir, child in sorted(node.items()):
            dir_id = f"dir_{dir_counter}"
            dir_counter += 1
            sub_rel = f"{rel_dir}/{subdir}" if rel_dir else subdir
            dir_id_map[sub_rel] = dir_id
            lines.append(f'{indent}<Directory Id="{dir_id}" Name="{escape(subdir)}">')
            emit_directory(child, sub_rel, indent + "  ")
            lines.append(f'{indent}</Directory>')

    emit_directory(tree, "", "      ")
    lines.append('    </DirectoryRef>')
    lines.append('  </Fragment>')
    lines.append('')

    lines.append('  <Fragment>')
    lines.append('    <DirectoryRef Id="CommonDesktopFolder">')
    lines.append('      <Component Id="DesktopShortcutCmp" Guid="*">')
    lines.append('        <Shortcut Id="DesktopShortcut"')
    lines.append('                  Name="AgentBuddy"')
    lines.append('                  Target="[INSTALLFOLDER]AgentBuddy.exe"')
    lines.append('                  WorkingDirectory="INSTALLFOLDER" />')
    lines.append('        <RegistryValue Root="HKLM"')
    lines.append('                      Key="Software\\AgentBuddy"')
    lines.append('                      Name="installed"')
    lines.append('                      Type="integer"')
    lines.append('                      Value="1"')
    lines.append('                      KeyPath="yes" />')
    lines.append('      </Component>')
    lines.append('    </DirectoryRef>')
    lines.append('  </Fragment>')
    lines.append('')

    lines.append('  <Fragment>')
    lines.append('    <ComponentGroup Id="AppComponents">')
    for i in range(1, comp_counter):
        lines.append(f'      <ComponentRef Id="cmp_{i}" />')
    lines.append('      <ComponentRef Id="DesktopShortcutCmp" />')
    lines.append('    </ComponentGroup>')
    lines.append('  </Fragment>')
    lines.append('</Wix>')

    with open(out_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))


def main():
    if len(sys.argv) != 3:
        print("Usage: python generate_wix.py <staging_dir> <output_wxs>")
        sys.exit(1)
    staging_dir = os.path.abspath(sys.argv[1])
    output_wxs = os.path.abspath(sys.argv[2])
    generate_wxs(staging_dir, output_wxs)
    print(f"Wrote {output_wxs}")


if __name__ == "__main__":
    main()
