// SPDX-License-Identifier: GPL-3.0-or-later
using System.Reflection;

namespace DeXter;

/// <summary>Dados fixos do projeto.</summary>
internal static class Projeto
{
    /// <summary>Pagina do projeto no GitHub (menu "Sobre").</summary>
    public const string Url = "https://github.com/fabriciolima77/DeXter";

    /// <summary>Precisa bater com o TaskId da StartupTask no AppxManifest.</summary>
    public const string IdTarefaInicio = "DeXterStartup";

    public static string Versao { get; } =
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?")
        .Split('+')[0];
}
