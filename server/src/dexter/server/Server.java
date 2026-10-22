// SPDX-License-Identifier: Apache-2.0
// Derivado do ScrcpyDeX (https://github.com/Aureliano021/ScrcpyDex)
// Copyright 2026 Aureliano Peixoto and ScrcpyDeX Contributors. Licenciado sob a Apache License 2.0
// (veja server/LICENSE-ScrcpyDeX.txt).
// Modificacoes para o DeXter: saida em linhas DEXTER_* para o app no PC; encerra o processo quando a
// sessao do DeX termina; feito para rodar destacado (setsid/nohup), sobrevivendo a troca cabo <-> Wi-Fi.
package dexter.server;

import android.os.Handler;
import android.os.Looper;

import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Servidor do DeXter, executado no celular via app_process como usuario shell (sem root).
 *
 *   activate   : faz o Wi-Fi Display da Samsung conectar no proprio celular (127.0.0.1) em modo
 *                "DeX sem fio"; o DeX abre num display real (sem a janelinha do display simulado).
 *                Imprime DEXTER_DISPLAY_ID=<id> e fica rodando ate ser encerrado.
 *   disconnect : encerra qualquer sessao de Wi-Fi Display/DeX.
 *
 * Linhas para o app no PC:
 *   DEXTER_DISPLAY_ID=<id>   DeX pronto
 *   DEXTER_INCOMPATIVEL=<m>  aparelho/firmware sem a API usada (o PC usa o modo compatibilidade)
 *   DEXTER_ERRO=<m>          falhou
 *   DEXTER_FIM               sessao encerrada
 */
public final class Server {
    private static final AtomicBoolean running = new AtomicBoolean(true);

    private static DisplayManager displayManager;
    private static DexActivator dexActivator;

    public static void main(String[] args) {
        if (args.length == 0 || (!"activate".equalsIgnoreCase(args[0]) && !"disconnect".equalsIgnoreCase(args[0]))) {
            System.err.println("Uso: app_process / dexter.server.Server [activate|disconnect]");
            System.exit(1);
            return;
        }

        if (Looper.myLooper() == null) {
            Looper.prepare();
        }
        Handler mainHandler = new Handler(Looper.myLooper());

        Workarounds.apply();
        displayManager = new DisplayManager();

        if ("disconnect".equalsIgnoreCase(args[0])) {
            displayManager.disconnectWifiDisplay();
            System.out.println("DEXTER_FIM");
            return;
        }

        Runtime.getRuntime().addShutdownHook(new Thread(Server::cleanup, "DeXter-Cleanup"));

        try {
            CompatCheck.verify();

            DisplayWatch displayWatch = new DisplayWatch();
            displayWatch.startListening(mainHandler);

            dexActivator = new DexActivator(displayManager);
            dexActivator.setAoTerminar(() -> {
                Ln.i("Sessao RTSP terminou; encerrando o servidor.");
                parar();
            });
            if (!dexActivator.activate()) {
                throw new RuntimeException("O celular nao completou a conexao do DeX (RTSP).");
            }

            int displayId = displayWatch.waitForDisplay(15);
            if (displayId == -1) {
                throw new RuntimeException("O display do DeX nao apareceu em 15 s.");
            }
            System.out.println("DEXTER_DISPLAY_ID=" + displayId);
            System.out.flush();

            synchronized (Server.class) {
                while (running.get()) {
                    try {
                        Server.class.wait();
                    } catch (InterruptedException ignored) {
                    }
                }
            }
        } catch (IncompatibleDeviceException ide) {
            System.out.println("DEXTER_INCOMPATIVEL=" + ide.getMessage());
            Ln.e("Incompativel: " + ide.getMessage());
        } catch (Throwable t) {
            System.out.println("DEXTER_ERRO=" + t.getMessage());
            Ln.e("Erro no servidor do DeXter: " + t.getMessage(), t);
        } finally {
            cleanup();
        }
        System.exit(0);
    }

    private static void parar() {
        running.set(false);
        synchronized (Server.class) {
            Server.class.notifyAll();
        }
    }

    private static void cleanup() {
        if (dexActivator == null && displayManager == null) {
            return;
        }
        parar();
        DexActivator a = dexActivator;
        DisplayManager d = displayManager;
        dexActivator = null;
        displayManager = null;
        if (a != null) {
            a.stop();
        }
        if (d != null) {
            d.disconnectWifiDisplay();
        }
        System.out.println("DEXTER_FIM");
        System.out.flush();
    }
}
