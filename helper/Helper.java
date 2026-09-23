package com.adbinstall;

import android.content.Context;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.drawable.Drawable;
import android.os.Build;
import android.os.Looper;
import android.util.Base64;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileDescriptor;
import java.io.FileOutputStream;
import java.io.PrintStream;

/**
 * Runs on the device through app_process (like scrcpy's server) and prints what adb alone can't give:
 * app labels in the device language, icons, install dates and APK sizes.
 *
 *   CLASSPATH=/data/local/tmp/adbinstall.dex app_process / com.adbinstall.Helper list
 *   ... com.adbinstall.Helper icons [user]
 *
 * Output is tab-separated, one line per app.
 */
public class Helper {
    public static void main(String[] args) throws Exception {
        PrintStream out = new PrintStream(new FileOutputStream(FileDescriptor.out), false, "UTF-8");
        Looper.prepareMainLooper();
        Class<?> at = Class.forName("android.app.ActivityThread");
        Object thread = at.getMethod("systemMain").invoke(null);
        Context ctx = (Context) at.getMethod("getSystemContext").invoke(thread);
        PackageManager pm = ctx.getPackageManager();

        String mode = args.length > 0 ? args[0] : "list";
        if (mode.equals("kblayout")) {
            keyboardLayout(out, args.length > 1 ? args[1] : "hebrew");
            out.flush();
            System.exit(0);
        }
        if (mode.equals("kbserve")) {
            // Stays running while mirroring: each stdin line ("hebrew" / "english" / "sample") is applied at once,
            // so switching language on the PC switches the phone without starting a new process.
            java.io.BufferedReader in = new java.io.BufferedReader(new java.io.InputStreamReader(System.in, "UTF-8"));
            String line;
            while ((line = in.readLine()) != null) {
                line = line.replace("﻿", "").trim(); // a writer may prepend a byte-order mark
                if (line.equals("exit")) break;
                try {
                    if (line.equals("sample")) out.println("S\t" + sampleA());
                    else if (line.length() > 0) keyboardLayout(out, line);
                } catch (Throwable t) {
                    out.println("E\t" + t);
                }
                out.flush();
            }
            System.exit(0);
        }
        boolean userOnly = args.length > 1 && args[1].equals("user");
        for (PackageInfo p : pm.getInstalledPackages(0)) {
            ApplicationInfo ai = p.applicationInfo;
            if (ai == null) continue;
            boolean system = (ai.flags & ApplicationInfo.FLAG_SYSTEM) != 0;
            if (userOnly && system) continue;
            try {
                if (mode.equals("icons")) {
                    out.println("I\t" + p.packageName + "\t" + png(icon(pm, ai), 96));
                    continue;
                }
                String label;
                try { label = String.valueOf(ai.loadLabel(pm)); } catch (Throwable t) { label = p.packageName; }
                long size = new File(ai.sourceDir).length();
                int splits = 0;
                if (Build.VERSION.SDK_INT >= 21 && ai.splitSourceDirs != null) {
                    splits = ai.splitSourceDirs.length;
                    for (String s : ai.splitSourceDirs) size += new File(s).length();
                }
                long code = Build.VERSION.SDK_INT >= 28 ? p.getLongVersionCode() : p.versionCode;
                out.println("P\t" + p.packageName + "\t" + clean(label) + "\t" + clean(p.versionName) + "\t" + code
                        + "\t" + p.firstInstallTime + "\t" + p.lastUpdateTime + "\t" + size + "\t" + (system ? 1 : 0)
                        + "\t" + (ai.enabled ? 1 : 0) + "\t" + splits);
            } catch (Throwable t) {
                // One broken package must not stop the list.
            }
        }
        out.flush();
        System.exit(0);
    }

    /**
     * Gives scrcpy's virtual keyboard (UHID, named "scrcpy") the English + Hebrew layouts, so Ctrl+Space
     * switches between them. Without this Android uses the generic English map and Hebrew can't be typed.
     * Uses hidden InputManager APIs (Android 5-13); the shell holds SET_KEYBOARD_LAYOUT.
     * Prints "K\t<n devices configured>\t<detail>".
     */
    static void keyboardLayout(PrintStream out, String current) throws Exception {
        Class<?> imClass = Class.forName("android.hardware.input.InputManager");
        Object im = imClass.getMethod("getInstance").invoke(null);
        Object[] layouts = (Object[]) imClass.getMethod("getKeyboardLayouts").invoke(im);
        String hebrew = null, english = null;
        for (Object l : layouts) {
            String d = (String) l.getClass().getMethod("getDescriptor").invoke(l);
            String low = d.toLowerCase();
            if (hebrew == null && low.contains("hebrew")) hebrew = d;
            if (english == null && low.endsWith("keyboard_layout_english_us")) english = d;
        }
        if (hebrew == null) { out.println("K\t0\tno hebrew layout on this device"); return; }

        Class<?> identClass = Class.forName("android.hardware.input.InputDeviceIdentifier");
        java.lang.reflect.Method add, setCurrent;
        try {
            add = imClass.getMethod("addKeyboardLayoutForInputDevice", identClass, String.class);
            setCurrent = imClass.getMethod("setCurrentKeyboardLayoutForInputDevice", identClass, String.class);
        } catch (NoSuchMethodException e) {
            // Android 14+ picks the physical layout from the keyboard app's language instead.
            out.println("K\t-1\tautomatic (Android 14+)");
            return;
        }
        int done = 0;
        for (int id : (int[]) imClass.getMethod("getInputDeviceIds").invoke(im)) {
            android.view.InputDevice dev = (android.view.InputDevice) imClass.getMethod("getInputDevice", int.class).invoke(im, id);
            if (dev == null || !"scrcpy".equals(dev.getName())) continue;
            Object ident = dev.getClass().getMethod("getIdentifier").invoke(dev);
            if (english != null) add.invoke(im, ident, english);
            add.invoke(im, ident, hebrew);
            setCurrent.invoke(im, ident, current.equals("english") && english != null ? english : hebrew);
            done++;
        }
        out.println("K\t" + done + "\t" + hebrew + "\t" + sampleA());
    }

    // What the A key of scrcpy's keyboard(s) types right now - shows which layout is active.
    static String sampleA() throws Exception {
        Class<?> imClass = Class.forName("android.hardware.input.InputManager");
        Object im = imClass.getMethod("getInstance").invoke(null);
        String sample = "";
        for (int id : (int[]) imClass.getMethod("getInputDeviceIds").invoke(im)) {
            android.view.InputDevice dev = (android.view.InputDevice) imClass.getMethod("getInputDevice", int.class).invoke(im, id);
            if (dev != null && "scrcpy".equals(dev.getName()))
                sample += (char) dev.getKeyCharacterMap().get(android.view.KeyEvent.KEYCODE_A, 0);
        }
        return sample;
    }

    static boolean reported;

    // PackageManager.getApplicationIcon() falls back to the default robot when called from the shell,
    // so load the drawable from the app's own resources first.
    static Drawable icon(PackageManager pm, ApplicationInfo ai) {
        if (ai.icon != 0) {
            try {
                android.content.res.Resources r = pm.getResourcesForApplication(ai);
                Drawable d = Build.VERSION.SDK_INT >= 22
                        ? r.getDrawableForDensity(ai.icon, 480, null)
                        : r.getDrawableForDensity(ai.icon, 480);
                if (d != null) return d;
            } catch (Throwable t) {
                if (!reported) { reported = true; System.err.println("icon: " + t); }
            }
        }
        return pm.getApplicationIcon(ai);
    }

    static String clean(String s) {
        return s == null ? "" : s.replace('\t', ' ').replace('\n', ' ').replace('\r', ' ');
    }

    static String png(Drawable d, int px) {
        Bitmap b = Bitmap.createBitmap(px, px, Bitmap.Config.ARGB_8888);
        Canvas c = new Canvas(b);
        d.setBounds(0, 0, px, px);
        d.draw(c);
        ByteArrayOutputStream bo = new ByteArrayOutputStream();
        b.compress(Bitmap.CompressFormat.PNG, 100, bo);
        return Base64.encodeToString(bo.toByteArray(), Base64.NO_WRAP);
    }
}
