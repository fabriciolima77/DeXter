// SPDX-License-Identifier: Apache-2.0
// Derivado do ScrcpyDeX (https://github.com/Aureliano021/ScrcpyDex)
// Copyright 2026 Aureliano Peixoto and ScrcpyDeX Contributors. Licenciado sob a Apache License 2.0
// (veja server/LICENSE-ScrcpyDeX.txt). Modificacoes para o DeXter: veja o comentario de cada classe.
package dexter.server;

import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;

/**
 * Logging utility for ScrcpyDex Server.
 * 
 * Writes directly to stdout/stderr with clean formatting and timestamps.
 * Because the server runs via app_process (native command-line process
 * on Android), logs are directed to the standard process output, which can be
 * read directly via ADB or redirected to a log file.
 */
public final class Ln {
    private static final String TAG = "[DeXter]";
    private static final SimpleDateFormat DATE_FORMAT = 
            new SimpleDateFormat("HH:mm:ss.SSS", Locale.US);

    private Ln() {
        // Static utility class
    }

    private static String getTimestamp() {
        return DATE_FORMAT.format(new Date());
    }

    public static void i(String message) {
        System.out.println(getTimestamp() + " " + TAG + " [INFO] " + message);
    }

    public static void w(String message) {
        System.out.println(getTimestamp() + " " + TAG + " [WARN] " + message);
    }

    public static void e(String message) {
        System.err.println(getTimestamp() + " " + TAG + " [ERROR] " + message);
    }

    public static void e(String message, Throwable throwable) {
        System.err.println(getTimestamp() + " " + TAG + " [ERROR] " + message);
        if (throwable != null) {
            throwable.printStackTrace(System.err);
        }
    }

    public static void d(String message) {
        // Enabled for debugging during the interoperability analysis phase
        System.out.println(getTimestamp() + " " + TAG + " [DEBUG] " + message);
    }
}
