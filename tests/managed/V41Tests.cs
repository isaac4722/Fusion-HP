// ============================================================================
//  Fusion-HP · tests/managed/V41Tests.cs — pruebas de la v4.0.1:
//   · NRE al arrancar (cs.domain 2026-09-25 20:48): FusionInput.OnResize leía
//     «inner» nulo porque el ctor fijaba Height ANTES de crear el TextBox
//     interno; OnResize es virtual y se despachaba desde el ctor de la base
//     hasta FusionSearchBox.OnResize. La pila real: Program.Main → MainForm..
//     ctor → BuildPresent → BuildLibrary → FusionSearchBox..ctor → NRE.
//   · Estas pruebas construyen TODA la superficie de widgets (lo que
//     MainForm hace al arrancar) sin bomba de mensajes: si vuelve a romperse
//     el orden ctor/bounds, la suite revienta aquí y no en la máquina del
//     usuario. Ningún handle se crea: todo es construcción pura.
//  Descubiertas por reflexión (TestRunner): métodos públicos estáticos Test*.
// ============================================================================
using System;
using System.Windows.Forms;
using Fusion.Studio.Ui.Widgets;
using Fusion.Tests;

namespace Fusion.Tests
{
    static class V41Tests
    {
        // -------------------------------------------------- FusionInput
        public static void TestFusionInputConstructsAndResizes()
        {
            using (var input = new FusionInput())
            {
                TestRunner.Check(input.Height == 30, "FusionInput alto por defecto 30");
                input.Text = "texto";
                TestRunner.CheckEq(input.Text, "texto", "Text delega en el inner");
                input.Width = 260;   // dispara OnResize con inner ya creado
                input.Height = 34;
                TestRunner.Check(input.Inner != null && input.Inner.Width > 0,
                    "OnResize maqueta el inner sin NRE");
                input.ReadOnly = true;
                TestRunner.Check(input.ReadOnly, "ReadOnly delega en el inner");
            }
        }

        // ---------------------------------------------- regreso exacto v4.0.1
        public static void TestFusionSearchBoxCtorOrderRegression()
        {
            // REGRESIÓN EXACTA del cs.domain 2026-09-25: new FusionSearchBox()
            // → ctor de FusionInput → Height=30 → SetBounds → OnSizeChanged →
            // OnResize virtual → «inner» aún nulo → NullReferenceException.
            // BuildLibrary lo crea a 272×30 y el de Biblia a Dock.Top alto 30.
            using (var sb = new FusionSearchBox())
            {
                TestRunner.Check(sb.Inner != null, "inner ya existe tras el ctor");
                sb.Placeholder = "Buscar canto";
                sb.Width = 272; sb.Height = 30;
                sb.Text = "Salmos";
                TestRunner.CheckEq(sb.Text, "Salmos", "búsqueda escribe en el inner");
                sb.Text = "";
                TestRunner.Check(!sb.Inner.IsDisposed, "inner sano tras limpiar");
            }
        }

        // -------------------------------------------------- resto de widgets
        public static void TestTabsAndButtonsConstruct()
        {
            using (var tabs = new FusionTabs())
            using (var page = new Panel())
            using (var btn = new FusionButton())
            using (var ib = new FusionIconButton())
            {
                tabs.Width = 400; tabs.Height = 300;
                int idx = tabs.Add("Inicio", "home", page);
                TestRunner.CheckEq(idx, 0, "primera pestaña índice 0");
                tabs.SelectedIndex = idx;          // idempotente: no debe lanzar
                btn.Kind = FusionButtonKind.Primary;
                btn.Text = "Proyectar";
                TestRunner.CheckEq(btn.Text, "Proyectar", "botón modelado");
                btn.Width = 120; btn.Height = 36;  // sin OnResize propio: seguro
                TestRunner.Check(ib.Width == 34 && ib.Height == 34, "icono 34×34");
            }
        }
    }
}
