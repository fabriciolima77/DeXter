// SPDX-License-Identifier: GPL-3.0-or-later
// DeXter - usa o Samsung DeX numa janela do Windows (app de bandeja).
// O PC se anuncia como "TV" Miracast; quando o celular inicia o DeX sem fio,
// o app descobre o display do DeX e abre o scrcpy nele (video + mouse/teclado).

namespace DeXter;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var instancia = new Mutex(initiallyOwned: true, @"Local\DeXter.InstanciaUnica", out var primeira);
        if (!primeira)
        {
            MessageBox.Show("O DeXter ja esta rodando (veja o icone na bandeja, perto do relogio).",
                "DeXter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Erro("excecao na UI", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Log.Erro("excecao nao tratada", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Erro("tarefa com excecao nao observada", e.Exception);
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
        GC.KeepAlive(instancia);
    }
}
