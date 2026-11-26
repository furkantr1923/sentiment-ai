#!/usr/bin/env bash
# Android emülatör duman testi: APK kurulumu, açılış, çökme kontrolü, ekran görüntüsü.
APK="$1"; PKG=com.furkan.sentimentai
mkdir -p reports; R=reports/android-emulator-tests.txt
log() { echo "$1" | tee -a "$R"; }
pass=0; fail=0
check() { if eval "$2"; then log "[GEÇTİ] $1"; pass=$((pass+1)); else log "[BAŞARISIZ] $1"; fail=$((fail+1)); fi; }

log "SENTIMENT AI — Android emülatör testi (API 34, x86_64, Google APIs) — $(date -u '+%Y-%m-%d %H:%M') UTC"
check "APK kuruluyor" "adb install -r '$APK' >/dev/null"
adb logcat -c
adb shell monkey -p $PKG -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
sleep 25
check "Uygulama açılıyor ve çalışıyor (süreç canlı)" "[ -n \"\$(adb shell pidof $PKG)\" ]"
adb exec-out screencap -p > reports/android-ekran.png
adb shell uiautomator dump /sdcard/ui.xml >/dev/null 2>&1; adb pull /sdcard/ui.xml reports/ui.xml >/dev/null 2>&1
check "Ana ekran (logo/başlık) görünüyor" "grep -q 'SENTIMENT AI' reports/ui.xml"
check "Analiz Et butonu görünüyor" "grep -q 'Analiz Et' reports/ui.xml"
check "Çökme yok (FATAL EXCEPTION yok)" "! adb logcat -d | grep -q 'FATAL EXCEPTION'"
# Klavye açıkken alt çubuk görünür mü: metin alanına dokun, tekrar dump al
adb shell input tap 540 700; sleep 3
adb exec-out screencap -p > reports/android-klavye.png
adb shell uiautomator dump /sdcard/ui2.xml >/dev/null 2>&1; adb pull /sdcard/ui2.xml reports/ui2.xml >/dev/null 2>&1
check "Klavye açıkken Analiz Et butonu ekranda" "grep -q 'Analiz Et' reports/ui2.xml"
log "Toplam: geçti $pass, başarısız $fail"
log "Not: Emülatör testidir; gerçek fiziksel cihazda test yapılmamıştır."
exit 0
