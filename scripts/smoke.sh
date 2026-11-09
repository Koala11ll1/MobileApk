#!/usr/bin/env bash
# Запускається всередині Android-емулятора (див. .github/workflows/smoke-test.yml).
# 1) ставить APK; 2) кладе в сховище дані у СТАРОМУ форматі (як у реальному телефоні);
# 3) відкриває апку й по черзі відкриває кожну вкладку (тап за назвою через uiautomator);
# 4) шле дії ярликів; 5) друкує збої. Падає, якщо апка померла або показала вікно збою.
set -u

PKG=com.zarobitok.app
APK=$(ls ZarobitokApp/bin/Debug/net10.0-android/*.apk | head -1)
echo "APK: $APK"
adb install -r "$APK" || { echo "INSTALL FAILED"; exit 1; }
adb shell am force-stop $PKG

# ---- дані у старому форматі (без нових полів) ----
now=$(date -u +%s)
iso() { date -u -d "@$1" +%Y-%m-%dT%H:%M:%SZ; }
RUN_START=$(iso $((now - 3600)))
Y_S=$(iso $((now - 86400 - 8*3600))); Y_E=$(iso $((now - 86400)))
T_S=$(iso $((now - 3*86400 - 8*3600))); T_E=$(iso $((now - 3*86400)))
ITEM=11111111-1111-1111-1111-111111111111
cat > /tmp/store.xml <<XML
<?xml version='1.0' encoding='utf-8' standalone='yes' ?>
<map>
  <string name="shift_state_json">{"RatePerHour":120,"Currency":"₴","UnpaidBreakMinutes":30,"PlannedHours":8}</string>
  <string name="shift_log_json">[{"StartedAtUtc":"$Y_S","EndedAtUtc":"$Y_E","RatePerHour":120,"Earned":960},{"StartedAtUtc":"$T_S","EndedAtUtc":"$T_E","RatePerHour":100,"Earned":800}]</string>
  <string name="extra_items_json">[{"Id":"$ITEM","Label":"Red Bull","UnitPrice":60}]</string>
  <string name="extra_ticks_json">[{"Id":"22222222-2222-2222-2222-222222222222","ItemId":"$ITEM","AtUtc":"$(iso $((now - 1800)))","Delta":2},{"Id":"33333333-3333-3333-3333-333333333333","ItemId":"$ITEM","AtUtc":"$(iso $((now - 86400 - 3600)))","Delta":1}]</string>
</map>
XML
# Збірка не debuggable, тож run-as недоступний; образ google_apis дає root.
adb root >/dev/null; sleep 3
UID_APP=$(adb shell stat -c %u /data/data/$PKG | tr -d '\r')
adb push /tmp/store.xml /data/local/tmp/store.xml >/dev/null
adb shell "mkdir -p /data/data/$PKG/shared_prefs && cp /data/local/tmp/store.xml /data/data/$PKG/shared_prefs/zarobitok_shared_store.xml && chown -R $UID_APP:$UID_APP /data/data/$PKG/shared_prefs && chmod 660 /data/data/$PKG/shared_prefs/zarobitok_shared_store.xml && restorecon -R /data/data/$PKG/shared_prefs"
echo "seeded:"; adb shell ls -la /data/data/$PKG/shared_prefs
# uiautomator під root не працює — далі звичайний shell
adb unroot >/dev/null; sleep 3; adb wait-for-device

adb logcat -c
adb shell monkey -p $PKG -c android.intent.category.LAUNCHER 1
sleep 20

crash_dialog() {
  adb shell uiautomator dump /sdcard/ui.xml >/dev/null 2>&1
  adb pull /sdcard/ui.xml /tmp/ui.xml >/dev/null 2>&1
  if grep -q "Останній збій" /tmp/ui.xml; then
    echo "!!! CRASH DIALOG SHOWN:"
    python3 -c "
import re;t=open('/tmp/ui.xml',encoding='utf-8').read()
print(' | '.join(re.findall(r'text=\"([^\"]{3,})\"',t))[:3000])"
    return 0
  fi
  return 1
}

tap_text() {
  adb shell uiautomator dump /sdcard/ui.xml 2>&1 | head -3
  adb pull /sdcard/ui.xml /tmp/ui.xml >/dev/null 2>&1
  XY=$(python3 -c "
import sys,re,xml.etree.ElementTree as ET
for n in ET.parse('/tmp/ui.xml').iter('node'):
    if sys.argv[1] in (n.get('text'), n.get('content-desc')):
        a,b,c,d=map(int,re.match(r'\[(\d+),(\d+)\]\[(\d+),(\d+)\]',n.get('bounds')).groups())
        print((a+c)//2,(b+d)//2); break
" "$1")
  if [ -z "$XY" ]; then echo "tab '$1' NOT FOUND"; FAILED=1; return; fi
  echo "tap '$1' at $XY"
  adb shell input tap $XY
  sleep 4
}

FAILED=0
adb shell wm size
crash_dialog && FAILED=1

for tab in "Допзаробіток" "Журнал" "Статистика" "Налаштування" "Зміна"; do
  tap_text "$tab"
  adb shell pidof $PKG >/dev/null || { echo "APP DIED after tab '$tab'"; FAILED=1; break; }
  crash_dialog && FAILED=1
done

# Календар у журналі
tap_text "Журнал"; tap_text "Календар"; adb shell pidof $PKG >/dev/null || { echo "APP DIED in calendar"; FAILED=1; }
crash_dialog && FAILED=1

# Ярлики: почати зміну (екран тікає щосекунди, тож uiautomator далі не використовуємо), потім завершити
adb shell am start -a com.zarobitok.action.START_SHIFT -n $PKG/$PKG.MainActivity
sleep 8
adb shell pidof $PKG >/dev/null || { echo "APP DIED with a running shift"; FAILED=1; }
adb shell am start -a com.zarobitok.action.STOP_SHIFT -n $PKG/$PKG.MainActivity
sleep 4

echo "=== PID ==="
adb shell pidof $PKG || { echo "APP NOT RUNNING"; FAILED=1; }

echo "=== logcat: .NET / Java exceptions ==="
adb logcat -d | grep -E "FATAL EXCEPTION|Unhandled|XamlParse|System\.[A-Za-z.]+Exception|Java\.Lang\.[A-Za-z]+Exception|E AndroidRuntime|Process: $PKG|DOTNET|monodroid" | head -80

adb root >/dev/null; sleep 3; adb wait-for-device
echo "=== stored crash ==="
adb shell cat /data/data/$PKG/shared_prefs/zarobitok_shared_store.xml 2>/dev/null | grep -A12 'name="last_crash"' | head -40
echo '--- state after run:'; adb shell cat /data/data/$PKG/shared_prefs/zarobitok_shared_store.xml | cut -c1-400 | head -12

adb logcat -d | grep -q "FATAL EXCEPTION" && FAILED=1
if [ "$FAILED" = 0 ]; then echo "SMOKE OK"; else echo "SMOKE FAILED"; exit 1; fi
