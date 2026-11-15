#!/usr/bin/env python3
"""Cross-build a Debian package using .NET and the Python standard library."""
import argparse
import hashlib
import io
from pathlib import Path
import re
import subprocess
import tarfile
import tempfile


def tar_entry(archive, name, content, mode=0o644):
    entry = tarfile.TarInfo(name)
    entry.mode = mode
    entry.uid = entry.gid = 0
    entry.uname = entry.gname = 'root'
    entry.size = len(content)
    entry.mtime = 0
    archive.addfile(entry, io.BytesIO(content))


def ar_entry(stream, name, path):
    size = path.stat().st_size
    header = f'{name + "/":<16}{0:<12}{0:<6}{0:<6}{"100644":<8}{size:<10}`\n'
    assert len(header) == 60
    stream.write(header.encode('ascii'))
    with path.open('rb') as source:
        while chunk := source.read(1024 * 1024):
            stream.write(chunk)
    if size % 2:
        stream.write(b'\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', default='0.1.1')
    parser.add_argument('--runtime', choices=['linux-x64', 'linux-arm64'], default='linux-x64')
    parser.add_argument('--lite', action='store_true', help='Require the system .NET 8 runtime')
    args = parser.parse_args()
    if not re.fullmatch(r'\d+\.\d+\.\d+', args.version):
        parser.error('Use a version such as 0.1.0')
    root = Path(__file__).resolve().parent.parent
    arch = 'amd64' if args.runtime == 'linux-x64' else 'arm64'
    suffix = '-lite' if args.lite else ''
    destination = root / 'releases' / f'YijianTodo-{args.version}-ubuntu-{arch}{suffix}.deb'
    destination.parent.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='Yijian-deb-') as temporary:
        stage = Path(temporary)
        app = stage / 'app'
        subprocess.run([
            'dotnet', 'publish', str(root / 'ubuntu/src/Yijian.Ubuntu'), '-c', 'Release',
            '-r', args.runtime, '--self-contained', 'false' if args.lite else 'true',
            '-p:PublishSingleFile=true', '-p:PublishTrimmed=false', '-p:DebugType=None',
            '-p:DebugSymbols=false', f'-p:Version={args.version}', '-o', str(app), '--nologo'
        ], check=True)
        files = []
        for path in sorted(app.rglob('*')):
            if path.is_file():
                relative = path.relative_to(app).as_posix()
                files.append(('usr/lib/yijian-todo/' + relative, path.read_bytes(), 0o755 if relative == 'YijianTodo' or relative.endswith('.so') else 0o644))
        files.extend([
            ('usr/bin/yijian-todo', b'#!/bin/sh\nexec /usr/lib/yijian-todo/YijianTodo "$@"\n', 0o755),
            ('usr/share/applications/yijian-todo.desktop', '''[Desktop Entry]
Type=Application
Name=Yijian Todo
Name[zh_CN]=一件 Todo
Comment=Tasks, poems and an AI companion
Exec=/usr/bin/yijian-todo
Icon=yijian-todo
Terminal=false
Categories=Office;Utility;
StartupNotify=true
StartupWMClass=YijianTodo
'''.encode('utf-8'), 0o644),
            ('usr/share/icons/hicolor/256x256/apps/yijian-todo.png', (root / 'shared/Assets/app.png').read_bytes(), 0o644),
        ])
        installed_kb = (sum(len(data) for _, data, _ in files) + 1023) // 1024
        dependencies = [
            'libc6 (>= 2.35)', 'libgcc-s1', 'libstdc++6', 'zlib1g',
            'libicu70 | libicu74', 'libssl3 | libssl3t64', 'ca-certificates',
            'libx11-6', 'libice6', 'libsm6', 'libfontconfig1', 'libfreetype6',
            'libxext6', 'libxrender1', 'libxi6', 'libxrandr2', 'libxcursor1', 'libglib2.0-bin'
        ]
        if args.lite:
            dependencies.append('dotnet-runtime-8.0')
        control = f'''Package: yijian-todo
Version: {args.version}
Section: utils
Priority: optional
Architecture: {arch}
Maintainer: Yijian Todo contributors
Installed-Size: {installed_kb}
Depends: {', '.join(dependencies)}
Recommends: libsecret-tools, fonts-noto-cjk
Description: A desktop task manager with poems and an AI companion
 Hierarchical tasks, drag ordering, undo, Chinese and English interfaces,
 dark theme, local storage, tray integration and configurable AI tools.
 {'Uses the shared system .NET 8 runtime.' if args.lite else 'Includes a private .NET runtime; no separate .NET installation is required.'}
'''
        (stage / 'debian-binary').write_bytes(b'2.0\n')
        with tarfile.open(stage / 'data.tar.xz', 'w:xz', format=tarfile.GNU_FORMAT, preset=6) as archive:
            for name, data, mode in files:
                tar_entry(archive, './' + name, data, mode)
        with tarfile.open(stage / 'control.tar.xz', 'w:xz', format=tarfile.GNU_FORMAT, preset=6) as archive:
            tar_entry(archive, './control', control.encode('utf-8'))
            checksums = ''.join(f'{hashlib.md5(data).hexdigest()}  {name}\n' for name, data, _ in files)
            tar_entry(archive, './md5sums', checksums.encode('utf-8'))
        with destination.open('wb') as output:
            output.write(b'!<arch>\n')
            for member in ['debian-binary', 'control.tar.xz', 'data.tar.xz']:
                ar_entry(output, member, stage / member)
        print(f'Ready: {destination}')
        print(f'Package: {destination.stat().st_size:,} bytes; installed application: {installed_kb:,} KiB')


if __name__ == '__main__':
    main()
