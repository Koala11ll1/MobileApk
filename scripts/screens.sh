#!/usr/bin/env bash
# Тимчасово: скріни кожної вкладки на екрані як у Galaxy S24 Ultra (1080×2340, 450 dpi). Видаляється після перегляду.
set -u
PKG=com.zarobitok.app
APK=$(ls ZarobitokApp/bin/Debug/net10.0-android/*-Signed.apk 2>/dev/null | head -1)
[ -z "$APK" ] && APK=$(ls ZarobitokApp/bin/Debug/net10.0-android/*.apk | head -1)
mkdir -p screens
adb shell wm size 1080x2340
adb shell wm density 450
adb install -r "$APK" >/dev/null || { echo "INSTALL FAILED"; exit 1; }

seed() {   # seed <theme>
  python3 - "$1" > /tmp/store.xml <<'PY'
import sys, json, uuid, datetime as d
theme = sys.argv[1]
now = d.datetime.now(d.timezone.utc).replace(microsecond=0)
fmt = lambda t: t.strftime('%Y-%m-%dT%H:%M:%SZ')
state = {"RatePerHour": 120, "Currency": "₴", "UnpaidBreakMinutes": 30, "PlannedHours": 8,
         "MonthlyGoal": 25000, "TaxPercent": 5, "Theme": theme, "Language": "uk",
         "StartedAtUtc": fmt(now - d.timedelta(hours=3, minutes=17))}
log = [{"StartedAtUtc": fmt(now - d.timedelta(days=i, hours=8, minutes=30)), "EndedAtUtc": fmt(now - d.timedelta(days=i)),
        "RatePerHour": 120, "Earned": 960} for i in range(1, 20) if i % 7 not in (5, 6)]
a, b = str(uuid.uuid4()), str(uuid.uuid4())
items = [{"Id": a, "Label": "Ред Бул", "UnitPrice": 25, "Archived": False},
         {"Id": b, "Label": "Доставка", "UnitPrice": 60, "Archived": False}]
ticks = [{"Id": str(uuid.uuid4()), "ItemId": a, "AtUtc": fmt(now - d.timedelta(minutes=10 * k)), "UnitPrice": 25} for k in range(1, 4)]
ticks += [{"Id": str(uuid.uuid4()), "ItemId": b, "AtUtc": fmt(now - d.timedelta(minutes=25))}]
j = lambda o: json.dumps(o, ensure_ascii=False, separators=(",", ":")).replace("&", "&amp;").replace("<", "&lt;")
print("<?xml version='1.0' encoding='utf-8' standalone='yes' ?>\n<map>")
for k, v in [("shift_state_json", state), ("shift_log_json", log), ("extra_items_json", items), ("extra_ticks_json", ticks)]:
    print('  <string name="%s">%s</string>' % (k, j(v)))
print("</map>")
PY
  adb shell am force-stop $PKG
  adb root >/dev/null; sleep 3; adb wait-for-device
  U=$(adb shell stat -c %u /data/data/$PKG | tr -d '\r')
  adb push /tmp/store.xml /data/local/tmp/store.xml >/dev/null
  adb shell "mkdir -p /data/data/$PKG/shared_prefs && cp /data/local/tmp/store.xml /data/data/$PKG/shared_prefs/zarobitok_shared_store.xml && chown -R $U:$U /data/data/$PKG/shared_prefs && chmod 660 /data/data/$PKG/shared_prefs/zarobitok_shared_store.xml && restorecon -R /data/data/$PKG/shared_prefs" >/dev/null 2>&1
  adb unroot >/dev/null; sleep 3; adb wait-for-device
}

dump() { adb shell uiautomator dump /sdcard/ui.xml >/dev/null 2>&1; adb pull /sdcard/ui.xml /tmp/ui.xml >/dev/null 2>&1; }
tap() {
  dump
  XY=$(python3 -c "
import sys,re,xml.etree.ElementTree as ET
for n in ET.parse('/tmp/ui.xml').iter('node'):
    if sys.argv[1] in (n.get('text'), n.get('content-desc')):
        a,b,c,d=map(int,re.match(r'\[(\d+),(\d+)\]\[(\d+),(\d+)\]',n.get('bounds')).groups()); print((a+c)//2,(b+d)//2); break
" "$1")
  [ -z "$XY" ] && { echo "NOT FOUND: $1"; return 1; }
  adb shell input tap $XY; sleep 4
}
shot() { adb exec-out screencap -p > "screens/$1.png"; echo "shot $1"; }

for theme in dark light; do
  seed $theme
  adb logcat -c
  adb shell monkey -p $PKG -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
  sleep 25
  shot "${theme}_1_shift"
  i=2
  for t in "Допзаробіток" "Журнал" "Статистика" "Налаштування"; do
    tap "$t"; shot "${theme}_${i}_$t"; i=$((i+1))
  done
  tap "Журнал" && tap "Календар" && shot "${theme}_6_calendar"
  adb shell input swipe 540 1700 540 500 300; sleep 1
  adb logcat -d > "screens/${theme}_logcat.txt"
  grep -q "FATAL EXCEPTION" "screens/${theme}_logcat.txt" && echo "CRASH in $theme"
done
dump; cp /tmp/ui.xml screens/last_ui.xml
exit 0
