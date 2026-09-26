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
using System.IO;
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

        // ------------------------------------- estados: disabled / navegación
        public static void TestWidgetDisabledStatesAndNavigation()
        {
            // FusionInput: Enabled=false sincroniza el inner y pinta tenue
            using (var input = new FusionInput())
            {
                input.Enabled = false;
                TestRunner.Check(!input.Inner.Enabled, "FusionInput: inner deshabilitado en sincronía");
                input.Width = 200; input.Height = 30;   // OnResize con inner deshabilitado: sin NRE
                TestRunner.Check(input.Inner.Width > 0, "FusionInput: layout sano en disabled");
                input.Enabled = true;
                TestRunner.Check(input.Inner.Enabled, "FusionInput: re-habilitación sincronizada");
            }
            // FusionSearchBox: disabled + cue banner no revienta
            using (var sb = new FusionSearchBox())
            {
                sb.Placeholder = "Buscar";
                sb.Enabled = false;
                sb.Width = 272;
                sb.Enabled = true;
                TestRunner.Check(sb.Inner != null && !sb.Inner.IsDisposed, "FusionSearchBox: disabled/re-enabled sano");
            }
            // FusionIconButton: el pintado con alpha no revienta (sin handle, dry)
            using (var ib = new FusionIconButton())
            {
                ib.IconName = "search";
                ib.Enabled = false;
                ib.Enabled = true;
                TestRunner.Check(true, "FusionIconButton: transición Enabled estable");
            }
            // FusionTabs: navegación deja páginas visibles correctas y sin estado roto
            using (var tabs = new FusionTabs())
            {
                tabs.Width = 500; tabs.Height = 400;
                var p1 = new Panel(); var p2 = new Panel(); var p3 = new Panel();
                tabs.Add("A", "home", p1);
                tabs.Add("B", "book", p2);
                tabs.Add("C", "photo", p3);
                tabs.SelectedIndex = 1;
                TestRunner.Check(!p1.Visible && p2.Visible && !p3.Visible, "FusionTabs: selección B visible");
                tabs.SelectedIndex = 2;
                TestRunner.Check(!p1.Visible && !p2.Visible && p3.Visible, "FusionTabs: salto a C correcto");
                tabs.Enabled = false;
                tabs.SelectedIndex = 0;   // con Enabled=false el set programático sigue (API estable)
                TestRunner.Check(p1.Visible, "FusionTabs: API programática activa en disabled");
            }
            // FusionTabs.Add(null) → contracto claro
            using (var tabs2 = new FusionTabs())
            {
                bool threw = false;
                try { tabs2.Add("X", "x", null); }
                catch (ArgumentNullException) { threw = true; }
                TestRunner.Check(threw, "FusionTabs: Add(null) → ArgumentNullException");
            }
        }

        // ------------------------------------ iconos: 0 botones sin glifo
        public static void TestAllUsedIconsExist()
        {
            // nombres usados por el código de UI (auditoría v4.1.0); si alguien
            // añade un IconName sin PNG, esta prueba lo detecta en CI
            string[] used = {
                "activity","app-window-bottom","bolt","book","check","chevrons-left","chevrons-right",
                "clock","device-desktop","download","eye","eye-off","file-text","folder-open","heading",
                "home","hourglass","layout-grid","list","movie","music","palette","pencil","photo","piano",
                "player-play","player-skip-back","player-skip-forward","plus","refresh","search","settings",
                "square","stack-2","trash","upload","wand","x","folder"
            };
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string icons = null;
            for (string d = root; d != null && d.Length > 3; d = Path.GetDirectoryName(d))
            {
                string cand = Path.Combine(Path.Combine(Path.Combine(d, "resources"), "img"), "icons");
                if (Directory.Exists(cand)) { icons = cand; break; }
            }
            TestRunner.Check(icons != null, "iconos: carpeta resources/img/icons localizada");
            if (icons == null) return;
            int missing = 0; string missingList = "";
            foreach (string name in used)
            {
                string p = Path.Combine(Path.Combine(Path.Combine(icons, "ink20"), name + ".png"));
                if (!File.Exists(p)) { missing++; missingList += " " + name; }
            }
            TestRunner.CheckEq(missing, 0, "iconos usados presentes en ink20" + missingList);
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
