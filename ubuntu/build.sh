#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
runtime=linux-x64
self_contained=true
while (($#)); do
  case "$1" in
    --runtime) runtime="${2:?Missing runtime}"; shift 2 ;;
    --framework-dependent) self_contained=false; shift ;;
    *) printf 'Unknown option: %s\n' "$1" >&2; exit 2 ;;
  esac
done
case "$runtime" in linux-x64|linux-arm64) ;; *) echo 'Use linux-x64 or linux-arm64.' >&2; exit 2 ;; esac
package="yijian-todo-$runtime"
stage="$(mktemp -d)"
trap 'rm -rf -- "$stage"' EXIT
dotnet publish "$script_dir/src/Yijian.Ubuntu" -c Release -r "$runtime" --self-contained "$self_contained" \
  -p:PublishSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o "$stage/$package" --nologo
cp -- "$script_dir/install.sh" "$stage/$package/install.sh"
chmod 755 "$stage/$package/YijianTodo" "$stage/$package/install.sh"
mkdir -p -- "$script_dir/dist"
tar -czf "$script_dir/dist/$package.tar.gz" -C "$stage" "$package"
# Replace only this script's generated package directory, never a user data directory.
destination="$script_dir/dist/$package"
case "$destination" in "$script_dir"/dist/yijian-todo-linux-*) ;; *) exit 2 ;; esac
if [[ -e "$destination" ]]; then rm -rf -- "$destination"; fi
mv -- "$stage/$package" "$destination"
printf '\nReady: %s/YijianTodo\nArchive: %s/dist/%s.tar.gz\n' "$destination" "$script_dir" "$package"
