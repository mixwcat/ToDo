#!/usr/bin/env bash
set -euo pipefail
package_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
[[ "$(uname -s)" == Linux ]] || { echo 'This installer is for Linux.' >&2; exit 1; }
[[ -f "$package_dir/YijianTodo" ]] || { echo 'Run install.sh from the extracted release package.' >&2; exit 1; }
install_dir="${XDG_DATA_HOME:-$HOME/.local/share}/yijian-todo-app"
applications_dir="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
[[ "$install_dir" = /* && "$applications_dir" = /* ]] || { echo 'XDG_DATA_HOME must be absolute.' >&2; exit 1; }
mkdir -p -- "$install_dir" "$applications_dir"
if [[ "$package_dir" != "$install_dir" ]]; then cp -a -- "$package_dir/." "$install_dir/"; fi
chmod 755 "$install_dir/YijianTodo"
# Escape Desktop Entry quoted arguments, including unusual home-directory names.
exec_path="$(printf '%s' "$install_dir/YijianTodo" | sed 's/\\/\\\\/g; s/"/\\"/g; s/`/\\`/g; s/\$/\\$/g; s/%/%%/g')"
icon_path="$install_dir/Assets/app.ico"
cat > "$applications_dir/yijian-todo.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Yijian Todo
Name[zh_CN]=一件 Todo
Comment=A quiet space for tasks and an AI companion
Exec="$exec_path"
Icon=$icon_path
Terminal=false
Categories=Office;Utility;
StartupNotify=true
StartupWMClass=YijianTodo
EOF
if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database "$applications_dir"; fi
printf 'Installed. Open Yijian Todo from the applications menu.\nPath: %s\n' "$install_dir/YijianTodo"
