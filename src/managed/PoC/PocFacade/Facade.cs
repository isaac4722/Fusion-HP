// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Facade.cs : fachada COM-visible (SIN registro: la activa el hosting API del
//  PoC nativo clrhost vía _AppDomain::CreateInstanceFrom — vía C de la
//  arquitectura, disponible como fallback). El PoC.ClrHost nativo (otro agente)
//  invoca Add/Echo/Version para verificar que el CLR hospedado ejecuta código
//  gestionado de LuminaPresentation.
//
//  GUID fijo de ensamblado: la identidad de tipo COM debe ser estable entre
//  compilaciones para que el lado nativo resuelva siempre el mismo CLSID.
// ============================================================================
using System;
using System.Runtime.InteropServices;

[assembly: ComVisible(false)]
[assembly: Guid("8F4B4D2A-91C7-4B0E-9F6A-3B2A5C7D1E10")]

namespace lumina.poc
{
    /// <summary>
    /// Fachada mínima COM-visible (AutoDual → IDispatch + vtable temprana).
    /// Métodos estáticos y de instancia: el host nativo puede usar ambos
    /// estilos según la vía de activación.
    /// </summary>
    [ComVisible(true)]
    [Guid("5A9E3C41-7D2B-4E8F-9C10-6B4F2A8D3E20")]
    [ProgId("Lumina.PocFacade.LuminaFacade")]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class LuminaFacade
    {
        /// <summary>Marcador estable de la fachada (lo asserta el PoC nativo).</summary>
        public static string Version()
        {
            return "LUMINA-FACADE-OK-3.0";
        }

        /// <summary>Suma estática (smoke test de marshaling de enteros).</summary>
        public static int AddStatic(int a, int b)
        {
            return a + b;
        }

        /// <summary>Suma de instancia (smoke test de creación de objeto COM).</summary>
        public int Add(int a, int b)
        {
            return a + b;
        }

        /// <summary>Eco de cadena (smoke test de marshaling BSTR/Unicode).</summary>
        public string Echo(string s)
        {
            return s ?? string.Empty;
        }
    }
}
