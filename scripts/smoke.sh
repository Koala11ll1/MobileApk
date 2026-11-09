#!/usr/bin/env bash
# Запускається всередині Android-емулятора (див. .github/workflows/smoke-test.yml).
# Ставить APK, відкриває апку, клікає по всіх вкладках, шле дію ярлика
# і друкує з logcat усе, що схоже на збій. Падає, якщо апка померла.
set -u

APK=$(ls ZarobitokApp/bin/Debug/net10.0-android/*.apk | head -1)
echo "APK: $APK"
adb install -r "$APK" || { echo "INSTALL FAILED"; exit 1; }

adb logcat -c
adb shell monkey -p com.zarobitok.app -c android.intent.category.LAUNCHER 1
sleep 20

read -r W H < <(adb shell wm size | tail -1 | sed -E 's/.*: ([0-9]+)x([0-9]+).*/\1 \2/')
echo "screen ${W}x${H}"
Y=$((H - H / 25))

# 5 вкладок знизу: Зміна, Допзаробіток, Журнал, Статистика, Налаштування
for i in 0 1 2 3 4 3 2 1 0; do
  X=$(( (2 * i + 1) * W / 10 ))
  adb shell input tap "$X" "$Y"
  sleep 3
done

# Ярлики: почати і завершити зміну
adb shell am start -a com.zarobitok.action.START_SHIFT -n com.zarobitok.app/com.zarobitok.app.MainActivity
sleep 4
adb shell am start -a com.zarobitok.action.STOP_SHIFT -n com.zarobitok.app/com.zarobitok.app.MainActivity
sleep 4

echo "=== PID ==="
adb shell pidof com.zarobitok.app || echo "APP NOT RUNNING"

echo "=== logcat: errors ==="
adb logcat -d | grep -E "FATAL EXCEPTION|AndroidRuntime|Unhandled|XamlParse|System\.[A-Za-z]+Exception|Java\.Lang|SecurityException|zarobitok" | head -120

echo "=== stored crash (SharedPreferences) ==="
adb shell run-as com.zarobitok.app cat shared_prefs/zarobitok_shared_store.xml 2>/dev/null | grep -A20 last_crash | head -60 || true

if ! adb shell pidof com.zarobitok.app >/dev/null; then exit 1; fi
if adb logcat -d | grep -q "FATAL EXCEPTION"; then exit 1; fi
echo "SMOKE OK"
