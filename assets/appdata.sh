# ADB Install: backs up and restores one app's data on the phone.
# Pushed to /data/local/tmp and run with sh: as root for the "in" parts, as the shell user for "ext".
#
#   sh appdata.sh backup  in|ext <package> <dir>   writes one <part>.tar per part into <dir>
#   sh appdata.sh restore in|ext <package> <dir>   extracts them back and fixes owner, SELinux label and quota
#
# Parts: data and user_de are the app's private storage (root only). ext, obb and media are its folders under
# Android/ in shared storage. Up to Android 10 those go through /sdcard, which gives new files the right owner by
# itself, so the shell user handles them. From Android 11, Android/data and Android/obb live directly on
# /data/media and apps reach them without a translating layer, so root handles them there and gives restored files
# the owner, group and quota project that Android would. Android/media always goes through /sdcard (MediaProvider).
# Each part prints one line: "OK <part>", "SKIP <part>" (nothing to do), "WARN <part> <message>" (done, with
# complaints) or "ERR <part> <message>".

action=$1 kind=$2 pkg=$3 dir=$4
case "$pkg" in ''|*[!A-Za-z0-9._]*) echo "ERR all bad package name"; exit 1 ;; esac
case "$dir" in /data/local/tmp/*) ;; *) echo "ERR all bad folder"; exit 1 ;; esac

TAR=
for t in tar "toybox tar" "busybox tar" "/data/adb/magisk/busybox tar" "/data/adb/ksu/bin/busybox tar" "/data/adb/ap/bin/busybox tar"; do
  if $t -cf /dev/null "$0" 2>/dev/null; then TAR=$t; break; fi
done
[ -n "$TAR" ] || { echo "ERR all no tar"; exit 1; }

sdk=$(getprop ro.build.version.sdk)
lower=/data/media/0/Android
if [ "${sdk:-0}" -ge 30 ]; then
  if [ $kind = in ]; then parts="data user_de ext obb"; else parts="media"; fi
else
  if [ $kind = in ]; then parts="data user_de"; else parts="ext obb media"; fi
fi

dir_of() {
  case $1 in
    data) echo /data/data/$pkg ;;
    user_de) echo /data/user_de/0/$pkg ;;
    ext|obb)
      if [ $kind = in ]; then shared=$lower; else shared=/sdcard/Android; fi
      [ $1 = ext ] && echo $shared/data/$pkg || echo $shared/obb/$pkg ;;
    media) echo /sdcard/Android/media/$pkg ;;
  esac
}

# Folders Android creates and manages itself: caches and the native library link.
managed() { # part name
  case "$1:$2" in data:cache|data:code_cache|data:lib|user_de:cache|user_de:code_cache|ext:cache) return 0 ;; esac
  return 1
}

uid_of() {
  while read n u rest; do
    [ "$n" = "$pkg" ] && { echo $u; return; }
  done < /data/system/packages.list
}

context_of() {
  for t in $(ls -Zd "$1" 2>/dev/null); do
    case "$t" in *:object_r:*) echo "$t"; return ;; esac
  done
}

# chown -R for toolboxes whose chown has no -R.
own() { # owner file
  local c
  chown $1 "$2"
  [ -d "$2" ] && [ ! -L "$2" ] || return
  for c in "$2"/* "$2"/.*; do
    case "${c##*/}" in .|..) continue ;; esac
    [ -e "$c" ] || [ -L "$c" ] || continue
    own $1 "$c"
  done
}

report() { # part errfile
  if [ -s "$2" ]; then echo "WARN $1 $(cat "$2")"; else echo "OK $1"; fi
  rm -f "$2"
}

backup() {
  local part=$1 src out=$dir/$1.tar err=$dir/$1.err f
  src=$(dir_of $part)
  rm -f "$out"
  [ -d "$src" ] || { echo "SKIP $part"; return; }
  cd "$src" 2>/dev/null || { echo "ERR $part cannot open $src"; return; }
  set --
  for f in * .*; do
    case "$f" in .|..) continue ;; esac
    managed $part "$f" && continue
    [ -e "$f" ] || [ -L "$f" ] || continue
    set -- "$@" "$f"
  done
  [ $# -gt 0 ] || { echo "SKIP $part"; return; }
  $TAR -cf "$out" "$@" 2>"$err"
  if [ -s "$out" ]; then chmod 644 "$out"; report $part "$err"; else echo "ERR $part $(cat "$err")"; rm -f "$err"; fi
}

restore() {
  local part=$1 dst file=$dir/$1.tar err=$dir/$1.err uid ctx f gid project
  [ -f "$file" ] || { echo "SKIP $part"; return; }
  dst=$(dir_of $part)
  case $part in
    data|user_de)
      [ $part = user_de ] && [ ! -d /data/user_de ] && { echo "SKIP $part"; return; }
      cd "$dst" 2>/dev/null || { echo "ERR $part missing $dst"; return; }
      uid=$(uid_of)
      [ -n "$uid" ] || { echo "ERR $part unknown uid"; return; }
      ctx=$(context_of "$dst")
      # Start clean, so nothing the fresh install created mixes with the restored databases.
      for f in * .*; do
        case "$f" in .|..) continue ;; esac
        managed $part "$f" || rm -rf "$f"
      done
      ;;
    *)
      mkdir -p "$dst" 2>/dev/null
      cd "$dst" 2>/dev/null || { echo "ERR $part cannot create $dst"; return; }
      ;;
  esac
  $TAR -xf "$file" 2>"$err" || { echo "ERR $part $(cat "$err")"; rm -f "$err"; return; }
  case $part in
    data|user_de)
      for f in * .*; do
        case "$f" in .|..) continue ;; esac
        managed $part "$f" && continue
        [ -e "$f" ] || [ -L "$f" ] || continue
        chown -R $uid:$uid "$f" 2>/dev/null || own $uid:$uid "$f"
        [ -n "$ctx" ] && chcon -R "$ctx" "$f" 2>/dev/null
      done
      [ -n "$ctx" ] || restorecon -R "$dst" 2>/dev/null
      ;;
    ext|obb)
      if [ $kind = in ]; then
        # What vold gives an app folder: the app as owner, ext_data_rw / ext_obb_rw as group, a quota project
        # per app (20000 / 40000 + app id) inherited by new files, and the label of shared storage.
        uid=$(uid_of)
        [ -n "$uid" ] || { echo "ERR $part unknown uid"; return; }
        if [ $part = ext ]; then gid=1078 project=$((20000 + uid % 100000 - 10000)); else gid=1079 project=$((40000 + uid % 100000 - 10000)); fi
        chown -R $uid:$gid "$dst"
        chmod 2770 "$dst"
        chattr -R -p $project "$dst" 2>/dev/null
        find "$dst" -type d -exec chattr +P {} + 2>/dev/null
        ctx=$(context_of "${dst%/*}")
        [ -n "$ctx" ] && chcon -R "$ctx" "$dst" 2>/dev/null
      fi
      ;;
  esac
  report $part "$err"
}

for p in $parts; do
  case $action in
    backup) backup $p ;;
    restore) restore $p ;;
  esac
done
