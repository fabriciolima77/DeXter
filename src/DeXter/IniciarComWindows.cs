// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Win32;

namespace DeXter;

/// <summary>"Iniciar com o Windows" pela chave Run do usuario (sem pacote, sem administrador).</summary>
internal static class IniciarComWindows
{
    private const string Chave = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Nome = "DeXter";

    private static string Comando => $"\"{Environment.ProcessPath}\"";

    public static bool Ligado
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(Chave);
                return k?.GetValue(Nome) is string v && v.Equals(Comando, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    public static void Definir(bool ligar)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Chave);
        if (ligar)
        {
            k.SetValue(Nome, Comando);
        }
        else if (k.GetValue(Nome) != null)
        {
            k.DeleteValue(Nome);
        }
    }
}
