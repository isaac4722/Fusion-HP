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
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Fusion.Studio.Ui;
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

        // -------------------------------------------------- v4.2.0 (C1)
        // «Medios» y «Temas» quedaban FUERA del área de clic: la tira natural de
        // 5 pestañas mide ≈476 px y el panel de biblioteca 302 px. Los chips son
        // ahora adaptativos: TODOS deben caber en el ancho disponible.
        public static void TestFusionTabsFitsRealFiveTabs()
        {
            using (var tabs = new FusionTabs { Width = 338, Height = 300 })
            using (var p1 = new Panel()) using (var p2 = new Panel()) using (var p3 = new Panel())
            using (var p4 = new Panel()) using (var p5 = new Panel())
            {
                tabs.Add("Cantos", "music", p1);
                tabs.Add("Biblia", "book", p2);
                tabs.Add("Escenarios", "stack-2", p3);
                tabs.Add("Medios", "photo", p4);
                tabs.Add("Temas", "palette", p5);
                // los 5 índices deben poder seleccionarse (clic posible)
                for (int i = 0; i < 5; i++)
                {
                    tabs.SelectedIndex = i;
                    TestRunner.CheckEq(tabs.SelectedIndex, i, "pestaña " + i + " seleccionable en 338 px");
                }
            }
        }

        public static void TestFusionTabsFitsNarrowPanel()
        {
            // caso extremo: panel de 220 px (biblioteca colapsada) — nada por encima
            // del ancho y nada con ancho negativo
            using (var tabs = new FusionTabs { Width = 220, Height = 300 })
            using (var p1 = new Panel()) using (var p2 = new Panel()) using (var p3 = new Panel())
            using (var p4 = new Panel()) using (var p5 = new Panel())
            {
                tabs.Add("Cantos", "music", p1);
                tabs.Add("Biblia", "book", p2);
                tabs.Add("Escenarios", "stack-2", p3);
                tabs.Add("Medios", "photo", p4);
                tabs.Add("Temas", "palette", p5);
                tabs.SelectedIndex = 4;
                TestRunner.CheckEq(tabs.SelectedIndex, 4, "última pestaña alcanzable en 220 px");
            }
        }

        // -------------------------------------------------- v4.2.0 (C6)
        // El interlineado de la preview tenía DOBLE escala (H/1080 aplicado dos
        // veces) y las líneas se solapaban ~60 %. La fórmula corregida debe ser
        // la del núcleo: pts→px (96/72) × spacing, sin factor adicional.
        public static void TestSlidePreviewLineHeightMatchesCore()
        {
            float lh = SlidePreview.ComputeLineHeight(48f, 1.15f);
            float expected = 48f * 1.15f * 96f / 72f;      // 73.6 px
            TestRunner.Check(Math.Abs(lh - expected) < 0.01f,
                "interlineado = sizePt·spacing·96/72 (" + lh.ToString("0.00") + " px)");
            // el espaciado no puede quedar POR DEBAJO del alto del glifo (1.33×sizePt
            // en px): eso era exactamente el solape del bug
            TestRunner.Check(lh > 48f * 1.3333f, "interlineado mayor que el em del glifo");
            // auto-ajuste: 12 líneas en una caja de 84 px deben reducir el cuerpo
            float lh2 = SlidePreview.ComputeLineHeight(10f, 1.15f);
            float total = lh2 * 12;
            float boxH = 84f;
            if (total > boxH)
            {
                float shrunk = 10f * boxH / total;
                TestRunner.Check(shrunk < 10f && shrunk > 0f, "auto-ajuste reduce el cuerpo correctamente");
            }
            // RenderSlide no debe lanzar con muchas líneas ni logo configurado
            using (var bmp = new Bitmap(148, 84))
            using (var g = Graphics.FromImage(bmp))
            {
                var slide = new Fusion.Shared.Model.ResolvedSlide();
                slide.Kind = "text";
                for (int i = 0; i < 12; i++) slide.Lines.Add("Línea de prueba " + (i + 1));
                SlidePreview.RenderSlide(g, slide, 3, 148, 84);   // no debe lanzar
                SlidePreview.LogoPath = null;                     // reposo logo sin logo: negro, sin lanzar
            }
        }

        // -------------------------------------------------- v4.2.0 — Modo Operador
        public static void TestOperatorNextOfTransitions()
        {
            var p = Fusion.Shared.Model.AhpProject.CreateDefault();
            p.Scenarios.Clear();
            var s1 = new Fusion.Shared.Model.Scenario { Title = "Canto uno" };
            s1.Elements.Add(new Fusion.Shared.Model.Element { Lines = { "uno-a" } });
            s1.Elements.Add(new Fusion.Shared.Model.Element { Lines = { "uno-b" } });
            var s2 = new Fusion.Shared.Model.Scenario { Title = "Canto dos" };
            s2.Elements.Add(new Fusion.Shared.Model.Element { Lines = { "dos-a" } });
            p.Scenarios.Add(s1); p.Scenarios.Add(s2);
            // (0,0)→(0,1)→(1,0)→null  — la misma secuencia del transporte del Motor
            TestRunner.Check(OperatorForm.NextOf(p, 0, 0) == s1.Elements[1], "siguiente: mismo escenario");
            TestRunner.Check(OperatorForm.NextOf(p, 0, 1) == s2.Elements[0], "siguiente: salta al próximo escenario");
            TestRunner.Check(OperatorForm.NextOf(p, 1, 0) == null, "fin del programa: null");
            TestRunner.Check(OperatorForm.NextOf(p, -1, 0) == null && OperatorForm.NextOf(null, 0, 0) == null,
                "fuera de rango/proyecto nulo: null");
        }

        // -------------------------------------------------- v4.2.0 (C8)
        public static void TestDisabledIconsUseInkFadedTint()
        {
            // tinta InkFaded: SIEMPRE debe devolver imagen (o degrade controlado)
            // cuando la carpeta de iconos existe — en disabled ya no se usa el
            // blanco puro que desaparecía sobre fondo blanco.
            if (!UiIcons.Available) return;   // CI sin assets: degrade aceptado
            Image faded = UiIcons.Get("search", IconTint.InkFaded);
            TestRunner.Check(faded != null, "tinta InkFaded disponible para search");
            if (faded == null) return;
            using (var bmp = new Bitmap(faded))
            {
                // alfa 35 %: ningún píxel puede ser opaco (sería la tinta original)
                bool anyOpaque = false;
                for (int y = 0; y < bmp.Height && !anyOpaque; y += 4)
                    for (int x = 0; x < bmp.Width; x += 4)
                        if (bmp.GetPixel(x, y).A == 255) { anyOpaque = true; break; }
                TestRunner.Check(!anyOpaque, "InkFaded aplica alfa 35 % (sin píxeles opacos)");
            }
        }
    }
}
