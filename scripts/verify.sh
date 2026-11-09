#!/usr/bin/env bash
# Тимчасова перевірка в емуляторі: тема, мова, замок PIN. Видається після перевірки.
set -u
PKG=com.zarobitok.app
APK=$(find ZarobitokApp/bin/Release -name "*-Signed.apk" | head -1)
echo "APK under test: $APK"
FAILED=0
pass() { echo "PASS: $1"; }
diag() {
  echo "  --- diag: pid=$(adb shell pidof $PKG | tr -d '\r') top=$(adb shell dumpsys activity activities | grep -m1 'topResumedActivity' | cut -c1-140)"
  dump
  echo "  --- visible texts: $(python3 -c "
import re;t=open('/tmp/ui.xml',encoding='utf-8').read()
v=re.findall(r'(?:text|content-desc)=\"([^\"]{2,40})\"',t); print(' | '.join(dict.fromkeys(v))[:600])")"
  adb logcat -d | grep -E "ANR in|FATAL EXCEPTION|am_anr|Force finishing|zarobitok.*(Exception|died)" | tail -5
}
fail() { echo "FAIL: $1"; FAILED=1; diag; }

adb install -r "$APK" >/dev/null || { echo "INSTALL FAILED"; exit 1; }
pip install -q pillow >/dev/null 2>&1

seed() {   # seed <theme> <lang> [pin] [crash]
  python3 - "$1" "$2" "${3:-}" "${4:-}" > /tmp/store.xml <<'PY'
import sys, json, hashlib, base64, os, uuid, datetime as d
theme, lang, pin, crash = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
now = d.datetime.now(d.timezone.utc)
fmt = lambda t: t.strftime('%Y-%m-%dT%H:%M:%SZ')
state = {"RatePerHour": 120, "Currency": "₴", "UnpaidBreakMinutes": 30, "PlannedHours": 8, "Theme": theme, "Language": lang}
log = [{"StartedAtUtc": fmt(now - d.timedelta(days=i, hours=8)), "EndedAtUtc": fmt(now - d.timedelta(days=i)), "RatePerHour": 120, "Earned": 960} for i in range(1, 20)]
j = lambda o: json.dumps(o, ensure_ascii=False, separators=(",", ":"))
print("<?xml version='1.0' encoding='utf-8' standalone='yes' ?>\n<map>")
print('  <string name="shift_state_json">%s</string>' % j(state))
print('  <string name="shift_log_json">%s</string>' % j(log))
if crash:
    entry = [{"AtUtc": fmt(now), "AppVersion": "1.1 (1)", "Device": "Test Device", "Os": "14 (API 34)", "Stack": "System.InvalidOperationException: seeded crash for the test", "Seen": False}]
    print('  <string name="crash_log_json">%s</string>' % j(entry))
if pin:
    salt = os.urandom(16)
    h = hashlib.pbkdf2_hmac('sha256', pin.encode(), salt, 60000, 32)
    print('  <string name="lock_pin_salt">%s</string>' % base64.b64encode(salt).decode())
    print('  <string name="lock_pin_hash">%s</string>' % base64.b64encode(h).decode())
    print('  <string name="lock_pin_len">%d</string>' % len(pin))
    print('  <string name="lock_grace_min">0</string>')   # блокувати одразу після повернення з фону
print("</map>")
PY
  adb shell am force-stop $PKG
  adb root >/dev/null; sleep 3; adb wait-for-device
  U=$(adb shell stat -c %u /data/data/$PKG | tr -d '\r')
  adb push /tmp/store.xml /data/local/tmp/store.xml >/dev/null
  adb shell "mkdir -p /data/data/$PKG/shared_prefs && cp /data/local/tmp/store.xml /data/data/$PKG/shared_prefs/zarobitok_shared_store.xml && chown -R $U:$U /data/data/$PKG/shared_prefs && chmod 660 /data/data/$PKG/shared_prefs/zarobitok_shared_store.xml && restorecon -R /data/data/$PKG/shared_prefs" >/dev/null 2>&1
  adb unroot >/dev/null; sleep 3; adb wait-for-device
}

launch() { adb logcat -c; adb shell monkey -p $PKG -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1; sleep 14; }

dump() { adb shell uiautomator dump /sdcard/ui.xml >/dev/null 2>&1; adb pull /sdcard/ui.xml /tmp/ui.xml >/dev/null 2>&1; }
# Заголовки вкладок Android віддає як content-desc, решту — як text.
has() { dump; grep -qF "text=\"$1\"" /tmp/ui.xml || grep -qF "content-desc=\"$1\"" /tmp/ui.xml; }

# Чекаємо, поки текст з'явиться (Debug-збірка на емуляторі стартує повільно): до ~45 с.
wait_has() { for _ in $(seq 1 15); do has "$1" && return 0; sleep 3; done; return 1; }

tap() {  # tap <text>
  dump
  XY=$(python3 -c "
import sys,re,xml.etree.ElementTree as ET
for n in ET.parse('/tmp/ui.xml').iter('node'):
    if n.get('text')==sys.argv[1] or n.get('content-desc')==sys.argv[1]:
        a,b,c,d=map(int,re.match(r'\[(\d+),(\d+)\]\[(\d+),(\d+)\]',n.get('bounds')).groups()); print((a+c)//2,(b+d)//2); break
" "$1")
  [ -z "$XY" ] && return 1
  adb shell input tap $XY; sleep 3; return 0
}

bg_luma() {  # luminance of the page background (left margin)
  adb exec-out screencap -p > /tmp/s.png
  python3 -c "
from PIL import Image
im=Image.open('/tmp/s.png').convert('RGB'); w,h=im.size
r,g,b=im.getpixel((6,int(h*0.55))); print(int(0.2126*r+0.7152*g+0.0722*b))"
}

crashed() { adb logcat -d | grep -q "FATAL EXCEPTION" || has "Останній збій застосунку" || has "Last app crash"; }

echo "=== 1. Default: Ukrainian + dark ==="
seed dark uk; launch
wait_has "Допзаробіток" && pass "Ukrainian tab titles" || fail "Ukrainian tab titles"
L=$(bg_luma); echo "luma=$L"; [ "$L" -lt 60 ] && pass "dark background" || fail "dark background (luma=$L)"
crashed && fail "crash in scenario 1" || pass "no crash"

echo "=== 2. Light + English from saved settings ==="
seed light en; launch
wait_has "Journal" && pass "English tab titles" || fail "English tab titles"
has "Журнал" && fail "Ukrainian text leaked into English UI" || pass "no Ukrainian tab titles"
L=$(bg_luma); echo "luma=$L"; [ "$L" -gt 200 ] && pass "light background" || fail "light background (luma=$L)"
tap "Statistics"; has "LAST 7 DAYS" && pass "Stats page in English" || fail "Stats page in English"
tap "Journal"; has "Calendar" && pass "Journal in English" || fail "Journal in English"
crashed && fail "crash in scenario 2" || pass "no crash"

echo "=== 3. PIN lock (Ukrainian) ==="
seed dark uk 1234; launch
has "Введіть PIN" && pass "lock screen shown" || fail "lock screen shown"
has "Допзаробіток" && fail "app content visible behind the lock" || pass "content hidden while locked"
for d in 0 0 0 0; do tap "$d"; done
has "Невірний PIN" && pass "wrong PIN rejected" || fail "wrong PIN rejected"
has "Допзаробіток" && fail "unlocked with a wrong PIN" || pass "still locked after wrong PIN"
for d in 1 2 3 4; do tap "$d"; done
wait_has "Допзаробіток" && pass "right PIN unlocks the app" || fail "right PIN unlocks the app"
adb shell input keyevent KEYCODE_HOME; sleep 3
adb shell monkey -p $PKG -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
wait_has "Введіть PIN" && pass "locks again after returning from background" || fail "locks again after returning from background"
crashed && fail "crash in scenario 3" || pass "no crash"

echo "=== 4. PIN lock (English) ==="
seed dark en 4321; launch
has "Enter PIN" && pass "English lock screen" || fail "English lock screen"

echo "=== 5. Live language and theme switch in Settings ==="
seed dark uk; launch
tap "Налаштування"
for i in 1 2 3 4 5 6 7; do has "Українська" && break; adb shell input swipe 540 1800 540 700 300; sleep 1; done
tap "Українська" && tap "English"
sleep 4
wait_has "SHIFT SETTINGS" && pass "UI recreated in English and opened on Settings" || fail "UI recreated in English and opened on Settings"
has "Налаштування" && fail "Ukrainian title remained after switching language" || pass "no Ukrainian title"
for i in 1 2 3 4 5 6 7; do has "Dark" && break; adb shell input swipe 540 1800 540 700 300; sleep 1; done
tap "Dark" && tap "Light"
sleep 3
L=$(bg_luma); echo "luma=$L"; [ "$L" -gt 200 ] && pass "theme switched to light live" || fail "theme switched to light live (luma=$L)"
crashed && fail "crash in scenario 5" || pass "no crash"


echo "=== 6. Version info from the build ==="
seed dark uk; launch
dumpsys_out=$(adb shell dumpsys package $PKG | grep -E "versionName|versionCode" | head -3 | tr '\n' ' ')
echo "$dumpsys_out"
echo "$dumpsys_out" | grep -q "versionName=9.9.9" && pass "versionName comes from the build property" || fail "versionName comes from the build property"
CODE=$(echo "$dumpsys_out" | grep -oE "versionCode=[0-9]+" | head -1 | cut -d= -f2)
[ "${CODE:-0}" -gt 100000 ] && pass "versionCode is the monotonic build number ($CODE)" || fail "versionCode is the monotonic build number ($CODE)"
wait_has "Налаштування"
tap "Налаштування"
for i in 1 2 3 4 5 6 7 8 9 10 11 12; do has "Збоїв не було" && break; adb shell input swipe 540 1800 540 600 300; sleep 1; done
has "Збоїв не було" && pass "no crashes recorded on a clean install" || fail "no crashes recorded on a clean install"
dump; grep -qF "9.9.9 (" /tmp/ui.xml && pass "About card shows the version" || fail "About card shows the version"

echo "=== 7. Crash log dialog and Settings ==="
seed dark uk "" crash; launch
wait_has "Надіслати звіт" && pass "crash dialog offers to send a report" || fail "crash dialog offers to send a report"
has "Закрити" && tap "Закрити"
wait_has "Допзаробіток" && pass "app continues after closing the crash dialog" || fail "app continues after closing the crash dialog"
tap "Налаштування"
for i in 1 2 3 4 5 6 7 8 9 10; do has "Надіслати звіт про збої" && break; adb shell input swipe 540 1800 540 600 300; sleep 1; done
has "Надіслати звіт про збої" && pass "Settings lists the crash report actions" || fail "Settings lists the crash report actions"
dump; grep -q "Збоїв зафіксовано: 1" /tmp/ui.xml && pass "Settings counts the recorded crash" || fail "Settings counts the recorded crash"
launch_again() { :; }
adb shell am force-stop $PKG; launch
wait_has "Допзаробіток"
has "Останній збій застосунку" && fail "the crash dialog is shown only once" || pass "the crash dialog is shown only once"

echo "=== 8. PDF report ==="
sudo apt-get install -y -q poppler-utils >/dev/null 2>&1
seed dark uk; launch
wait_has "Статистика" && tap "Статистика"
for i in 1 2 3 4 5 6; do has "Звіт за період (PDF)" && break; adb shell input swipe 540 1800 540 600 300; sleep 1; done
tap "Звіт за період (PDF)" && pass "report button found" || fail "report button found"
tap "Цей рік" && pass "period chosen" || fail "period chosen"
sleep 8
adb root >/dev/null; sleep 3; adb wait-for-device
PDF=$(adb shell "ls /data/data/$PKG/cache/zarobitok-report-*.pdf 2>/dev/null" | tr -d '\r' | head -1)
echo "pdf: $PDF"
if [ -n "$PDF" ]; then
  adb pull "$PDF" /tmp/report.pdf >/dev/null
  head -c 5 /tmp/report.pdf | grep -q "%PDF" && pass "file is a PDF" || fail "file is a PDF"
  pdfinfo /tmp/report.pdf | grep -E "Pages|Page size" 
  pdftotext -layout /tmp/report.pdf /tmp/report.txt
  head -40 /tmp/report.txt
  grep -q "Звіт про заробіток" /tmp/report.txt && pass "PDF has the Ukrainian title (Cyrillic renders)" || fail "PDF has the Ukrainian title"
  grep -q "18240.00" /tmp/report.txt && pass "PDF total is 18240.00 (19 shifts x 960)" || fail "PDF total is 18240.00"
  grep -q "По змінах" /tmp/report.txt && pass "PDF has the shifts table" || fail "PDF has the shifts table"
  grep -q "Стор. 1 з" /tmp/report.txt && pass "PDF has page numbers" || fail "PDF has page numbers"
else
  fail "PDF file was created"
fi
adb unroot >/dev/null; sleep 3; adb wait-for-device
crashed && fail "crash while building the report" || pass "no crash while building the report"

echo "=== logcat exceptions (with stack) ==="
adb logcat -d | grep -A45 "FATAL EXCEPTION" | head -120
adb logcat -d | grep -E "XamlParse|Unhandled" | head -10

[ "$FAILED" = 0 ] && echo "VERIFY OK" || { echo "VERIFY FAILED"; exit 1; }
