// SPDX-License-Identifier: Apache-2.0
// Derivado do ScrcpyDeX (https://github.com/Aureliano021/ScrcpyDex)
// Copyright 2026 Aureliano Peixoto and ScrcpyDeX Contributors. Licenciado sob a Apache License 2.0
// (veja server/LICENSE-ScrcpyDeX.txt). Modificacoes para o DeXter: veja o comentario de cada classe.
package dexter.server;

/**
 * Exception thrown when the device does not meet the minimum hardware
 * or software requirements to run ScrcpyDeX (e.g., non-Samsung device,
 * Android version below 11, or absence of SemWifiDisplayConfig APIs).
 */
public class IncompatibleDeviceException extends RuntimeException {
    public IncompatibleDeviceException(String message) {
        super(message);
    }

    public IncompatibleDeviceException(String message, Throwable cause) {
        super(message, cause);
    }
}
